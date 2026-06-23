-- AddProductTypeDocumentsTable.sql
-- Lets staff configure, per dbo.ProductTypes row, which documents the mobile app's
-- Bond Application wizard should require/upload for that bond type. Consumed by the
-- mobile API at GET /api/bonds/types (MobileBondsController) and managed in the web
-- portal under Admin > Product Types.

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    -- dbo.ProductTypes.Id was created as a plain IDENTITY column with no PRIMARY KEY,
    -- so it can't be referenced by a foreign key yet. Add it now (safe: no duplicate
    -- Id values exist as of this script's authoring).
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProductTypes') AND is_primary_key = 1
    )
    BEGIN
        ALTER TABLE dbo.ProductTypes ADD CONSTRAINT PK_ProductTypes PRIMARY KEY (Id);
        PRINT 'Added PK_ProductTypes';
    END;

    IF OBJECT_ID(N'dbo.ProductTypeDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ProductTypeDocuments (
            Id            INT IDENTITY(1,1) PRIMARY KEY,
            ProductTypeId INT NOT NULL,
            DocumentKey   NVARCHAR(100) NOT NULL,
            Label         NVARCHAR(200) NOT NULL,
            Description   NVARCHAR(500) NULL,
            Required      BIT NOT NULL CONSTRAINT DF_ProductTypeDocuments_Required DEFAULT 1,
            SortOrder     INT NOT NULL CONSTRAINT DF_ProductTypeDocuments_SortOrder DEFAULT 0,
            CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_ProductTypeDocuments_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT UQ_ProductTypeDocuments UNIQUE (ProductTypeId, DocumentKey),
            CONSTRAINT FK_ProductTypeDocuments_ProductType FOREIGN KEY (ProductTypeId) REFERENCES dbo.ProductTypes(Id)
        );

        PRINT 'Created dbo.ProductTypeDocuments';
    END
    ELSE
    BEGIN
        PRINT 'dbo.ProductTypeDocuments already exists - skipped.';
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddProductTypeDocumentsTable.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
