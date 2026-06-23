-- Persists unhandled exceptions caught by the global UseExceptionHandler so staff have
-- somewhere to look when something breaks, instead of relying on console/file logs.
-- Also self-created on startup by ApplicationErrorLogger.EnsureSchemaAsync(); this script
-- exists for explicit/manual application against an environment, per repo convention.

IF OBJECT_ID(N'dbo.ApplicationErrors', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApplicationErrors (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        ExceptionType NVARCHAR(300)  NOT NULL,
        Message       NVARCHAR(MAX)  NULL,
        StackTrace    NVARCHAR(MAX)  NULL,
        RequestPath   NVARCHAR(500)  NULL,
        UserName      NVARCHAR(200)  NULL,
        CreatedAtUtc  DATETIME2      NOT NULL CONSTRAINT DF_ApplicationErrors_CreatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_ApplicationErrors_CreatedAtUtc ON dbo.ApplicationErrors(CreatedAtUtc DESC);
END;
