-- AddPermissionsTables.sql
-- One initialization for the whole RBAC system, split into two clearly differentiated parts
-- that share the same dbo.Permissions catalog:
--
--   PART 1 -- WEB PORTAL (role-based): dbo.RolePermissions grants a permission to every user
--             of a given Role. Used for staff-only features like Official Use (Step 3).
--   PART 2 -- MOBILE APP (per-user): dbo.UserPermissions grants a permission to one specific
--             user. Used to gate a brand-new client signup's PIN login until staff reviews
--             and activates that individual account.
--
-- Role values match OnwardsSwift.Core.Enums.UserRole: Admin=1, RelationshipManager=2,
-- CreditOfficer=3, Client=4, Auditor=5.
-- This script is also auto-applied at API startup by PermissionService.EnsureSchemaAndSeedAsync,
-- so running it manually is optional / for documentation and ops visibility.

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    -- ════════════════════════════════════════════════════════════════════
    -- Shared catalog: dbo.Permissions
    -- ════════════════════════════════════════════════════════════════════

    IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Permissions (
            Id          INT IDENTITY(1,1) PRIMARY KEY,
            Code        NVARCHAR(100) NOT NULL,
            Description NVARCHAR(300) NULL,
            CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Permissions_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT UQ_Permissions_Code UNIQUE (Code)
        );

        PRINT 'Created dbo.Permissions';
    END
    ELSE
    BEGIN
        PRINT 'dbo.Permissions already exists - skipped.';
    END;

    -- ════════════════════════════════════════════════════════════════════
    -- PART 1: WEB PORTAL -- role-based permissions (dbo.RolePermissions)
    -- ════════════════════════════════════════════════════════════════════

    IF OBJECT_ID(N'dbo.RolePermissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.RolePermissions (
            Id           INT IDENTITY(1,1) PRIMARY KEY,
            Role         INT NOT NULL,
            PermissionId INT NOT NULL,
            CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_RolePermissions_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT UQ_RolePermissions UNIQUE (Role, PermissionId),
            CONSTRAINT FK_RolePermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
        );

        PRINT 'Created dbo.RolePermissions';
    END
    ELSE
    BEGIN
        PRINT 'dbo.RolePermissions already exists - skipped.';
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'OfficialUse.Edit')
    BEGIN
        INSERT INTO dbo.Permissions (Code, Description)
        VALUES (N'OfficialUse.Edit', N'View and submit Official Use (Step 3) approvals for cheque/bond onboarding requests.');

        PRINT 'Seeded permission OfficialUse.Edit';
    END;

    DECLARE @OfficialUsePermissionId INT = (SELECT Id FROM dbo.Permissions WHERE Code = N'OfficialUse.Edit');

    -- Admin=1, RelationshipManager=2, CreditOfficer=3
    INSERT INTO dbo.RolePermissions (Role, PermissionId)
    SELECT r.Role, @OfficialUsePermissionId
    FROM (VALUES (1), (2), (3)) AS r(Role)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.RolePermissions rp
        WHERE rp.Role = r.Role AND rp.PermissionId = @OfficialUsePermissionId
    );

    PRINT 'Seeded RolePermissions for OfficialUse.Edit (Admin, RelationshipManager, CreditOfficer)';

    -- ════════════════════════════════════════════════════════════════════
    -- PART 2: MOBILE APP -- per-user permissions (dbo.UserPermissions)
    -- A new client signup (SystemUsers.Role = 4) cannot sign in with their PIN until staff
    -- explicitly grants Mobile.Login to their specific account (see Admin > User Management).
    -- Existing already-active clients are backfilled here so they are not locked out --
    -- this only affects clients created AFTER this script first runs.
    -- ════════════════════════════════════════════════════════════════════

    DECLARE @ClientRole INT = 4;
    DECLARE @UserPermissionsExisted BIT = CASE WHEN OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL THEN 0 ELSE 1 END;

    IF @UserPermissionsExisted = 0
    BEGIN
        CREATE TABLE dbo.UserPermissions (
            Id           INT IDENTITY(1,1) PRIMARY KEY,
            UserId       INT NOT NULL,
            PermissionId INT NOT NULL,
            GrantedAt    DATETIME2     NOT NULL CONSTRAINT DF_UserPermissions_GrantedAt DEFAULT SYSUTCDATETIME(),
            GrantedBy    NVARCHAR(200) NULL,
            CONSTRAINT UQ_UserPermissions UNIQUE (UserId, PermissionId),
            CONSTRAINT FK_UserPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
        );

        PRINT 'Created dbo.UserPermissions';
    END
    ELSE
    BEGIN
        PRINT 'dbo.UserPermissions already exists - skipped.';
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'Mobile.Login')
    BEGIN
        INSERT INTO dbo.Permissions (Code, Description)
        VALUES (N'Mobile.Login', N'Allows this specific client to sign in to the mobile app with their PIN. Granted per-user by staff after review.');

        PRINT 'Seeded permission Mobile.Login';
    END;

    DECLARE @MobileLoginPermissionId INT = (SELECT Id FROM dbo.Permissions WHERE Code = N'Mobile.Login');

    IF @UserPermissionsExisted = 0
    BEGIN
        INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy)
        SELECT u.Id, @MobileLoginPermissionId, N'system-backfill'
        FROM dbo.SystemUsers u
        WHERE u.Role = @ClientRole AND u.IsActive = 1 AND u.IsDeleted = 0;

        PRINT 'Backfilled Mobile.Login for existing active clients';
    END;

    COMMIT TRAN;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'-');

    RAISERROR('AddPermissionsTables.sql failed at line %d in %s: %s', 16, 1, @ErrLine, @ErrProc, @ErrMsg);
END CATCH;
