-- AddChequeEncashmentPaymentMethodColumns.sql
-- Adds applicant Category (Individual/Company) and disbursement PaymentMethod
-- (MPESA/BANK) columns to dbo.ChequeEncashmentRequests.
-- Individual applicants are paid via M-Pesa (existing Phone column); Company
-- applicants are paid via Bank Transfer (DisburseBank / DisburseAccount).

SET NOCOUNT ON;

PRINT 'Running AddChequeEncashmentPaymentMethodColumns.sql on database: ' + DB_NAME();

IF DB_NAME() = N'master'
BEGIN
    RAISERROR('Do not run AddChequeEncashmentPaymentMethodColumns.sql on master. Switch to the application database (for example: USE OnwardsSwiftDB).', 16, 1);
    RETURN;
END;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U') IS NULL
    BEGIN
        RAISERROR('dbo.ChequeEncashmentRequests does not exist. Run AddChequeEncashmentTables.sql first.', 16, 1);
    END;

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'Category') IS NULL
        ALTER TABLE dbo.ChequeEncashmentRequests ADD Category NVARCHAR(20) NOT NULL CONSTRAINT DF_CER_Category DEFAULT 'Individual';

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'PaymentMethod') IS NULL
        ALTER TABLE dbo.ChequeEncashmentRequests ADD PaymentMethod NVARCHAR(20) NOT NULL CONSTRAINT DF_CER_PaymentMethod DEFAULT 'MPESA';

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'DisburseBank') IS NULL
        ALTER TABLE dbo.ChequeEncashmentRequests ADD DisburseBank NVARCHAR(300) NULL;

    IF COL_LENGTH('dbo.ChequeEncashmentRequests', 'DisburseAccount') IS NULL
        ALTER TABLE dbo.ChequeEncashmentRequests ADD DisburseAccount NVARCHAR(100) NULL;

    COMMIT TRAN;
    PRINT 'Cheque encashment payment-method schema aligned successfully';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddChequeEncashmentPaymentMethodColumns.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
