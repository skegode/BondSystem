-- Add missing columns to Clients table for Individual-specific fields and KYC document paths
-- This allows proper support for both Individual and Corporate client types

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'Category')
BEGIN
    ALTER TABLE Clients ADD Category INT NULL;  -- 1=Individual, 2=Corporate
    PRINT 'Added Category column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'IdNumber')
BEGIN
    ALTER TABLE Clients ADD IdNumber NVARCHAR(50) NULL;
    PRINT 'Added IdNumber column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'Gender')
BEGIN
    ALTER TABLE Clients ADD Gender INT NULL;  -- 1=Male, 2=Female, 3=Other
    PRINT 'Added Gender column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'KycIdFrontPath')
BEGIN
    ALTER TABLE Clients ADD KycIdFrontPath NVARCHAR(500) NULL;
    PRINT 'Added KycIdFrontPath column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'KycIdBackPath')
BEGIN
    ALTER TABLE Clients ADD KycIdBackPath NVARCHAR(500) NULL;
    PRINT 'Added KycIdBackPath column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'KycPassportPhotoPath')
BEGIN
    ALTER TABLE Clients ADD KycPassportPhotoPath NVARCHAR(500) NULL;
    PRINT 'Added KycPassportPhotoPath column to Clients';
END

IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Clients' AND COLUMN_NAME = 'KycRegCertPath')
BEGIN
    ALTER TABLE Clients ADD KycRegCertPath NVARCHAR(500) NULL;
    PRINT 'Added KycRegCertPath column to Clients';
END

PRINT 'All missing columns have been added to Clients table successfully.';
