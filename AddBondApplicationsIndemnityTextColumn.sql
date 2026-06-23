PRINT 'Running AddBondApplicationsIndemnityTextColumn.sql on database: ' + DB_NAME();

IF OBJECT_ID(N'dbo.BondApplications', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.BondApplications', 'ApplicantEmail') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD ApplicantEmail NVARCHAR(200) NULL;
        PRINT 'Added ApplicantEmail column to dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'Column ApplicantEmail already exists on dbo.BondApplications';
    END

    IF COL_LENGTH('dbo.BondApplications', 'ApplicantPhone') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD ApplicantPhone NVARCHAR(100) NULL;
        PRINT 'Added ApplicantPhone column to dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'Column ApplicantPhone already exists on dbo.BondApplications';
    END

    IF COL_LENGTH('dbo.BondApplications', 'Status') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD Status NVARCHAR(100) NULL;
        PRINT 'Added Status column to dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'Column Status already exists on dbo.BondApplications';
    END

    IF COL_LENGTH('dbo.BondApplications', 'StatusNote') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD StatusNote NVARCHAR(MAX) NULL;
        PRINT 'Added StatusNote column to dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'Column StatusNote already exists on dbo.BondApplications';
    END

    IF COL_LENGTH('dbo.BondApplications', 'IndemnityText') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD IndemnityText NVARCHAR(MAX) NULL;
        PRINT 'Added IndemnityText column to dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'Column IndemnityText already exists on dbo.BondApplications';
    END
END
ELSE
BEGIN
    PRINT 'Table dbo.BondApplications does not exist. Run AddBondApplicationsTable.sql first.';
END
