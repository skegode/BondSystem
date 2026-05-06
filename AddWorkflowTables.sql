-- ══════════════════════════════════════════════════════════════
-- AddWorkflowTables.sql
-- Run ONCE against OnwardsSwiftDB after all previous migrations.
-- Safe to re-run — all wrapped in IF NOT EXISTS checks.
-- ══════════════════════════════════════════════════════════════
USE OnwardsSwiftDB;
GO

-- ── WorkflowStages ────────────────────────────────────────────
-- Created here if not already in the database. The CanReturn and
-- ReturnToStepOrder columns are added if the table already exists.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowStages')
BEGIN
    CREATE TABLE WorkflowStages (
        Id                INT          IDENTITY(1,1) PRIMARY KEY,
        ModuleType        NVARCHAR(20) NOT NULL,   -- 'BOND' or 'CHEQUE'
        StageName         NVARCHAR(200) NOT NULL,
        SequenceOrder     INT          NOT NULL,
        IsFinalStage      BIT          NOT NULL DEFAULT 0,
        CanReturn         BIT          NOT NULL DEFAULT 0,
        ReturnToStepOrder INT          NULL,       -- SequenceOrder to return to; NULL = applicant
        CreatedAt         DATETIME     NOT NULL DEFAULT GETDATE()
    );
    PRINT 'Created WorkflowStages';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'WorkflowStages' AND COLUMN_NAME = 'CanReturn')
BEGIN
    ALTER TABLE WorkflowStages ADD CanReturn BIT NOT NULL DEFAULT 0;
    ALTER TABLE WorkflowStages ADD ReturnToStepOrder INT NULL;
    PRINT 'Added CanReturn / ReturnToStepOrder to WorkflowStages';
END
GO

-- ── WorkflowApprovers ─────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowApprovers')
BEGIN
    CREATE TABLE WorkflowApprovers (
        Id      INT          IDENTITY(1,1) PRIMARY KEY,
        StageId INT          NOT NULL,
        UserId  NVARCHAR(50) NOT NULL  -- stored as string; holds INT cast to NVARCHAR
    );
    PRINT 'Created WorkflowApprovers';
END
GO

-- ── WorkflowStageDocuments ────────────────────────────────────
-- Documents that must be present before an approver can act at a stage.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WorkflowStageDocuments')
BEGIN
    CREATE TABLE WorkflowStageDocuments (
        Id           INT           IDENTITY(1,1) PRIMARY KEY,
        StageId      INT           NOT NULL REFERENCES WorkflowStages(Id) ON DELETE CASCADE,
        DocumentName NVARCHAR(200) NOT NULL,
        IsRequired   BIT           NOT NULL DEFAULT 1,
        CreatedAt    DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_WorkflowStageDocuments_Stage ON WorkflowStageDocuments(StageId);
    PRINT 'Created WorkflowStageDocuments';
END
GO

-- ── FacilityApprovalInstances ─────────────────────────────────
-- One row per Bond / Cheque being processed; tracks the current stage.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FacilityApprovalInstances')
BEGIN
    CREATE TABLE FacilityApprovalInstances (
        Id             INT          IDENTITY(1,1) PRIMARY KEY,
        ReferenceId    INT          NOT NULL,
        ModuleType     NVARCHAR(20) NOT NULL,   -- 'BOND' or 'CHEQUE'
        CurrentStageId INT          NULL REFERENCES WorkflowStages(Id),
        Status         NVARCHAR(20) NOT NULL DEFAULT 'PENDING', -- PENDING | APPROVED | REJECTED | RETURNED
        InitiatedAt    DATETIME     NOT NULL DEFAULT GETDATE(),
        CompletedAt    DATETIME     NULL
    );
    CREATE UNIQUE INDEX IX_FacilityApprovalInstances_Ref ON FacilityApprovalInstances(ReferenceId, ModuleType);
    PRINT 'Created FacilityApprovalInstances';
END
GO

-- ── FacilityApprovalActions ───────────────────────────────────
-- Full audit trail of every approve / reject / return action.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'FacilityApprovalActions')
BEGIN
    CREATE TABLE FacilityApprovalActions (
        Id           INT           IDENTITY(1,1) PRIMARY KEY,
        InstanceId   INT           NOT NULL REFERENCES FacilityApprovalInstances(Id),
        StageId      INT           NULL,
        StageName    NVARCHAR(200) NULL,
        ActionType   NVARCHAR(30)  NOT NULL,  -- APPROVED | REJECTED | RETURNED | DOC_UPLOADED
        ActionById   INT           NOT NULL,
        ActionByName NVARCHAR(200) NOT NULL,
        Comment      NVARCHAR(1000) NULL,
        ActionAt     DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_FacilityApprovalActions_Inst ON FacilityApprovalActions(InstanceId);
    PRINT 'Created FacilityApprovalActions';
END
GO

-- ── ApprovalDocuments ─────────────────────────────────────────
-- Files uploaded by applicants or approvers during the approval flow.
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ApprovalDocuments')
BEGIN
    CREATE TABLE ApprovalDocuments (
        Id             INT           IDENTITY(1,1) PRIMARY KEY,
        ReferenceId    INT           NOT NULL,
        ModuleType     NVARCHAR(20)  NOT NULL,
        DocumentName   NVARCHAR(200) NOT NULL,
        FilePath       NVARCHAR(500) NOT NULL,
        UploadedById   INT           NOT NULL,
        UploadedByName NVARCHAR(200) NOT NULL,
        UploadedAt     DATETIME      NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_ApprovalDocuments_Ref ON ApprovalDocuments(ReferenceId, ModuleType);
    PRINT 'Created ApprovalDocuments';
END
GO

PRINT '=== AddWorkflowTables.sql complete ===';
GO
