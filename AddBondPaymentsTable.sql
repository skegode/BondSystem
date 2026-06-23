-- Captures installment (partial/full) payments against each bond's client charge.
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_NAME = 'BondPayments'
)
BEGIN
    CREATE TABLE BondPayments
    (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        BondId INT NOT NULL,
        ClientId INT NOT NULL,
        AmountPaid DECIMAL(18,2) NOT NULL,
        PaymentMethod NVARCHAR(20) NULL,
        PaymentReference NVARCHAR(100) NULL,
        Notes NVARCHAR(300) NULL,
        PaymentDate DATETIME NOT NULL CONSTRAINT DF_BondPayments_PaymentDate DEFAULT(GETDATE()),
        CreatedAt DATETIME NOT NULL CONSTRAINT DF_BondPayments_CreatedAt DEFAULT(GETDATE()),
        CreatedBy NVARCHAR(50) NULL
    );

    CREATE INDEX IX_BondPayments_BondId ON BondPayments(BondId);
    CREATE INDEX IX_BondPayments_ClientId_PaymentDate ON BondPayments(ClientId, PaymentDate);
END;
