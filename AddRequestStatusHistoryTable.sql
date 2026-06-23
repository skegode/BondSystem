-- Centralized status workflow + history for mobile-originated cheque/bond requests.
-- Previously there was no way for staff to advance a request's status at all (Edit actions
-- only touched request data), and the only "status" the mobile app ever saw was a local
-- SQLite log written once at submission time on the API server, never updated afterward.
-- Status values match OnwardsSwift.Core.Enums.FacilityStatus (Draft=0, Pending=1,
-- UnderReview=2, Approved=3, Disbursed=4, Rejected=5, Expired=6, Settled=7).

IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'Status') IS NULL
    ALTER TABLE dbo.ChequeEncashmentRequests ADD Status INT NULL;

IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'StatusNote') IS NULL
    ALTER TABLE dbo.ChequeEncashmentRequests ADD StatusNote NVARCHAR(500) NULL;

IF OBJECT_ID(N'dbo.RequestStatusHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RequestStatusHistory (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        SourceType    NVARCHAR(20)  NOT NULL,
        SourceId      INT           NOT NULL,
        Status        INT           NOT NULL,
        StatusNote    NVARCHAR(500) NULL,
        ChangedBy     NVARCHAR(200) NULL,
        ChangedAtUtc  DATETIME2     NOT NULL CONSTRAINT DF_RequestStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_RequestStatusHistory_Source ON dbo.RequestStatusHistory(SourceType, SourceId, ChangedAtUtc);
END;