-- ============================================================
--  Onwards Swift — AddFeatureTables.sql
--  Run AFTER InitialMigration.sql AND AddMissingTables.sql
--  Safe to re-run — all statements use IF NOT EXISTS
-- ============================================================
USE OnwardsSwiftDB;


-- ── ADD InstitutionType and LicenceNumber to BankPartners ─────
IF EXISTS (SELECT * FROM sysobjects WHERE name='BankPartners' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BankPartners') AND name='InstitutionType')
        ALTER TABLE BankPartners ADD InstitutionType NVARCHAR(30) NOT NULL DEFAULT 'Bank';
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('BankPartners') AND name='LicenceNumber')
        ALTER TABLE BankPartners ADD LicenceNumber NVARCHAR(100) NULL;
    PRINT 'BankPartners updated with InstitutionType and LicenceNumber';
END
GO

-- ── ADD CommissionRate and CommissionType to ProductRates ─────
IF EXISTS (SELECT * FROM sysobjects WHERE name='ProductRates' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('ProductRates') AND name='CommissionRate')
        ALTER TABLE ProductRates ADD CommissionRate DECIMAL(18,4) NOT NULL DEFAULT 0;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('ProductRates') AND name='CommissionType')
        ALTER TABLE ProductRates ADD CommissionType NVARCHAR(30) NOT NULL DEFAULT 'PercentOfFee';
    PRINT 'ProductRates updated with CommissionRate and CommissionType';
END
GO

-- ── ADD CommissionStatus columns to LedgerEntries ─────────────
IF EXISTS (SELECT * FROM sysobjects WHERE name='LedgerEntries' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('LedgerEntries') AND name='CommissionStatus')
        ALTER TABLE LedgerEntries ADD CommissionStatus NVARCHAR(20) NOT NULL DEFAULT 'Pending';
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('LedgerEntries') AND name='CommissionReceivedAt')
        ALTER TABLE LedgerEntries ADD CommissionReceivedAt DATETIME2 NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('LedgerEntries') AND name='CommissionPaymentMethod')
        ALTER TABLE LedgerEntries ADD CommissionPaymentMethod NVARCHAR(50) NULL;
    PRINT 'LedgerEntries updated with commission tracking columns';
END
GO

-- ── ADD RejectionReason and Notes to Clients ──────────────────
IF EXISTS (SELECT * FROM sysobjects WHERE name='Clients' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='RejectionReason')
        ALTER TABLE Clients ADD RejectionReason NVARCHAR(500) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Clients') AND name='Notes')
        ALTER TABLE Clients ADD Notes NVARCHAR(1000) NULL;
    PRINT 'Clients table updated with RejectionReason and Notes';
END
GO

GO


-- ── OBLIGEES (Project Owners / Procuring Entities) ───────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Obligees' AND xtype='U')
BEGIN
CREATE TABLE Obligees (
    Id            UNIQUEIDENTIFIER  NOT NULL DEFAULT NEWID() PRIMARY KEY,
    Name          NVARCHAR(300)     NOT NULL,
    ShortName     NVARCHAR(50)      NULL,
    Category      NVARCHAR(50)      NOT NULL DEFAULT 'Government',
    ContactPerson NVARCHAR(200)     NULL,
    Email         NVARCHAR(200)     NULL,
    Phone         NVARCHAR(50)      NULL,
    Address       NVARCHAR(500)     NULL,
    IsActive      BIT               NOT NULL DEFAULT 1,
    CreatedAt     DATETIME2         NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt     DATETIME2         NULL,
    CreatedBy     NVARCHAR(200)     NULL,
    UpdatedBy     NVARCHAR(200)     NULL,
    IsDeleted     BIT               NOT NULL DEFAULT 0
);
CREATE INDEX IX_Obligees_Name     ON Obligees(Name);
CREATE INDEX IX_Obligees_Category ON Obligees(Category);

-- Seed common Kenyan government obligees
INSERT INTO Obligees (Name, ShortName, Category) VALUES
  ('Ministry of Health','MOH','Government'),
  ('Ministry of Roads & Transport','MoRT','Government'),
  ('Ministry of Education','MOE','Government'),
  ('Ministry of Water & Irrigation','MoW','Government'),
  ('Ministry of Energy & Petroleum','MEP','Government'),
  ('Kenya National Highways Authority','KeNHA','Government'),
  ('Kenya Rural Roads Authority','KeRRA','Government'),
  ('Kenya Urban Roads Authority','KURA','Government'),
  ('Kenya Power & Lighting Company','KPLC','Government'),
  ('Kenya Airports Authority','KAA','Government'),
  ('Kenya Ports Authority','KPA','Government'),
  ('Kenya Railways Corporation','KRC','Government'),
  ('National Hospital Insurance Fund','NHIF','Government'),
  ('National Social Security Fund','NSSF','Government'),
  ('Kenya Revenue Authority','KRA','Government'),
  ('Kenya Pipeline Company','KPC','Government'),
  ('Kenya National Water Storage Authority','KNWSA','Government'),
  ('Nairobi City County','NCC','County'),
  ('Mombasa County Government','MCG','County'),
  ('Kisumu County Government','KCG','County');
PRINT 'Created Obligees table with seed data';
END
GO

-- ── CLIENT DOCUMENTS ─────────────────────────────────────────
-- Stores KYC/onboarding documents uploaded per client
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ClientDocuments' AND xtype='U')
BEGIN
CREATE TABLE ClientDocuments (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    ClientId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Clients(Id),
    DocumentType    NVARCHAR(100)       NOT NULL,  -- 'KRA PIN','CoI','CR12','Director ID',etc.
    FileName        NVARCHAR(300)       NOT NULL,
    FilePath        NVARCHAR(1000)      NOT NULL,  -- server-relative path e.g. /uploads/clients/{id}/file.pdf
    ContentType     NVARCHAR(100)       NOT NULL,
    FileSizeBytes   BIGINT              NOT NULL DEFAULT 0,
    IsVerified      BIT                 NOT NULL DEFAULT 0,
    VerifiedBy      NVARCHAR(200)       NULL,
    VerifiedAt      DATETIME2           NULL,
    Notes           NVARCHAR(500)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_ClientDocuments_ClientId ON ClientDocuments(ClientId);
PRINT 'Created ClientDocuments table';
END
GO

-- ── FACILITY DOCUMENTS (file path version) ────────────────────
-- Add FilePath column to FacilityDocuments if not present (BlobPath was for Azure)
IF EXISTS (SELECT * FROM sysobjects WHERE name='FacilityDocuments' AND xtype='U')
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('FacilityDocuments') AND name='FilePath')
        ALTER TABLE FacilityDocuments ADD FilePath NVARCHAR(1000) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('FacilityDocuments') AND name='DocumentType')
        ALTER TABLE FacilityDocuments ADD DocumentType NVARCHAR(100) NULL;
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('FacilityDocuments') AND name='Notes')
        ALTER TABLE FacilityDocuments ADD Notes NVARCHAR(500) NULL;
    PRINT 'FacilityDocuments updated';
END
GO

-- ── CASH COVER ────────────────────────────────────────────────
-- Performance / Customs bonds that have a security deposit held
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CashCovers' AND xtype='U')
BEGIN
CREATE TABLE CashCovers (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId      UNIQUEIDENTIFIER    NOT NULL REFERENCES Facilities(Id),
    BondRef         NVARCHAR(30)        NOT NULL,  -- denormalised for quick display
    ClientId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Clients(Id),
    ClientName      NVARCHAR(200)       NOT NULL,
    BondAmount      DECIMAL(18,2)       NOT NULL,
    CashCoverAmount DECIMAL(18,2)       NOT NULL,
    CashCoverPct    DECIMAL(5,2)        NULL,       -- % of bond amount
    MaturityDate    DATE                NOT NULL,
    HoldingAccount  NVARCHAR(200)       NULL,       -- bank account holding the deposit
    HoldingBank     NVARCHAR(200)       NULL,
    Status          NVARCHAR(20)        NOT NULL DEFAULT 'Active',
    -- Active | Released | Forfeited
    ReleasedDate    DATE                NULL,
    ReleasedBy      NVARCHAR(200)       NULL,
    ReleaseNotes    NVARCHAR(500)       NULL,
    Notes           NVARCHAR(500)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_CashCovers_FacilityId  ON CashCovers(FacilityId);
CREATE INDEX IX_CashCovers_ClientId    ON CashCovers(ClientId);
CREATE INDEX IX_CashCovers_MaturityDate ON CashCovers(MaturityDate);
PRINT 'Created CashCovers table';
END
GO

-- ── BOND CLAIMS ───────────────────────────────────────────────
-- Records when a bond is called (bidder backs out)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BondClaims' AND xtype='U')
BEGIN
CREATE TABLE BondClaims (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId      UNIQUEIDENTIFIER    NOT NULL REFERENCES Facilities(Id),
    BondRef         NVARCHAR(30)        NOT NULL,
    ClientId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Clients(Id),
    Obligee         NVARCHAR(300)       NOT NULL,   -- who is claiming
    PenalSum        DECIMAL(18,2)       NOT NULL,   -- bond amount (max payable)
    DefaultingBid   DECIMAL(18,2)       NULL,       -- winning bid that was rejected
    NextLowestBid   DECIMAL(18,2)       NULL,       -- next lowest bid (re-tender cost)
    ClaimAmount     DECIMAL(18,2)       NOT NULL,   -- min(nextBid-defaultBid, penalSum)
    ClaimDate       DATE                NOT NULL,
    ClaimReference  NVARCHAR(100)       NULL,       -- obligee claim ref number
    ClaimStatus     NVARCHAR(30)        NOT NULL DEFAULT 'Recorded',
    -- Recorded | PaidToObligee | Recovered | WrittenOff
    PaidDate        DATE                NULL,
    RecoveredFrom   NVARCHAR(200)       NULL,
    RecoveredAmount DECIMAL(18,2)       NULL,
    Notes           NVARCHAR(1000)      NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_BondClaims_FacilityId ON BondClaims(FacilityId);
PRINT 'Created BondClaims table';
END
GO

-- ── ADVANCE PAYMENTS ─────────────────────────────────────────
-- Separate detail table for Advance Payment Guarantees
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AdvancePayments' AND xtype='U')
BEGIN
CREATE TABLE AdvancePayments (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId      UNIQUEIDENTIFIER    NOT NULL REFERENCES Facilities(Id),
    ClientId        UNIQUEIDENTIFIER    NOT NULL REFERENCES Clients(Id),
    ContractRef     NVARCHAR(100)       NOT NULL,   -- contract/reference number
    Beneficiary     NVARCHAR(300)       NOT NULL,   -- obligee / project owner
    BondAmount      DECIMAL(18,2)       NOT NULL,
    BankRate        DECIMAL(5,4)        NOT NULL,   -- rate per annum (e.g. 0.025 = 2.5%)
    TenorDays       INT                 NOT NULL,
    BankCharges     DECIMAL(18,2)       NOT NULL,
    CommissionAmount DECIMAL(18,2)      NOT NULL,
    VatAmount       DECIMAL(18,2)       NOT NULL DEFAULT 0,
    TotalFee        DECIMAL(18,2)       NOT NULL,
    IssuingBank     NVARCHAR(200)       NULL,
    ContractStartDate DATE              NULL,
    ContractEndDate   DATE              NULL,
    PaymentMode     NVARCHAR(50)        NULL,
    RelationshipOfficerId NVARCHAR(100) NULL,   -- SystemUsers.Id
    RelationshipOfficer   NVARCHAR(200) NULL,   -- name
    Status          NVARCHAR(30)        NOT NULL DEFAULT 'Pending',
    Notes           NVARCHAR(1000)      NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_AdvancePayments_FacilityId ON AdvancePayments(FacilityId);
CREATE INDEX IX_AdvancePayments_ClientId   ON AdvancePayments(ClientId);
PRINT 'Created AdvancePayments table';
END
GO

-- ── DAILY LEDGER ─────────────────────────────────────────────
-- One row per day, stores opening/closing balances and is auto-populated
-- by the system when payments are recorded
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DailyLedger' AND xtype='U')
BEGIN
CREATE TABLE DailyLedger (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LedgerDate      DATE                NOT NULL UNIQUE,
    OpeningBalance  DECIMAL(18,2)       NOT NULL DEFAULT 0,
    ClosingBalance  DECIMAL(18,2)       NOT NULL DEFAULT 0,
    TotalReceipts   DECIMAL(18,2)       NOT NULL DEFAULT 0,
    TotalPayments   DECIMAL(18,2)       NOT NULL DEFAULT 0,
    TotalProfit     DECIMAL(18,2)       NOT NULL DEFAULT 0,
    IsReconciled    BIT                 NOT NULL DEFAULT 0,
    ReconciledBy    NVARCHAR(200)       NULL,
    ReconciledAt    DATETIME2           NULL,
    Notes           NVARCHAR(500)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
PRINT 'Created DailyLedger table';
END
GO

-- ── LEDGER ENTRIES ─────────────────────────────────────────────
-- Individual debit/credit lines linked to a ledger day
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LedgerEntries' AND xtype='U')
BEGIN
CREATE TABLE LedgerEntries (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    LedgerDate      DATE                NOT NULL,
    EntryType       NVARCHAR(20)        NOT NULL,  -- Receipt | Payment | Profit | Adjustment
    Reference       NVARCHAR(100)       NOT NULL,  -- FacilityRef or manual ref
    FacilityId      UNIQUEIDENTIFIER    NULL REFERENCES Facilities(Id),
    ClientId        UNIQUEIDENTIFIER    NULL REFERENCES Clients(Id),
    ClientName      NVARCHAR(200)       NULL,
    Description     NVARCHAR(500)       NOT NULL,
    Amount          DECIMAL(18,2)       NOT NULL,
    BankCharges     DECIMAL(18,2)       NOT NULL DEFAULT 0,
    CommissionAmount DECIMAL(18,2)      NOT NULL DEFAULT 0,
    VatAmount       DECIMAL(18,2)       NOT NULL DEFAULT 0,
    IsPaid          BIT                 NOT NULL DEFAULT 0,
    PaidAt          DATETIME2           NULL,
    PaymentMethod   NVARCHAR(50)        NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_LedgerEntries_LedgerDate  ON LedgerEntries(LedgerDate);
CREATE INDEX IX_LedgerEntries_FacilityId  ON LedgerEntries(FacilityId);
CREATE INDEX IX_LedgerEntries_ClientId    ON LedgerEntries(ClientId);
PRINT 'Created LedgerEntries table';
END
GO

-- ── SYSTEM SETTINGS ────────────────────────────────────────────
-- Key-value store for system-wide settings (commission rate, VAT, company info)
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemSettings' AND xtype='U')
BEGIN
CREATE TABLE SystemSettings (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    SettingKey      NVARCHAR(100)       NOT NULL UNIQUE,
    SettingValue    NVARCHAR(2000)      NOT NULL,
    Category        NVARCHAR(50)        NOT NULL DEFAULT 'General',
    Description     NVARCHAR(300)       NULL,
    UpdatedAt       DATETIME2           NULL,
    UpdatedBy       NVARCHAR(200)       NULL
);
-- Seed default settings
INSERT INTO SystemSettings (SettingKey, SettingValue, Category, Description)
VALUES
  ('commissionPct',     '15',                         'Charges',  'Service commission % on top of bank charges'),
  ('processingFeeFlat', '0',                          'Charges',  'Flat KSh fee per application'),
  ('vatPct',            '16',                         'Charges',  'VAT % applied on commission'),
  ('applyVatOnComm',    'true',                       'Charges',  'Whether to apply VAT on commission'),
  ('companyName',       'Onwards Swift Co. Ltd',      'Company',  'Company name shown on quotes'),
  ('companyAddress',    'Nairobi, Kenya',             'Company',  'Physical address'),
  ('companyEmail',      'info@onwardsswift.com',      'Company',  'Contact email'),
  ('companyPhone',      '+254 700 000 000',           'Company',  'Contact phone'),
  ('companyWebsite',    'www.onwardsswift.com',       'Company',  'Website'),
  ('companyLicence',    'CBK Licence No. TF/2026/001','Company',  'Regulatory licence shown on quotes'),
  ('quoteValidDays',    '7',                          'Company',  'Days a client quote is valid');
PRINT 'Created SystemSettings table with defaults';
END
GO

PRINT '====================================================';
PRINT 'Onwards Swift — AddFeatureTables.sql completed OK';
PRINT '====================================================';
GO
