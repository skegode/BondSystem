-- AddBondApplicationsTable.sql
-- Creates/aligns schema used by the Bond Request Wizard.

SET NOCOUNT ON;

PRINT 'Running AddBondApplicationsTable.sql on database: ' + DB_NAME();

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.BondApplications', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.BondApplications (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            ApplicantName NVARCHAR(300) NULL,
            ApplicantAddress NVARCHAR(500) NULL,
            ApplicantCode NVARCHAR(100) NULL,
            ApplicantTown NVARCHAR(200) NULL,
            Procuring NVARCHAR(300) NULL,
            ProcAddress NVARCHAR(500) NULL,
            ProcCode NVARCHAR(100) NULL,
            ProcTown NVARCHAR(200) NULL,
            GuaranteeFigures NVARCHAR(200) NULL,
            GuaranteeWords NVARCHAR(500) NULL,
            BondTypes NVARCHAR(200) NULL,
            TypeOther NVARCHAR(200) NULL,
            TenderRef NVARCHAR(200) NULL,
            GuaranteeFrom DATE NULL,
            GuaranteeTo DATE NULL,
            SigName1 NVARCHAR(200) NULL,
            SigSignature1 NVARCHAR(200) NULL,
            SigName2 NVARCHAR(200) NULL,
            SigSignature2 NVARCHAR(200) NULL,
            IndemnityDateDay NVARCHAR(20) NULL,
            IndemnityDateMonth NVARCHAR(50) NULL,
            IndemnityDateYear NVARCHAR(20) NULL,
            IndemnityName1 NVARCHAR(200) NULL,
            IndemnitySignature1 NVARCHAR(200) NULL,
            IndemnityName2 NVARCHAR(200) NULL,
            IndemnitySignature2 NVARCHAR(200) NULL,
            CompanySealStamp NVARCHAR(200) NULL,
            AttachmentSummary NVARCHAR(MAX) NULL,
            CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_BondApplications_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy NVARCHAR(200) NULL
        );

        PRINT 'Created dbo.BondApplications';
    END
    ELSE
    BEGIN
        PRINT 'dbo.BondApplications already exists - checking missing columns';
    END;

    -- Ensure existing table includes all wizard-captured fields.
    IF COL_LENGTH('dbo.BondApplications', 'ApplicantName') IS NULL ALTER TABLE dbo.BondApplications ADD ApplicantName NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ApplicantAddress') IS NULL ALTER TABLE dbo.BondApplications ADD ApplicantAddress NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ApplicantCode') IS NULL ALTER TABLE dbo.BondApplications ADD ApplicantCode NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ApplicantTown') IS NULL ALTER TABLE dbo.BondApplications ADD ApplicantTown NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'Procuring') IS NULL ALTER TABLE dbo.BondApplications ADD Procuring NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ProcAddress') IS NULL ALTER TABLE dbo.BondApplications ADD ProcAddress NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ProcCode') IS NULL ALTER TABLE dbo.BondApplications ADD ProcCode NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'ProcTown') IS NULL ALTER TABLE dbo.BondApplications ADD ProcTown NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'GuaranteeFigures') IS NULL ALTER TABLE dbo.BondApplications ADD GuaranteeFigures NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'GuaranteeWords') IS NULL ALTER TABLE dbo.BondApplications ADD GuaranteeWords NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'BondTypes') IS NULL ALTER TABLE dbo.BondApplications ADD BondTypes NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'TypeOther') IS NULL ALTER TABLE dbo.BondApplications ADD TypeOther NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'TenderRef') IS NULL ALTER TABLE dbo.BondApplications ADD TenderRef NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'GuaranteeFrom') IS NULL ALTER TABLE dbo.BondApplications ADD GuaranteeFrom DATE NULL;
    IF COL_LENGTH('dbo.BondApplications', 'GuaranteeTo') IS NULL ALTER TABLE dbo.BondApplications ADD GuaranteeTo DATE NULL;
    IF COL_LENGTH('dbo.BondApplications', 'SigName1') IS NULL ALTER TABLE dbo.BondApplications ADD SigName1 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'SigSignature1') IS NULL ALTER TABLE dbo.BondApplications ADD SigSignature1 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'SigName2') IS NULL ALTER TABLE dbo.BondApplications ADD SigName2 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'SigSignature2') IS NULL ALTER TABLE dbo.BondApplications ADD SigSignature2 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnityDateDay') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnityDateDay NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnityDateMonth') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnityDateMonth NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnityDateYear') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnityDateYear NVARCHAR(20) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnityName1') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnityName1 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnitySignature1') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnitySignature1 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnityName2') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnityName2 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'IndemnitySignature2') IS NULL ALTER TABLE dbo.BondApplications ADD IndemnitySignature2 NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'CompanySealStamp') IS NULL ALTER TABLE dbo.BondApplications ADD CompanySealStamp NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'AttachmentSummary') IS NULL ALTER TABLE dbo.BondApplications ADD AttachmentSummary NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.BondApplications', 'CreatedBy') IS NULL ALTER TABLE dbo.BondApplications ADD CreatedBy NVARCHAR(200) NULL;

    IF COL_LENGTH('dbo.BondApplications', 'CreatedAt') IS NULL
        ALTER TABLE dbo.BondApplications ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_BondApplications_CreatedAt_2 DEFAULT SYSUTCDATETIME();

    COMMIT TRAN;
    PRINT 'BondApplications schema aligned successfully';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddBondApplicationsTable.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
