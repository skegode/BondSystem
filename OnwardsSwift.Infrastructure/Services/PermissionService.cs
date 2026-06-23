using System.Security.Claims;
using Dapper;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.Infrastructure.Services
{
    /// <summary>
    /// General-purpose RBAC: Permissions catalog + Role-to-Permission mapping (dbo.Permissions /
    /// dbo.RolePermissions). Auto-creates and seeds defaults on startup so the system can never
    /// boot into an undefined-permissions state; <see cref="IsInitializedAsync"/> is a defensive
    /// check for callers (e.g. mobile PIN sign-in) that want to refuse to operate otherwise.
    /// </summary>
    public class PermissionService
    {
        public const string OfficialUseEdit = "OfficialUse.Edit";

        /// <summary>
        /// Per-user (not per-role) gate: a newly signed-up mobile client cannot sign in with their
        /// PIN until a staff member explicitly grants this permission to their specific account.
        /// Granted via dbo.UserPermissions, keyed on SystemUsers.Id.
        /// </summary>
        public const string MobileLogin = "Mobile.Login";

        private const int ClientRole = 4;

        private readonly DapperContext _ctx;

        public PermissionService(DapperContext ctx) => _ctx = ctx;

        /// <summary>
        /// Single initialization for the whole RBAC system, covering both the web portal
        /// (role-based, dbo.RolePermissions) and the mobile app (per-user, dbo.UserPermissions)
        /// against the shared dbo.Permissions catalog. Mirrors AddPermissionsTables.sql.
        /// </summary>
        public async Task EnsureSchemaAndSeedAsync()
        {
            using var conn = _ctx.Create();

            // Shared catalog.
            await conn.ExecuteAsync(@"
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Permissions (
        Id          INT IDENTITY(1,1) PRIMARY KEY,
        Code        NVARCHAR(100) NOT NULL,
        Description NVARCHAR(300) NULL,
        CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_Permissions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_Permissions_Code UNIQUE (Code)
    );
END;

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
END;");

            // PART 1: WEB PORTAL -- role-based permissions (dbo.RolePermissions).
            if (await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Permissions WHERE Code = @Code", new { Code = OfficialUseEdit }) == 0)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO dbo.Permissions (Code, Description) VALUES (@Code, @Description)",
                    new { Code = OfficialUseEdit, Description = "View and submit Official Use (Step 3) approvals for cheque/bond onboarding requests." });
            }

            var officialUsePermissionId = await conn.ExecuteScalarAsync<int>(
                "SELECT Id FROM dbo.Permissions WHERE Code = @Code", new { Code = OfficialUseEdit });

            foreach (var role in new[] { UserRole.Admin, UserRole.RelationshipManager, UserRole.CreditOfficer })
            {
                await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM dbo.RolePermissions WHERE Role = @Role AND PermissionId = @PermissionId)
    INSERT INTO dbo.RolePermissions (Role, PermissionId) VALUES (@Role, @PermissionId);",
                    new { Role = (int)role, PermissionId = officialUsePermissionId });
            }

            // PART 2: MOBILE APP -- per-user permissions (dbo.UserPermissions), created once.
            // If this is the first time it's being created, backfill every already-active client
            // so existing users are not suddenly locked out; only clients signing up AFTER this
            // point start ungranted, pending staff review (see AdminController.GrantMobileAccess).
            var userPermissionsTableExisted = await conn.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.UserPermissions', N'U') IS NULL THEN 0 ELSE 1 END") == 1;

            if (!userPermissionsTableExisted)
            {
                await conn.ExecuteAsync(@"
CREATE TABLE dbo.UserPermissions (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    UserId       INT NOT NULL,
    PermissionId INT NOT NULL,
    GrantedAt    DATETIME2     NOT NULL CONSTRAINT DF_UserPermissions_GrantedAt DEFAULT SYSUTCDATETIME(),
    GrantedBy    NVARCHAR(200) NULL,
    CONSTRAINT UQ_UserPermissions UNIQUE (UserId, PermissionId),
    CONSTRAINT FK_UserPermissions_Permission FOREIGN KEY (PermissionId) REFERENCES dbo.Permissions(Id)
);");
            }

            if (await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM dbo.Permissions WHERE Code = @Code", new { Code = MobileLogin }) == 0)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO dbo.Permissions (Code, Description) VALUES (@Code, @Description)",
                    new { Code = MobileLogin, Description = "Allows this specific client to sign in to the mobile app with their PIN. Granted per-user by staff after review." });
            }

            var mobileLoginPermissionId = await conn.ExecuteScalarAsync<int>(
                "SELECT Id FROM dbo.Permissions WHERE Code = @Code", new { Code = MobileLogin });

            if (!userPermissionsTableExisted)
            {
                await conn.ExecuteAsync(@"
INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy)
SELECT u.Id, @PermissionId, 'system-backfill'
FROM dbo.SystemUsers u
WHERE u.Role = @ClientRole AND u.IsActive = 1 AND u.IsDeleted = 0;",
                    new { PermissionId = mobileLoginPermissionId, ClientRole });
            }
        }

        public async Task<bool> IsInitializedAsync()
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM dbo.Permissions") > 0;
        }

        public async Task<bool> RoleHasPermissionAsync(string? roleName, string permissionCode)
        {
            if (string.IsNullOrWhiteSpace(roleName) || !Enum.TryParse<UserRole>(roleName, true, out var role))
            {
                return false;
            }

            using var conn = _ctx.Create();
            var count = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.RolePermissions rp
JOIN dbo.Permissions p ON p.Id = rp.PermissionId
WHERE rp.Role = @Role AND p.Code = @Code;",
                new { Role = (int)role, Code = permissionCode });

            return count > 0;
        }

        public Task<bool> UserHasPermissionAsync(ClaimsPrincipal user, string permissionCode) =>
            RoleHasPermissionAsync(user.FindFirst(ClaimTypes.Role)?.Value, permissionCode);

        public async Task<bool> UserHasDirectPermissionAsync(int userId, string permissionCode)
        {
            using var conn = _ctx.Create();
            var count = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.UserPermissions up
JOIN dbo.Permissions p ON p.Id = up.PermissionId
WHERE up.UserId = @UserId AND p.Code = @Code;",
                new { UserId = userId, Code = permissionCode });

            return count > 0;
        }

        public async Task GrantUserPermissionAsync(int userId, string permissionCode, string? grantedBy)
        {
            using var conn = _ctx.Create();
            var permissionId = await conn.ExecuteScalarAsync<int>(
                "SELECT Id FROM dbo.Permissions WHERE Code = @Code", new { Code = permissionCode });

            await conn.ExecuteAsync(@"
IF NOT EXISTS (SELECT 1 FROM dbo.UserPermissions WHERE UserId = @UserId AND PermissionId = @PermissionId)
    INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy) VALUES (@UserId, @PermissionId, @GrantedBy);",
                new { UserId = userId, PermissionId = permissionId, GrantedBy = grantedBy });
        }

        public async Task RevokeUserPermissionAsync(int userId, string permissionCode)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync(@"
DELETE up FROM dbo.UserPermissions up
JOIN dbo.Permissions p ON p.Id = up.PermissionId
WHERE up.UserId = @UserId AND p.Code = @Code;",
                new { UserId = userId, Code = permissionCode });
        }

        public async Task<List<ClientAccessRow>> GetClientAccessListAsync()
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<ClientAccessRow>(@"
SELECT
    u.Id,
    u.FullName,
    u.Phone,
    u.Email,
    u.IsActive,
    u.CreatedAt,
    CASE WHEN up.Id IS NULL THEN 0 ELSE 1 END AS HasMobileLogin
FROM dbo.SystemUsers u
LEFT JOIN dbo.UserPermissions up ON up.UserId = u.Id
    AND up.PermissionId = (SELECT Id FROM dbo.Permissions WHERE Code = @Code)
WHERE u.Role = @ClientRole AND u.IsDeleted = 0
ORDER BY u.CreatedAt DESC;",
                new { Code = MobileLogin, ClientRole });

            return rows.ToList();
        }
    }

    public class ClientAccessRow
    {
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasMobileLogin { get; set; }
    }
}
