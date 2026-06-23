-- Add Pricing Summary fields for bond applications
-- Safe to run multiple times.

IF OBJECT_ID('dbo.Bonds', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Bonds' AND COLUMN_NAME = 'TaxPercentage'
    )
    BEGIN
        ALTER TABLE dbo.Bonds ADD TaxPercentage DECIMAL(9,4) NOT NULL CONSTRAINT DF_Bonds_TaxPercentage DEFAULT 0;
        PRINT 'Added Bonds.TaxPercentage';
    END

    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Bonds' AND COLUMN_NAME = 'TaxCalculation'
    )
    BEGIN
        ALTER TABLE dbo.Bonds ADD TaxCalculation DECIMAL(18,2) NOT NULL CONSTRAINT DF_Bonds_TaxCalculation DEFAULT 0;
        PRINT 'Added Bonds.TaxCalculation';
    END

    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = 'Bonds' AND COLUMN_NAME = 'TotalBankCharge'
    )
    BEGIN
        ALTER TABLE dbo.Bonds ADD TotalBankCharge DECIMAL(18,2) NOT NULL CONSTRAINT DF_Bonds_TotalBankCharge DEFAULT 0;
        PRINT 'Added Bonds.TotalBankCharge';
    END

    EXEC sp_executesql N'
        UPDATE dbo.Bonds
        SET TotalBankCharge = ISNULL(BankCharge, 0)
        WHERE ISNULL(TotalBankCharge, 0) = 0;
    ';
END
GO
