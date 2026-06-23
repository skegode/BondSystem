-- Create CommissionPayments table for tracking monthly commission payments
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CommissionPayments')
BEGIN
    CREATE TABLE CommissionPayments (
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        UserId UNIQUEIDENTIFIER NOT NULL,
        PaymentMonth INT NOT NULL,  -- Month (1-12)
        PaymentYear INT NOT NULL,   -- Year (e.g., 2026)
        CommissionBase DECIMAL(18, 2) DEFAULT 0,
        CommissionPercent DECIMAL(5, 2) DEFAULT 0,
        CommissionAmount DECIMAL(18, 2) DEFAULT 0,
        PaymentStatus NVARCHAR(50) DEFAULT 'Pending',  -- Pending, Partial, Settled
        PaidAmount DECIMAL(18, 2) DEFAULT 0,
        PaymentDate DATETIME NULL,
        PaymentReference NVARCHAR(100) NULL,  -- Bank transfer reference or check number
        Notes NVARCHAR(MAX) NULL,
        CreatedAt DATETIME DEFAULT GETDATE(),
        CreatedBy BIGINT NULL,
        ModifiedAt DATETIME NULL,
        ModifiedBy BIGINT NULL,
        IsDeleted BIT DEFAULT 0
    );

    -- Add index for faster lookups
    CREATE INDEX IX_CommissionPayments_UserId ON CommissionPayments(UserId);
    CREATE INDEX IX_CommissionPayments_PaymentPeriod ON CommissionPayments(PaymentYear, PaymentMonth);
    CREATE INDEX IX_CommissionPayments_Status ON CommissionPayments(PaymentStatus);
END
GO

-- If the table already exists, add missing columns
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'CommissionPayments')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'UserId')
        ALTER TABLE CommissionPayments ADD UserId UNIQUEIDENTIFIER;
    ELSE IF EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'CommissionPayments'
          AND COLUMN_NAME = 'UserId'
          AND DATA_TYPE <> 'uniqueidentifier'
    )
        ALTER TABLE CommissionPayments ALTER COLUMN UserId UNIQUEIDENTIFIER NOT NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaymentMonth')
        ALTER TABLE CommissionPayments ADD PaymentMonth INT;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaymentYear')
        ALTER TABLE CommissionPayments ADD PaymentYear INT;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'CommissionBase')
        ALTER TABLE CommissionPayments ADD CommissionBase DECIMAL(18, 2) DEFAULT 0;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'CommissionPercent')
        ALTER TABLE CommissionPayments ADD CommissionPercent DECIMAL(5, 2) DEFAULT 0;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'CommissionAmount')
        ALTER TABLE CommissionPayments ADD CommissionAmount DECIMAL(18, 2) DEFAULT 0;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaymentStatus')
        ALTER TABLE CommissionPayments ADD PaymentStatus NVARCHAR(50) DEFAULT 'Pending';
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaidAmount')
        ALTER TABLE CommissionPayments ADD PaidAmount DECIMAL(18, 2) DEFAULT 0;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaymentDate')
        ALTER TABLE CommissionPayments ADD PaymentDate DATETIME NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'PaymentReference')
        ALTER TABLE CommissionPayments ADD PaymentReference NVARCHAR(100) NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'Notes')
        ALTER TABLE CommissionPayments ADD Notes NVARCHAR(MAX) NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'CreatedAt')
        ALTER TABLE CommissionPayments ADD CreatedAt DATETIME DEFAULT GETDATE();
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'CreatedBy')
        ALTER TABLE CommissionPayments ADD CreatedBy BIGINT NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'ModifiedAt')
        ALTER TABLE CommissionPayments ADD ModifiedAt DATETIME NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'ModifiedBy')
        ALTER TABLE CommissionPayments ADD ModifiedBy BIGINT NULL;
    
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'CommissionPayments' AND COLUMN_NAME = 'IsDeleted')
        ALTER TABLE CommissionPayments ADD IsDeleted BIT DEFAULT 0;
END
GO
