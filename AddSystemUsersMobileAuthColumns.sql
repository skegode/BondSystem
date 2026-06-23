USE OnwardsSwiftDB;
GO

IF COL_LENGTH('dbo.SystemUsers', 'PinHash') IS NULL
    ALTER TABLE dbo.SystemUsers ADD PinHash NVARCHAR(500) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'NationalId') IS NULL
    ALTER TABLE dbo.SystemUsers ADD NationalId NVARCHAR(50) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'PinUpdatedAt') IS NULL
    ALTER TABLE dbo.SystemUsers ADD PinUpdatedAt DATETIME2 NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'IprsVerified') IS NULL
    ALTER TABLE dbo.SystemUsers ADD IprsVerified BIT NOT NULL CONSTRAINT DF_SystemUsers_IprsVerified DEFAULT (0);
GO

IF COL_LENGTH('dbo.SystemUsers', 'IprsReference') IS NULL
    ALTER TABLE dbo.SystemUsers ADD IprsReference NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'KraPin') IS NULL
    ALTER TABLE dbo.SystemUsers ADD KraPin NVARCHAR(30) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'Gender') IS NULL
    ALTER TABLE dbo.SystemUsers ADD Gender NVARCHAR(20) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'PostalAddress') IS NULL
    ALTER TABLE dbo.SystemUsers ADD PostalAddress NVARCHAR(300) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'PhysicalAddress') IS NULL
    ALTER TABLE dbo.SystemUsers ADD PhysicalAddress NVARCHAR(500) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'FullName') IS NULL
    ALTER TABLE dbo.SystemUsers ADD FullName NVARCHAR(200) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'Email') IS NULL
    ALTER TABLE dbo.SystemUsers ADD Email NVARCHAR(256) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'Phone') IS NULL
    ALTER TABLE dbo.SystemUsers ADD Phone NVARCHAR(64) NULL;
GO

IF COL_LENGTH('dbo.SystemUsers', 'IsActive') IS NULL
    ALTER TABLE dbo.SystemUsers ADD IsActive BIT NOT NULL CONSTRAINT DF_SystemUsers_IsActive DEFAULT (1) WITH VALUES;
GO

IF COL_LENGTH('dbo.SystemUsers', 'CreatedAt') IS NULL
    ALTER TABLE dbo.SystemUsers ADD CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_SystemUsers_CreatedAt DEFAULT SYSUTCDATETIME() WITH VALUES;
GO

IF COL_LENGTH('dbo.SystemUsers', 'UpdatedAt') IS NULL
    ALTER TABLE dbo.SystemUsers ADD UpdatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_SystemUsers_UpdatedAt DEFAULT SYSUTCDATETIME() WITH VALUES;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SystemUsers_NationalId'
      AND object_id = OBJECT_ID('dbo.SystemUsers')
)
BEGIN
    CREATE UNIQUE INDEX IX_SystemUsers_NationalId
        ON dbo.SystemUsers(NationalId)
        WHERE NationalId IS NOT NULL AND IsDeleted = 0;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_SystemUsers_Phone'
      AND object_id = OBJECT_ID('dbo.SystemUsers')
)
BEGIN
    CREATE INDEX IX_SystemUsers_Phone
        ON dbo.SystemUsers(Phone)
        WHERE Phone IS NOT NULL AND IsDeleted = 0;
END
GO

IF OBJECT_ID('dbo.pin_reset_audit', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.pin_reset_audit
    (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NOT NULL,
        Phone NVARCHAR(30) NOT NULL,
        NationalId NVARCHAR(50) NOT NULL,
        RequestId NVARCHAR(50) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_pin_reset_audit_CreatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE INDEX IX_pin_reset_audit_UserId_CreatedAt
        ON dbo.pin_reset_audit(UserId, CreatedAt DESC);
END
GO

IF OBJECT_ID('dbo.signup_wizard_profiles', 'U') IS NOT NULL
BEGIN
    ;WITH latest_profile AS
    (
        SELECT
            UPPER(REPLACE(REPLACE(ISNULL(national_id, ''), ' ', ''), '-', '')) AS normalized_national_id,
            NULLIF(LTRIM(RTRIM(full_name)), '') AS full_name,
            NULLIF(LTRIM(RTRIM(email)), '') AS email,
            NULLIF(LTRIM(RTRIM(phone)), '') AS phone,
            NULLIF(LTRIM(RTRIM(kra_pin)), '') AS kra_pin,
            NULLIF(LTRIM(RTRIM(gender)), '') AS gender,
            NULLIF(LTRIM(RTRIM(postal_address)), '') AS postal_address,
            NULLIF(LTRIM(RTRIM(physical_address)), '') AS physical_address,
            NULLIF(LTRIM(RTRIM(iprs_reference)), '') AS iprs_reference,
            identity_verified,
            ROW_NUMBER() OVER
            (
                PARTITION BY UPPER(REPLACE(REPLACE(ISNULL(national_id, ''), ' ', ''), '-', ''))
                ORDER BY updated_at_utc DESC, created_at_utc DESC
            ) AS rn
        FROM dbo.signup_wizard_profiles
        WHERE ISNULL(account_created, 0) = 1
    )
    UPDATE su
    SET
        FullName = COALESCE(NULLIF(LTRIM(RTRIM(su.FullName)), ''), lp.full_name),
        Email = COALESCE(NULLIF(LTRIM(RTRIM(su.Email)), ''), lp.email),
        Phone = COALESCE(NULLIF(LTRIM(RTRIM(su.Phone)), ''), lp.phone),
        KraPin = COALESCE(NULLIF(LTRIM(RTRIM(su.KraPin)), ''), lp.kra_pin),
        Gender = COALESCE(NULLIF(LTRIM(RTRIM(su.Gender)), ''), lp.gender),
        PostalAddress = COALESCE(NULLIF(LTRIM(RTRIM(su.PostalAddress)), ''), lp.postal_address),
        PhysicalAddress = COALESCE(NULLIF(LTRIM(RTRIM(su.PhysicalAddress)), ''), lp.physical_address),
        IprsReference = COALESCE(NULLIF(LTRIM(RTRIM(su.IprsReference)), ''), lp.iprs_reference),
        IprsVerified = CASE WHEN su.IprsVerified = 1 THEN 1 ELSE ISNULL(lp.identity_verified, 0) END,
        UpdatedAt = SYSUTCDATETIME()
    FROM dbo.SystemUsers su
    INNER JOIN latest_profile lp
        ON UPPER(REPLACE(REPLACE(ISNULL(su.NationalId, ''), ' ', ''), '-', '')) = lp.normalized_national_id
    WHERE lp.rn = 1
      AND ISNULL(su.IsDeleted, 0) = 0
      AND (
          NULLIF(LTRIM(RTRIM(su.FullName)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.Email)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.Phone)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.KraPin)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.Gender)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.PostalAddress)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.PhysicalAddress)), '') IS NULL OR
          NULLIF(LTRIM(RTRIM(su.IprsReference)), '') IS NULL OR
          ISNULL(su.IprsVerified, 0) = 0
      );
END
GO
