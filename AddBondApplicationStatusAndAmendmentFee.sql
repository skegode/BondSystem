-- Adds application status and amendment fee fields to Bonds for bid-bond workflow updates.

IF COL_LENGTH('dbo.Bonds', 'ApplicationStatus') IS NULL
BEGIN
    ALTER TABLE dbo.Bonds
    ADD ApplicationStatus NVARCHAR(50) NOT NULL
        CONSTRAINT DF_Bonds_ApplicationStatus DEFAULT ('New Application');
END
GO

IF COL_LENGTH('dbo.Bonds', 'AmendmentFee') IS NULL
BEGIN
    ALTER TABLE dbo.Bonds
    ADD AmendmentFee DECIMAL(18,2) NOT NULL
        CONSTRAINT DF_Bonds_AmendmentFee DEFAULT (0);
END
GO
