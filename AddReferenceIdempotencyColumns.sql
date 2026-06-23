-- AddReferenceIdempotencyColumns.sql
-- Adds a client-generated Reference column to ChequeEncashmentRequests and BondApplications
-- so a retried/duplicated sync (the same submission sent twice because the app never saw the
-- first response) can be recognized and deduplicated server-side, instead of creating a second
-- row. Nullable for backward compatibility with existing rows and older app versions that don't
-- send it yet -- a FILTERED unique index allows any number of NULLs while still enforcing
-- uniqueness among the references that ARE provided.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRY
    BEGIN TRAN;

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'Reference') IS NULL
    BEGIN
        ALTER TABLE dbo.ChequeEncashmentRequests ADD Reference NVARCHAR(100) NULL;
        PRINT 'Added ChequeEncashmentRequests.Reference';
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ChequeEncashmentRequests_Reference')
    BEGIN
        -- EXEC: the column added above isn't visible to this batch's compile-time name
        -- resolution otherwise ("Invalid column name 'Reference'").
        EXEC(N'CREATE UNIQUE INDEX UQ_ChequeEncashmentRequests_Reference
            ON dbo.ChequeEncashmentRequests(Reference)
            WHERE Reference IS NOT NULL;');
        PRINT 'Created UQ_ChequeEncashmentRequests_Reference';
    END;

    IF COL_LENGTH('dbo.BondApplications', 'Reference') IS NULL
    BEGIN
        ALTER TABLE dbo.BondApplications ADD Reference NVARCHAR(100) NULL;
        PRINT 'Added BondApplications.Reference';
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_BondApplications_Reference')
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX UQ_BondApplications_Reference
            ON dbo.BondApplications(Reference)
            WHERE Reference IS NOT NULL;');
        PRINT 'Created UQ_BondApplications_Reference';
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddReferenceIdempotencyColumns.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
