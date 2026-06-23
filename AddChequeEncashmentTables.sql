-- AddChequeEncashmentTables.sql
-- Creates/aligns schema used by the Client Onboarding Wizard Step 2.

SET NOCOUNT ON;

PRINT 'Running AddChequeEncashmentTables.sql on database: ' + DB_NAME();

IF DB_NAME() = N'master'
BEGIN
    RAISERROR('Do not run AddChequeEncashmentTables.sql on master. Switch to the application database (for example: USE OnwardsSwiftDB).', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ChequeEncashmentRequests (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            ClientId INT NULL,
            ApplicantName NVARCHAR(300) NULL,
            IdNumber NVARCHAR(100) NULL,
            PostalAddress NVARCHAR(500) NULL,
            Phone NVARCHAR(100) NULL,
            Purpose NVARCHAR(MAX) NULL,
            TermsAccepted BIT NOT NULL CONSTRAINT DF_CER_TermsAccepted DEFAULT 0,
            CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CER_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy NVARCHAR(200) NULL
        );

        PRINT 'Created dbo.ChequeEncashmentRequests';
    END
    ELSE
    BEGIN
        PRINT 'dbo.ChequeEncashmentRequests already exists - checking missing columns';
    END;

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'ClientId') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD ClientId INT NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'ApplicantName') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD ApplicantName NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'IdNumber') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD IdNumber NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'PostalAddress') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD PostalAddress NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'Phone') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD Phone NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'Purpose') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD Purpose NVARCHAR(MAX) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'TermsAccepted') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD TermsAccepted BIT NOT NULL CONSTRAINT DF_CER_TermsAccepted_2 DEFAULT 0;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'CreatedBy') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD CreatedBy NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'CreatedAt') IS NULL ALTER TABLE dbo.ChequeEncashmentRequests ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CER_CreatedAt_2 DEFAULT SYSUTCDATETIME();

    IF OBJECT_ID(N'dbo.ChequeEncashmentCheques', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ChequeEncashmentCheques (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            RequestId INT NOT NULL,
            ChequeNumber NVARCHAR(100) NULL,
            Amount DECIMAL(18,2) NULL,
            Dated NVARCHAR(50) NULL,
            Drawer NVARCHAR(300) NULL,
            Bank NVARCHAR(300) NULL,
            Branch NVARCHAR(300) NULL,
            Payee NVARCHAR(300) NULL
        );

        PRINT 'Created dbo.ChequeEncashmentCheques';
    END
    ELSE
    BEGIN
        PRINT 'dbo.ChequeEncashmentCheques already exists - checking missing columns';
    END;

    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'RequestId') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD RequestId INT NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'ChequeNumber') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD ChequeNumber NVARCHAR(100) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Amount') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Amount DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Dated') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Dated NVARCHAR(50) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Drawer') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Drawer NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Bank') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Bank NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Branch') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Branch NVARCHAR(300) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentCheques', 'Payee') IS NULL ALTER TABLE dbo.ChequeEncashmentCheques ADD Payee NVARCHAR(300) NULL;

    IF OBJECT_ID(N'dbo.ChequeEncashmentAttachments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ChequeEncashmentAttachments (
            Id INT IDENTITY(1,1) PRIMARY KEY,
            RequestId INT NOT NULL,
            FilePath NVARCHAR(1000) NOT NULL,
            FileName NVARCHAR(500) NULL,
            ContentType NVARCHAR(200) NULL,
            CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CEA_CreatedAt DEFAULT SYSUTCDATETIME()
        );

        PRINT 'Created dbo.ChequeEncashmentAttachments';
    END
    ELSE
    BEGIN
        PRINT 'dbo.ChequeEncashmentAttachments already exists - checking missing columns';
    END;

    IF COL_LENGTH('dbo.ChequeEncashmentAttachments', 'RequestId') IS NULL ALTER TABLE dbo.ChequeEncashmentAttachments ADD RequestId INT NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentAttachments', 'FilePath') IS NULL ALTER TABLE dbo.ChequeEncashmentAttachments ADD FilePath NVARCHAR(1000) NOT NULL CONSTRAINT DF_CEA_FilePath DEFAULT N'';
    IF COL_LENGTH('dbo.ChequeEncashmentAttachments', 'FileName') IS NULL ALTER TABLE dbo.ChequeEncashmentAttachments ADD FileName NVARCHAR(500) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentAttachments', 'ContentType') IS NULL ALTER TABLE dbo.ChequeEncashmentAttachments ADD ContentType NVARCHAR(200) NULL;
    IF COL_LENGTH('dbo.ChequeEncashmentAttachments', 'CreatedAt') IS NULL ALTER TABLE dbo.ChequeEncashmentAttachments ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_CEA_CreatedAt_2 DEFAULT SYSUTCDATETIME();

    IF OBJECT_ID(N'dbo.FK_ChequeEncashmentCheques_Request', N'F') IS NULL
       AND OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.ChequeEncashmentCheques', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE pt.object_id = OBJECT_ID(N'dbo.ChequeEncashmentCheques', N'U')
              AND rt.object_id = OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U')
              AND pc.name = N'RequestId'
              AND rc.name = N'Id'
        )
        BEGIN
            PRINT 'An equivalent FK already exists on dbo.ChequeEncashmentCheques(RequestId) -> dbo.ChequeEncashmentRequests(Id). Skipping new FK creation.';
        END
        ELSE
        BEGIN
            DECLARE @ChequeOrphans INT = 0;
            EXEC sp_executesql
                N'SELECT @Orphans = COUNT(*)
                  FROM dbo.ChequeEncashmentCheques c
                  LEFT JOIN dbo.ChequeEncashmentRequests r ON r.Id = c.RequestId
                  WHERE c.RequestId IS NOT NULL AND r.Id IS NULL;',
                N'@Orphans INT OUTPUT',
                @Orphans = @ChequeOrphans OUTPUT;

            IF @ChequeOrphans > 0
                PRINT CONCAT('Found ', @ChequeOrphans, ' orphan cheque rows. FK will be created WITH NOCHECK (existing rows not validated).');

            BEGIN TRY
                EXEC(N'ALTER TABLE dbo.ChequeEncashmentCheques
                      WITH NOCHECK ADD CONSTRAINT FK_ChequeEncashmentCheques_Request
                      FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id) ON DELETE CASCADE;');

                EXEC(N'ALTER TABLE dbo.ChequeEncashmentCheques
                      CHECK CONSTRAINT FK_ChequeEncashmentCheques_Request;');

                PRINT 'Created FK_ChequeEncashmentCheques_Request with ON DELETE CASCADE';
            END TRY
            BEGIN CATCH
                DECLARE @ChequeFkErr NVARCHAR(4000) = ERROR_MESSAGE();
                PRINT CONCAT('Could not create FK_ChequeEncashmentCheques_Request with CASCADE. Error: ', @ChequeFkErr);
                PRINT 'Retrying FK creation without ON DELETE CASCADE...';

                BEGIN TRY
                    EXEC(N'ALTER TABLE dbo.ChequeEncashmentCheques
                          WITH NOCHECK ADD CONSTRAINT FK_ChequeEncashmentCheques_Request
                          FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id);');

                    EXEC(N'ALTER TABLE dbo.ChequeEncashmentCheques
                          CHECK CONSTRAINT FK_ChequeEncashmentCheques_Request;');

                    PRINT 'Created FK_ChequeEncashmentCheques_Request without cascade delete';
                END TRY
                BEGIN CATCH
                    PRINT CONCAT('Warning: could not create FK_ChequeEncashmentCheques_Request at all. Error: ', ERROR_MESSAGE());
                END CATCH
            END CATCH
        END
    END;

    IF OBJECT_ID(N'dbo.FK_ChequeEncashmentAttachments_Request', N'F') IS NULL
       AND OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.ChequeEncashmentAttachments', N'U') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
            JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
            JOIN sys.columns pc ON pc.object_id = pt.object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id
            WHERE pt.object_id = OBJECT_ID(N'dbo.ChequeEncashmentAttachments', N'U')
              AND rt.object_id = OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U')
              AND pc.name = N'RequestId'
              AND rc.name = N'Id'
        )
        BEGIN
            PRINT 'An equivalent FK already exists on dbo.ChequeEncashmentAttachments(RequestId) -> dbo.ChequeEncashmentRequests(Id). Skipping new FK creation.';
        END
        ELSE
        BEGIN
            DECLARE @AttachmentOrphans INT = 0;
            EXEC sp_executesql
                N'SELECT @Orphans = COUNT(*)
                  FROM dbo.ChequeEncashmentAttachments a
                  LEFT JOIN dbo.ChequeEncashmentRequests r ON r.Id = a.RequestId
                  WHERE a.RequestId IS NOT NULL AND r.Id IS NULL;',
                N'@Orphans INT OUTPUT',
                @Orphans = @AttachmentOrphans OUTPUT;

            IF @AttachmentOrphans > 0
                PRINT CONCAT('Found ', @AttachmentOrphans, ' orphan attachment rows. FK will be created WITH NOCHECK (existing rows not validated).');

            BEGIN TRY
                EXEC(N'ALTER TABLE dbo.ChequeEncashmentAttachments
                      WITH NOCHECK ADD CONSTRAINT FK_ChequeEncashmentAttachments_Request
                      FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id) ON DELETE CASCADE;');

                EXEC(N'ALTER TABLE dbo.ChequeEncashmentAttachments
                      CHECK CONSTRAINT FK_ChequeEncashmentAttachments_Request;');

                PRINT 'Created FK_ChequeEncashmentAttachments_Request with ON DELETE CASCADE';
            END TRY
            BEGIN CATCH
                DECLARE @AttachFkErr NVARCHAR(4000) = ERROR_MESSAGE();
                PRINT CONCAT('Could not create FK_ChequeEncashmentAttachments_Request with CASCADE. Error: ', @AttachFkErr);
                PRINT 'Retrying FK creation without ON DELETE CASCADE...';

                BEGIN TRY
                    EXEC(N'ALTER TABLE dbo.ChequeEncashmentAttachments
                          WITH NOCHECK ADD CONSTRAINT FK_ChequeEncashmentAttachments_Request
                          FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id);');

                    EXEC(N'ALTER TABLE dbo.ChequeEncashmentAttachments
                          CHECK CONSTRAINT FK_ChequeEncashmentAttachments_Request;');

                    PRINT 'Created FK_ChequeEncashmentAttachments_Request without cascade delete';
                END TRY
                BEGIN CATCH
                    PRINT CONCAT('Warning: could not create FK_ChequeEncashmentAttachments_Request at all. Error: ', ERROR_MESSAGE());
                END CATCH
            END CATCH
        END
    END;

    COMMIT TRAN;
    PRINT 'Cheque encashment schema aligned successfully';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddChequeEncashmentTables.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
