-- AddOfficialUseRecordsTable.sql
-- Stores Official Use (Step 3) submissions from the Client Onboarding Wizard

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF OBJECT_ID(N'dbo.OfficialUseRecords', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.OfficialUseRecords (
            Id                       INT IDENTITY(1,1) PRIMARY KEY,
            RequestId                INT NULL,
            CheckedBy                NVARCHAR(200) NULL,
            CheckedSignature         NVARCHAR(200) NULL,
            CheckedDate              NVARCHAR(50)  NULL,
            ConfirmedWith            NVARCHAR(200) NULL,
            Designation              NVARCHAR(200) NULL,
            BuildingStreet           NVARCHAR(500) NULL,
            DrawerStatus             NVARCHAR(200) NULL,
            ReasonForPayment         NVARCHAR(500) NULL,
            AccountConfirmedBy       NVARCHAR(200) NULL,
            AccountStatus            NVARCHAR(200) NULL,
            HeadOfTradeFinance       NVARCHAR(200) NULL,
            HeadOfTradeSignature     NVARCHAR(200) NULL,
            HeadOfTradeDate          NVARCHAR(50)  NULL,
            InChargeFinance          NVARCHAR(200) NULL,
            InChargeFinanceSignature NVARCHAR(200) NULL,
            InChargeFinanceDate      NVARCHAR(50)  NULL,
            CEO                      NVARCHAR(200) NULL,
            CEOSignature             NVARCHAR(200) NULL,
            CEODate                  NVARCHAR(50)  NULL,
            PaidByName               NVARCHAR(200) NULL,
            PaidBySignature          NVARCHAR(200) NULL,
            CreatedAt                DATETIME2     NOT NULL CONSTRAINT DF_OfficialUseRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
            CreatedBy                NVARCHAR(200) NULL
        );

        PRINT 'Created dbo.OfficialUseRecords';
    END
    ELSE
    BEGIN
        PRINT 'dbo.OfficialUseRecords already exists - skipped.';
    END;

    IF OBJECT_ID(N'dbo.ChequeEncashmentRequests', N'U') IS NOT NULL
       AND OBJECT_ID(N'dbo.FK_OfficialUseRecords_Request', N'F') IS NULL
    BEGIN
        ALTER TABLE dbo.OfficialUseRecords
        WITH CHECK ADD CONSTRAINT FK_OfficialUseRecords_Request
            FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id);

        PRINT 'Created FK_OfficialUseRecords_Request';
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddOfficialUseRecordsTable.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
