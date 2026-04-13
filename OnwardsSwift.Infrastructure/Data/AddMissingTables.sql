-- ============================================================
--  Onwards Swift — AddMissingTables.sql
--  Run this ONCE against OnwardsSwiftDB after the initial
--  InitialMigration.sql has already been applied.
--  Safe to re-run — all statements use IF NOT EXISTS.
-- ============================================================

USE OnwardsSwiftDB;
GO

-- ── SYSTEM USERS ─────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemUsers' AND xtype='U')
BEGIN
CREATE TABLE SystemUsers (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FullName        NVARCHAR(200)       NOT NULL,
    Email           NVARCHAR(200)       NOT NULL,
    Phone           NVARCHAR(30)        NULL,
    PasswordHash    NVARCHAR(500)       NOT NULL,
    Role            INT                 NOT NULL DEFAULT 0,
    -- 0=Admin,1=RelationshipManager,2=CreditOfficer,3=Auditor,4=Client
    Department      NVARCHAR(100)       NULL,
    IsActive        BIT                 NOT NULL DEFAULT 1,
    LastLoginAt     DATETIME2           NULL,
    LinkedClientId  UNIQUEIDENTIFIER    NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IX_SystemUsers_Email ON SystemUsers(Email) WHERE IsDeleted = 0;
END
GO

-- ── BANK PARTNERS ─────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BankPartners' AND xtype='U')
BEGIN
CREATE TABLE BankPartners (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    Name            NVARCHAR(200)       NOT NULL,
    ShortCode       NVARCHAR(20)        NOT NULL,
    ContactEmail    NVARCHAR(200)       NULL,
    ContactPhone    NVARCHAR(30)        NULL,
    IsActive        BIT                 NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
END
GO

-- ── PRODUCT RATES ─────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ProductRates' AND xtype='U')
BEGIN
CREATE TABLE ProductRates (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    BankPartnerId   UNIQUEIDENTIFIER    NULL REFERENCES BankPartners(Id),
    ProductType     INT                 NOT NULL,  -- 1=BidBond,2=Invoice,3=Cheque
    Rate            DECIMAL(5,4)        NOT NULL,  -- stored as decimal e.g. 0.025 = 2.5%
    MinAdvancePct   DECIMAL(5,2)        NULL,
    MaxAdvancePct   DECIMAL(5,2)        NULL,
    MinTenorDays    INT                 NULL,
    MaxTenorDays    INT                 NULL,
    MinAmount       DECIMAL(18,2)       NULL,
    MaxAmount       DECIMAL(18,2)       NULL,
    IsDefault       BIT                 NOT NULL DEFAULT 0,
    IsActive        BIT                 NOT NULL DEFAULT 1,
    Notes           NVARCHAR(500)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
END
GO

-- ── APPROVAL WORKFLOWS ────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApprovalWorkflows' AND xtype='U')
BEGIN
CREATE TABLE ApprovalWorkflows (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    Name            NVARCHAR(200)       NOT NULL,
    ProductType     INT                 NULL,  -- NULL = applies to all products
    MinAmount       DECIMAL(18,2)       NULL,
    MaxAmount       DECIMAL(18,2)       NULL,
    IsActive        BIT                 NOT NULL DEFAULT 1,
    Description     NVARCHAR(500)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
END
GO

-- ── WORKFLOW STEPS ────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='WorkflowSteps' AND xtype='U')
BEGIN
CREATE TABLE WorkflowSteps (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    WorkflowId      UNIQUEIDENTIFIER    NOT NULL REFERENCES ApprovalWorkflows(Id) ON DELETE CASCADE,
    StepOrder       INT                 NOT NULL,
    StepName        NVARCHAR(200)       NOT NULL,
    RequiredRole    INT                 NULL,   -- maps to UserRole enum
    RequiredUserId  NVARCHAR(100)       NULL,   -- specific user override
    IsOptional      BIT                 NOT NULL DEFAULT 0,
    TimeoutHours    INT                 NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_WorkflowSteps_WorkflowId ON WorkflowSteps(WorkflowId);
END
GO

-- ── FACILITY APPROVAL INSTANCES ───────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApprovalInstances' AND xtype='U')
BEGIN
CREATE TABLE ApprovalInstances (
    Id                  UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId          UNIQUEIDENTIFIER    NOT NULL REFERENCES Facilities(Id),
    WorkflowId          UNIQUEIDENTIFIER    NOT NULL REFERENCES ApprovalWorkflows(Id),
    CurrentStepOrder    INT                 NOT NULL DEFAULT 1,
    IsCompleted         BIT                 NOT NULL DEFAULT 0,
    IsRejected          BIT                 NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2           NULL,
    CreatedBy           NVARCHAR(200)       NULL,
    UpdatedBy           NVARCHAR(200)       NULL,
    IsDeleted           BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_ApprovalInstances_FacilityId ON ApprovalInstances(FacilityId);
END
GO

-- ── APPROVAL ACTIONS ──────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApprovalActions' AND xtype='U')
BEGIN
CREATE TABLE ApprovalActions (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    InstanceId      UNIQUEIDENTIFIER    NOT NULL REFERENCES ApprovalInstances(Id) ON DELETE CASCADE,
    StepId          UNIQUEIDENTIFIER    NOT NULL REFERENCES WorkflowSteps(Id),
    StepOrder       INT                 NOT NULL,
    ActionBy        NVARCHAR(200)       NOT NULL,
    ActionByName    NVARCHAR(200)       NOT NULL,
    ActionType      NVARCHAR(20)        NOT NULL,  -- Approved | Rejected | Returned
    Comment         NVARCHAR(1000)      NULL,
    ActionAt        DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_ApprovalActions_InstanceId ON ApprovalActions(InstanceId);
END
GO

-- ── BID BOND DETAIL (extends Facilities) ─────────────────────
-- Adds BondSubtype and resale columns to BidBonds if missing
IF EXISTS (SELECT * FROM sysobjects WHERE name='BidBonds' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='BondSubtype')
        ALTER TABLE BidBonds ADD BondSubtype NVARCHAR(30) NULL;   -- BidBond | PerformanceBond | CustomsBond
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='IsResale')
        ALTER TABLE BidBonds ADD IsResale BIT NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='BankRate')
        ALTER TABLE BidBonds ADD BankRate DECIMAL(5,4) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='BankFee')
        ALTER TABLE BidBonds ADD BankFee DECIMAL(18,2) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='ResaleRevenue')
        ALTER TABLE BidBonds ADD ResaleRevenue DECIMAL(18,2) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='BondLifecycle')
        ALTER TABLE BidBonds ADD BondLifecycle NVARCHAR(20) NOT NULL DEFAULT 'Active';
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='RefundStatus')
        ALTER TABLE BidBonds ADD RefundStatus NVARCHAR(50) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='RefundAmount')
        ALTER TABLE BidBonds ADD RefundAmount DECIMAL(18,2) NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='RefundDate')
        ALTER TABLE BidBonds ADD RefundDate DATETIME2 NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='RefundNotes')
        ALTER TABLE BidBonds ADD RefundNotes NVARCHAR(1000) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BidBonds') AND name='ReleasePaymentMode')
        ALTER TABLE BidBonds ADD ReleasePaymentMode NVARCHAR(50) NULL;
END
GO

-- ── CLIENT APPROVAL COLUMNS ───────────────────────────────────
IF EXISTS (SELECT * FROM sysobjects WHERE name='Clients' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='ApprovedBy')
        ALTER TABLE Clients ADD ApprovedBy NVARCHAR(200) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='ApprovedAt')
        ALTER TABLE Clients ADD ApprovedAt DATETIME2 NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='ApprovalNotes')
        ALTER TABLE Clients ADD ApprovalNotes NVARCHAR(1000) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='RejectionReason')
        ALTER TABLE Clients ADD RejectionReason NVARCHAR(500) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='ApprovedProducts')
        ALTER TABLE Clients ADD ApprovedProducts NVARCHAR(200) NULL;
END
GO

-- ══════════════════════════════════════════════════════════════
--  SEED DATA
-- ══════════════════════════════════════════════════════════════

-- Admin user  (password = Admin@1234!  hashed with SHA256 + salt "OnwardsSwiftSalt2026")
-- Hash = SHA256("Admin@1234!OnwardsSwiftSalt2026")
IF NOT EXISTS (SELECT 1 FROM SystemUsers WHERE Email = 'admin@onwardsswift.com')
INSERT INTO SystemUsers (Id, FullName, Email, Phone, PasswordHash, Role, Department, IsActive, CreatedBy)
VALUES (NEWID(), 'System Admin', 'admin@onwardsswift.com', '+254700000001',
        'Zmyl0f0MqTKc3xHoM4b59nW70FpljtNoOYOVwHQeNDc=',
        1, 'Information Technology', 1, 'seed');  -- Admin=1

-- RM user  (password = Rm@1234!)
IF NOT EXISTS (SELECT 1 FROM SystemUsers WHERE Email = 'rm@onwardsswift.com')
INSERT INTO SystemUsers (Id, FullName, Email, Phone, PasswordHash, Role, Department, IsActive, CreatedBy)
VALUES (NEWID(), 'Relationship Manager', 'rm@onwardsswift.com', '+254700000002',
        'Kdp3K+zoff2FeSK5idsathcvRfuGTCD5panQBAU2GNM=',
        2, 'Business Development', 1, 'seed');  -- RelationshipManager=2

-- Credit Officer  (password = Credit@1234!)
IF NOT EXISTS (SELECT 1 FROM SystemUsers WHERE Email = 'credit@onwardsswift.com')
INSERT INTO SystemUsers (Id, FullName, Email, Phone, PasswordHash, Role, Department, IsActive, CreatedBy)
VALUES (NEWID(), 'Credit Officer', 'credit@onwardsswift.com', '+254700000003',
        'n79YD6zdhxb3XwCE/QXe/M3XU9fWitfXs+7nIQdY5cU=',
        3, 'Credit', 1, 'seed');  -- CreditOfficer=3

-- Default system rates
IF NOT EXISTS (SELECT 1 FROM ProductRates WHERE IsDefault=1 AND ProductType=1)
INSERT INTO ProductRates (Id, ProductType, Rate, MinTenorDays, MaxTenorDays, MinAmount, IsDefault, IsActive, Notes, CreatedBy)
VALUES (NEWID(), 1, 0.0250, 30, 365, 50000, 1, 1, 'Standard bid bond rate', 'seed');

IF NOT EXISTS (SELECT 1 FROM ProductRates WHERE IsDefault=1 AND ProductType=2)
INSERT INTO ProductRates (Id, ProductType, Rate, MaxAdvancePct, MinTenorDays, MaxTenorDays, MinAmount, IsDefault, IsActive, Notes, CreatedBy)
VALUES (NEWID(), 2, 0.0400, 80, 14, 120, 100000, 1, 1, 'Up to 80% advance on verified invoices', 'seed');

IF NOT EXISTS (SELECT 1 FROM ProductRates WHERE IsDefault=1 AND ProductType=3)
INSERT INTO ProductRates (Id, ProductType, Rate, MaxAdvancePct, MinTenorDays, MaxTenorDays, MinAmount, MaxAmount, IsDefault, IsActive, Notes, CreatedBy)
VALUES (NEWID(), 3, 0.0350, 85, 7, 90, 50000, 5000000, 1, 1, 'Post-dated cheques from CBK-regulated banks only', 'seed');

-- Default workflows
DECLARE @wf1 UNIQUEIDENTIFIER = NEWID();
DECLARE @wf2 UNIQUEIDENTIFIER = NEWID();
DECLARE @wf3 UNIQUEIDENTIFIER = NEWID();
DECLARE @wf4 UNIQUEIDENTIFIER = NEWID();

IF NOT EXISTS (SELECT 1 FROM ApprovalWorkflows WHERE Name = 'Standard Bid Bond Approval')
BEGIN
    INSERT INTO ApprovalWorkflows (Id,Name,ProductType,MaxAmount,IsActive,Description,CreatedBy)
    VALUES (@wf1,'Standard Bid Bond Approval',1,5000000,1,'Bid bonds up to KSh 5M','seed');
    INSERT INTO WorkflowSteps (Id,WorkflowId,StepOrder,StepName,RequiredRole,TimeoutHours,CreatedBy)
    VALUES (NEWID(),@wf1,1,'RM Review',2,24,'seed')  -- RM=2,
           (NEWID(),@wf1,2,'Credit Assessment',3,24,'seed')  -- CreditOfficer=3;
END

IF NOT EXISTS (SELECT 1 FROM ApprovalWorkflows WHERE Name = 'Large Bid Bond Approval')
BEGIN
    INSERT INTO ApprovalWorkflows (Id,Name,ProductType,MinAmount,IsActive,Description,CreatedBy)
    VALUES (@wf2,'Large Bid Bond Approval',1,5000001,1,'Bid bonds above KSh 5M','seed');
    INSERT INTO WorkflowSteps (Id,WorkflowId,StepOrder,StepName,RequiredRole,TimeoutHours,CreatedBy)
    VALUES (NEWID(),@wf2,1,'RM Review',1,12,'seed'),
           (NEWID(),@wf2,2,'Senior Credit Review',2,24,'seed'),
           (NEWID(),@wf2,3,'Management Approval',1,48,'seed')  -- Admin=1;
END

IF NOT EXISTS (SELECT 1 FROM ApprovalWorkflows WHERE Name = 'Invoice Discounting Approval')
BEGIN
    INSERT INTO ApprovalWorkflows (Id,Name,ProductType,IsActive,Description,CreatedBy)
    VALUES (@wf3,'Invoice Discounting Approval',2,1,'All invoice discounting facilities','seed');
    INSERT INTO WorkflowSteps (Id,WorkflowId,StepOrder,StepName,RequiredRole,TimeoutHours,CreatedBy)
    VALUES (NEWID(),@wf3,1,'Debtor Verification',2,24,'seed'),
           (NEWID(),@wf3,2,'Credit Approval',3,24,'seed');
END

IF NOT EXISTS (SELECT 1 FROM ApprovalWorkflows WHERE Name = 'Cheque Discounting Approval')
BEGIN
    INSERT INTO ApprovalWorkflows (Id,Name,ProductType,IsActive,Description,CreatedBy)
    VALUES (@wf4,'Cheque Discounting Approval',3,1,'All cheque discounting facilities','seed');
    INSERT INTO WorkflowSteps (Id,WorkflowId,StepOrder,StepName,RequiredRole,TimeoutHours,CreatedBy)
    VALUES (NEWID(),@wf4,1,'Cheque Verification',2,8,'seed'),
           (NEWID(),@wf4,2,'Credit Approval',3,24,'seed');
END

-- Seed partner banks
IF NOT EXISTS (SELECT 1 FROM BankPartners WHERE ShortCode='KCB')
INSERT INTO BankPartners (Id,Name,ShortCode,ContactEmail,ContactPhone,IsActive,CreatedBy)
VALUES
  (NEWID(),'KCB Bank Kenya Ltd','KCB','trade@kcb.co.ke','+254711087000',1,'seed'),
  (NEWID(),'Equity Bank Kenya Ltd','EQT','trade@equitybank.co.ke','+254763000000',1,'seed'),
  (NEWID(),'Co-operative Bank of Kenya','COOP','trade@co-opbank.co.ke','+254703027000',1,'seed'),
  (NEWID(),'NCBA Bank Kenya','NCBA','trade@ncbabank.co.ke','+254711056444',1,'seed'),
  (NEWID(),'ABSA Bank Kenya','ABSA','trade@absa.co.ke','+254703023000',1,'seed'),
  (NEWID(),'Standard Chartered Bank Kenya','SCB','trade@sc.com','+254703093000',1,'seed'),
  (NEWID(),'DTB Bank Kenya','DTB','trade@dtb.co.ke','+254730150000',1,'seed');
GO

PRINT 'Onwards Swift — AddMissingTables.sql completed successfully.';
GO
