-- Extends SystemUsers with fields that were previously only cached on-device (mobile app's
-- sessionProfile.ts) and never actually persisted server-side, plus profile photo/signature
-- columns so the mobile Edit Profile screen can save them durably.

IF COL_LENGTH('dbo.SystemUsers', 'AlternativePhone') IS NULL
    ALTER TABLE dbo.SystemUsers ADD AlternativePhone NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'PlaceOfWork') IS NULL
    ALTER TABLE dbo.SystemUsers ADD PlaceOfWork NVARCHAR(200) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'WorkTelephone') IS NULL
    ALTER TABLE dbo.SystemUsers ADD WorkTelephone NVARCHAR(50) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'WorkPhysicalAddress') IS NULL
    ALTER TABLE dbo.SystemUsers ADD WorkPhysicalAddress NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'ProfilePhotoPath') IS NULL
    ALTER TABLE dbo.SystemUsers ADD ProfilePhotoPath NVARCHAR(500) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'ClientSignature') IS NULL
    ALTER TABLE dbo.SystemUsers ADD ClientSignature NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.SystemUsers', 'ClientSignDate') IS NULL
    ALTER TABLE dbo.SystemUsers ADD ClientSignDate DATETIME2 NULL;