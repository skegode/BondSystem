-- AddDeclarationFieldsToChequeEncashment.sql
-- Adds Declaration fields to ChequeEncashmentRequests for persistence

SET NOCOUNT ON;

PRINT 'Running AddDeclarationFieldsToChequeEncashment.sql on database: ' + DB_NAME();

IF DB_NAME() = N'master'
BEGIN
    RAISERROR('Do not run AddDeclarationFieldsToChequeEncashment.sql on master. Switch to the application database (for example: USE OnwardsSwiftDB).', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'DeclarantName') IS NULL
    BEGIN
        ALTER TABLE dbo.ChequeEncashmentRequests ADD DeclarantName NVARCHAR(300) NULL;
        PRINT 'Added DeclarantName column to dbo.ChequeEncashmentRequests';
    END
    ELSE
        PRINT 'DeclarantName column already exists';

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'DeclarantRole') IS NULL
    BEGIN
        ALTER TABLE dbo.ChequeEncashmentRequests ADD DeclarantRole NVARCHAR(300) NULL;
        PRINT 'Added DeclarantRole column to dbo.ChequeEncashmentRequests';
    END
    ELSE
        PRINT 'DeclarantRole column already exists';

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'DeclarantDate') IS NULL
    BEGIN
        ALTER TABLE dbo.ChequeEncashmentRequests ADD DeclarantDate NVARCHAR(50) NULL;
        PRINT 'Added DeclarantDate column to dbo.ChequeEncashmentRequests';
    END
    ELSE
        PRINT 'DeclarantDate column already exists';

    COMMIT;
    PRINT 'AddDeclarationFieldsToChequeEncashment.sql completed successfully.';
END TRY
BEGIN CATCH
    ROLLBACK;
    PRINT 'ERROR: ' + ERROR_MESSAGE();
    THROW;
END CATCH;
