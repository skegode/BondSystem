-- ============================================================
--  Onwards Swift Trade Finance System
--  SQL Server Migration Script
--  Run this after: dotnet ef migrations add InitialCreate
--                  dotnet ef database update
-- ============================================================

-- Or run this script manually against SQL Server

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'OnwardsSwiftDB')
BEGIN
    CREATE DATABASE OnwardsSwiftDB;
END
GO

USE OnwardsSwiftDB;
GO

-- ── CLIENTS ──────────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Clients' AND xtype='U')
BEGIN
CREATE TABLE Clients (
    Id              UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    CompanyName     NVARCHAR(200)       NOT NULL,
    KraPin          NVARCHAR(15)        NOT NULL UNIQUE,
    ContactPerson   NVARCHAR(150)       NOT NULL,
    Email           NVARCHAR(200)       NOT NULL UNIQUE,
    Phone           NVARCHAR(20)        NOT NULL,
    PhoneAlt        NVARCHAR(20)        NULL,
    ClientType      INT                 NOT NULL DEFAULT 1,  -- 1=Individual,2=SME,3=Corporate,4=Govt
    PhysicalAddress NVARCHAR(500)       NOT NULL,
    PostalAddress   NVARCHAR(200)       NULL,
    BusinessRegNumber NVARCHAR(100)     NULL,
    CreditLimit     DECIMAL(18,2)       NOT NULL DEFAULT 0,
    UtilisedLimit   DECIMAL(18,2)       NOT NULL DEFAULT 0,
    Status          INT                 NOT NULL DEFAULT 1,  -- 1=Pending,3=Approved,5=Rejected
    IsCrbCleared    BIT                 NOT NULL DEFAULT 0,
    CrbCheckedAt    DATETIME2           NULL,
    UserId          NVARCHAR(100)       NULL,
    CreatedAt       DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2           NULL,
    CreatedBy       NVARCHAR(200)       NULL,
    UpdatedBy       NVARCHAR(200)       NULL,
    IsDeleted       BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_Clients_KraPin  ON Clients(KraPin);
CREATE INDEX IX_Clients_Email   ON Clients(Email);
END
GO

-- ── FACILITIES ───────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Facilities' AND xtype='U')
BEGIN
CREATE TABLE Facilities (
    Id                  UNIQUEIDENTIFIER    NOT NULL DEFAULT NEWID() PRIMARY KEY,
    ReferenceNo         NVARCHAR(30)        NOT NULL UNIQUE,
    Type                INT                 NOT NULL,  -- 1=BidBond,2=InvoiceDisc,3=ChequeDisc
    ClientId            UNIQUEIDENTIFIER    NOT NULL REFERENCES Clients(Id),
    Amount              DECIMAL(18,2)       NOT NULL,
    Rate                DECIMAL(5,4)        NOT NULL,
    TenorDays           INT                 NOT NULL,
    FinanceFee          DECIMAL(18,2)       NOT NULL DEFAULT 0,
    NetAmount           DECIMAL(18,2)       NOT NULL DEFAULT 0,
    Status              INT                 NOT NULL DEFAULT 1,
    RejectionReason     NVARCHAR(500)       NULL,
    ApprovedBy          NVARCHAR(200)       NULL,
    ApprovedAt          DATETIME2           NULL,
    DisbursedAt         DATETIME2           NULL,
    SettledAt           DATETIME2           NULL,
    DisbursementAccount NVARCHAR(100)       NULL,
    DisbursementBank    NVARCHAR(200)       NULL,
    Notes               NVARCHAR(1000)      NULL,
    InternalNotes       NVARCHAR(1000)      NULL,
    CreatedAt           DATETIME2           NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2           NULL,
    CreatedBy           NVARCHAR(200)       NULL,
    UpdatedBy           NVARCHAR(200)       NULL,
    IsDeleted           BIT                 NOT NULL DEFAULT 0
);
CREATE INDEX IX_Facilities_ClientId    ON Facilities(ClientId);
CREATE INDEX IX_Facilities_Type        ON Facilities(Type);
CREATE INDEX IX_Facilities_Status      ON Facilities(Status);
END
GO

-- ── BID BONDS ────────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='BidBonds' AND xtype='U')
BEGIN
CREATE TABLE BidBonds (
    Id                          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId                  UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES Facilities(Id),
    TenderNumber                NVARCHAR(100)    NOT NULL,
    ProcuringEntity             NVARCHAR(300)    NOT NULL,
    IssuingBank                 INT              NOT NULL,
    TenderClosingDate           DATE             NOT NULL,
    BondNumber                  NVARCHAR(100)    NULL,
    BondCommissionRate          DECIMAL(5,4)     NOT NULL DEFAULT 0.025,
    IsResold                    BIT              NOT NULL DEFAULT 0,
    ResalePartner               NVARCHAR(300)    NULL,
    ResaleDate                  DATETIME2        NULL,
    ResaleAmount                DECIMAL(18,2)    NULL,
    ConvertedToPerformanceBond  BIT              NOT NULL DEFAULT 0,
    PerformanceBondFacilityId   UNIQUEIDENTIFIER NULL,
    CreatedAt                   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt                   DATETIME2        NULL,
    CreatedBy                   NVARCHAR(200)    NULL,
    UpdatedBy                   NVARCHAR(200)    NULL,
    IsDeleted                   BIT              NOT NULL DEFAULT 0
);
END
GO

-- ── INVOICE DISCOUNTS ────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='InvoiceDiscounts' AND xtype='U')
BEGIN
CREATE TABLE InvoiceDiscounts (
    Id                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId          UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES Facilities(Id),
    InvoiceNumber       NVARCHAR(100)    NOT NULL,
    DebtorName          NVARCHAR(300)    NOT NULL,
    DebtorKraPin        NVARCHAR(15)     NULL,
    DebtorContact       NVARCHAR(100)    NULL,
    InvoiceDate         DATE             NOT NULL,
    InvoiceDueDate      DATE             NOT NULL,
    InvoiceFaceValue    DECIMAL(18,2)    NOT NULL,
    AdvancePercentage   DECIMAL(5,2)     NOT NULL,
    AdvanceAmount       DECIMAL(18,2)    NOT NULL,
    DiscountFee         DECIMAL(18,2)    NOT NULL,
    NetAdvance          DECIMAL(18,2)    NOT NULL,
    DebtorPaid          BIT              NOT NULL DEFAULT 0,
    DebtorPaymentDate   DATETIME2        NULL,
    DebtorPaymentAmount DECIMAL(18,2)    NULL,
    RetainedAmount      DECIMAL(18,2)    NULL,
    CreatedAt           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2        NULL,
    CreatedBy           NVARCHAR(200)    NULL,
    UpdatedBy           NVARCHAR(200)    NULL,
    IsDeleted           BIT              NOT NULL DEFAULT 0
);
END
GO

-- ── CHEQUE DISCOUNTS ─────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ChequeDiscounts' AND xtype='U')
BEGIN
CREATE TABLE ChequeDiscounts (
    Id                  UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId          UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES Facilities(Id),
    ChequeNumber        NVARCHAR(50)     NOT NULL,
    DrawerName          NVARCHAR(300)    NOT NULL,
    DrawerKraPin        NVARCHAR(15)     NULL,
    DraweeBank          INT              NOT NULL,
    DraweeBranch        NVARCHAR(200)    NULL,
    ChequeDate          DATE             NOT NULL,
    MaturityDate        DATE             NOT NULL,
    ChequeFaceValue     DECIMAL(18,2)    NOT NULL,
    AdvanceAmount       DECIMAL(18,2)    NOT NULL,
    DiscountFee         DECIMAL(18,2)    NOT NULL,
    NetAdvance          DECIMAL(18,2)    NOT NULL,
    PresentedToBank     BIT              NOT NULL DEFAULT 0,
    PresentedAt         DATETIME2        NULL,
    Honoured            BIT              NOT NULL DEFAULT 0,
    HonouredAt          DATETIME2        NULL,
    Bounced             BIT              NOT NULL DEFAULT 0,
    BounceReason        NVARCHAR(500)    NULL,
    CreatedAt           DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           DATETIME2        NULL,
    CreatedBy           NVARCHAR(200)    NULL,
    UpdatedBy           NVARCHAR(200)    NULL,
    IsDeleted           BIT              NOT NULL DEFAULT 0
);
END
GO

-- ── FACILITY STATUS HISTORY ──────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FacilityStatusHistories' AND xtype='U')
BEGIN
CREATE TABLE FacilityStatusHistories (
    Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId  UNIQUEIDENTIFIER NOT NULL REFERENCES Facilities(Id),
    FromStatus  INT              NOT NULL,
    ToStatus    INT              NOT NULL,
    ChangedBy   NVARCHAR(200)    NOT NULL,
    Comment     NVARCHAR(1000)   NULL,
    IpAddress   NVARCHAR(50)     NULL,
    CreatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2        NULL,
    CreatedBy   NVARCHAR(200)    NULL,
    UpdatedBy   NVARCHAR(200)    NULL,
    IsDeleted   BIT              NOT NULL DEFAULT 0
);
CREATE INDEX IX_StatusHistory_FacilityId ON FacilityStatusHistories(FacilityId);
END
GO

-- ── NOTIFICATIONS ────────────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Notifications' AND xtype='U')
BEGIN
CREATE TABLE Notifications (
    Id          UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId  UNIQUEIDENTIFIER NULL REFERENCES Facilities(Id),
    ClientId    UNIQUEIDENTIFIER NULL,
    Type        INT              NOT NULL,
    Recipient   NVARCHAR(200)    NOT NULL,
    Subject     NVARCHAR(300)    NOT NULL,
    Body        NVARCHAR(MAX)    NOT NULL,
    IsSent      BIT              NOT NULL DEFAULT 0,
    SentAt      DATETIME2        NULL,
    Channel     NVARCHAR(20)     NULL,
    ErrorMessage NVARCHAR(500)   NULL,
    CreatedAt   DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt   DATETIME2        NULL,
    CreatedBy   NVARCHAR(200)    NULL,
    UpdatedBy   NVARCHAR(200)    NULL,
    IsDeleted   BIT              NOT NULL DEFAULT 0
);
END
GO

-- ── FACILITY DOCUMENTS ───────────────────────────────────────
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FacilityDocuments' AND xtype='U')
BEGIN
CREATE TABLE FacilityDocuments (
    Id              UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    FacilityId      UNIQUEIDENTIFIER NOT NULL REFERENCES Facilities(Id),
    DocumentType    INT              NOT NULL,
    FileName        NVARCHAR(300)    NOT NULL,
    BlobPath        NVARCHAR(1000)   NOT NULL,
    ContentType     NVARCHAR(100)    NOT NULL,
    FileSizeBytes   BIGINT           NOT NULL DEFAULT 0,
    IsVerified      BIT              NOT NULL DEFAULT 0,
    VerifiedBy      NVARCHAR(200)    NULL,
    VerifiedAt      DATETIME2        NULL,
    CreatedAt       DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt       DATETIME2        NULL,
    CreatedBy       NVARCHAR(200)    NULL,
    UpdatedBy       NVARCHAR(200)    NULL,
    IsDeleted       BIT              NOT NULL DEFAULT 0
);
END
GO

-- ── SEED DATA ────────────────────────────────────────────────
INSERT INTO Clients (Id, CompanyName, KraPin, ContactPerson, Email, Phone, ClientType, PhysicalAddress, CreditLimit, Status, IsCrbCleared, CreatedBy)
VALUES
    ('A1B2C3D4-0001-0001-0001-000000000001','Kamau Enterprises Ltd','P051200001A','James Kamau','james@kamauent.co.ke','+254712000001',3,'Westlands, Nairobi',10000000,3,1,'seed'),
    ('A1B2C3D4-0001-0001-0001-000000000002','Wanjiku Supplies Co.','P051200002B','Grace Wanjiku','grace@wanjikusupplies.co.ke','+254712000002',2,'Industrial Area, Nairobi',2000000,3,1,'seed'),
    ('A1B2C3D4-0001-0001-0001-000000000003','Mwangi & Sons Ltd','P051200003C','Peter Mwangi','peter@mwangisons.co.ke','+254712000003',2,'Thika Road, Nairobi',1500000,3,1,'seed'),
    ('A1B2C3D4-0001-0001-0001-000000000004','Otieno Contractors','P051200004D','David Otieno','david@otienocontractors.co.ke','+254712000004',3,'Mombasa Road, Nairobi',20000000,2,0,'seed');
GO

PRINT 'Onwards Swift database schema created and seeded successfully.';
GO
