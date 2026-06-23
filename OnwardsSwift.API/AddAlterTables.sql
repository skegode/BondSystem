-- ══════════════════════════════════════════════════════════════
-- AddAlterTables.sql
-- Run this ONCE in SSMS against OnwardsSwiftDB
-- Safe to re-run — all wrapped in IF NOT EXISTS checks
-- ══════════════════════════════════════════════════════════════

-- ── Banks: add InstitutionType and LicenceNumber columns ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Banks' AND COLUMN_NAME='InstitutionType')
BEGIN
    ALTER TABLE Banks ADD InstitutionType INT NOT NULL DEFAULT 1;
    PRINT 'Added Banks.InstitutionType';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Banks' AND COLUMN_NAME='LicenceNumber')
BEGIN
    ALTER TABLE Banks ADD LicenceNumber NVARCHAR(100) NULL;
    PRINT 'Added Banks.LicenceNumber';
END
GO

-- ── Clients: add UtilisedLimit if missing ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Clients' AND COLUMN_NAME='UtilisedLimit')
BEGIN
    ALTER TABLE Clients ADD UtilisedLimit DECIMAL(18,2) NOT NULL DEFAULT 0;
    PRINT 'Added Clients.UtilisedLimit';
END
GO

-- ── Clients: add CrbCheckedAt if missing ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Clients' AND COLUMN_NAME='CrbCheckedAt')
BEGIN
    ALTER TABLE Clients ADD CrbCheckedAt DATETIME2 NULL;
    PRINT 'Added Clients.CrbCheckedAt';
END
GO

-- ── SystemUsers: add CommissionPercent if missing ──
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='SystemUsers')
AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='SystemUsers' AND COLUMN_NAME='CommissionPercent')
BEGIN
    ALTER TABLE SystemUsers ADD CommissionPercent DECIMAL(5,2) NOT NULL CONSTRAINT DF_SystemUsers_CommissionPercent DEFAULT 0;
    PRINT 'Added SystemUsers.CommissionPercent';
END
GO

-- ── Facilities: add ApprovedBy/ApprovedAt if missing ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='ApprovedBy')
BEGIN
    ALTER TABLE Facilities ADD ApprovedBy NVARCHAR(200) NULL;
    PRINT 'Added Facilities.ApprovedBy';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='ApprovedAt')
BEGIN
    ALTER TABLE Facilities ADD ApprovedAt DATETIME2 NULL;
    PRINT 'Added Facilities.ApprovedAt';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='DisbursedAt')
BEGIN
    ALTER TABLE Facilities ADD DisbursedAt DATETIME2 NULL;
    PRINT 'Added Facilities.DisbursedAt';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='SettledAt')
BEGIN
    ALTER TABLE Facilities ADD SettledAt DATETIME2 NULL;
    PRINT 'Added Facilities.SettledAt';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='RejectionReason')
BEGIN
    ALTER TABLE Facilities ADD RejectionReason NVARCHAR(500) NULL;
    PRINT 'Added Facilities.RejectionReason';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='DisbursementAccount')
BEGIN
    ALTER TABLE Facilities ADD DisbursementAccount NVARCHAR(200) NULL;
    PRINT 'Added Facilities.DisbursementAccount';
END
GO

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Facilities' AND COLUMN_NAME='DisbursementBank')
BEGIN
    ALTER TABLE Facilities ADD DisbursementBank NVARCHAR(200) NULL;
    PRINT 'Added Facilities.DisbursementBank';
END
GO

-- ── InvoiceDiscounts: add DebtorPaymentAmount if missing ──
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='InvoiceDiscounts')
AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='InvoiceDiscounts' AND COLUMN_NAME='DebtorPaymentAmount')
BEGIN
    ALTER TABLE InvoiceDiscounts ADD DebtorPaymentAmount DECIMAL(18,2) NULL;
    PRINT 'Added InvoiceDiscounts.DebtorPaymentAmount';
END
GO

-- ── ChequeDiscounts: add HonouredAt and PresentedAt if missing ──
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='ChequeDiscounts')
AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ChequeDiscounts' AND COLUMN_NAME='HonouredAt')
BEGIN
    ALTER TABLE ChequeDiscounts ADD HonouredAt  DATETIME2 NULL;
    ALTER TABLE ChequeDiscounts ADD PresentedAt DATETIME2 NULL;
    PRINT 'Added ChequeDiscounts.HonouredAt/PresentedAt';
END
GO

-- ── BidBonds: add ResaleAmount/ResaleDate if missing ──
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='BidBonds')
AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='BidBonds' AND COLUMN_NAME='ResaleAmount')
BEGIN
    ALTER TABLE BidBonds ADD ResaleAmount DECIMAL(18,2) NULL;
    ALTER TABLE BidBonds ADD ResaleDate   DATETIME2     NULL;
    PRINT 'Added BidBonds.ResaleAmount/ResaleDate';
END
GO

-- ── LedgerEntries: add CommissionStatus columns if missing ──
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='LedgerEntries')
AND NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='LedgerEntries' AND COLUMN_NAME='CommissionStatus')
BEGIN
    ALTER TABLE LedgerEntries ADD CommissionStatus          NVARCHAR(50)  NULL;
    ALTER TABLE LedgerEntries ADD CommissionReceivedAt      DATETIME2     NULL;
    ALTER TABLE LedgerEntries ADD CommissionPaymentMethod   NVARCHAR(100) NULL;
    PRINT 'Added LedgerEntries.CommissionStatus columns';
END
GO

-- ── FacilityStatusHistory: create if missing ──
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='FacilityStatusHistory')
BEGIN
    CREATE TABLE FacilityStatusHistory (
        Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        FacilityId  UNIQUEIDENTIFIER NOT NULL,
        FromStatus  INT NULL,
        ToStatus    INT NOT NULL,
        ChangedBy   NVARCHAR(200) NULL,
        Comment     NVARCHAR(500) NULL,
        CreatedAt   DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
        IsDeleted   BIT           NOT NULL DEFAULT 0
    );
    PRINT 'Created FacilityStatusHistory';
END
GO

PRINT '=== AddAlterTables.sql complete ===';
GO
