using Dapper;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.Infrastructure.Services
{
    /// <summary>
    /// Persists unhandled exceptions to dbo.ApplicationErrors so staff have somewhere to look
    /// when something breaks in production, instead of relying on whoever happens to notice a
    /// user complaint. No external monitoring service is wired up -- this is the zero-dependency
    /// floor: a queryable record of what broke, when, and for whom.
    /// </summary>
    public class ApplicationErrorLogger
    {
        private readonly DapperContext _ctx;

        public ApplicationErrorLogger(DapperContext ctx) => _ctx = ctx;

        public async Task EnsureSchemaAsync()
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync(@"
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
END;");
        }

        public async Task LogAsync(Exception ex, string? requestPath, string? userName)
        {
            try
            {
                using var conn = _ctx.Create();
                await conn.ExecuteAsync(@"
INSERT INTO dbo.ApplicationErrors (ExceptionType, Message, StackTrace, RequestPath, UserName)
VALUES (@ExceptionType, @Message, @StackTrace, @RequestPath, @UserName);",
                    new
                    {
                        ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                        Message = ex.Message,
                        StackTrace = ex.ToString(),
                        RequestPath = requestPath,
                        UserName = userName
                    });
            }
            catch
            {
                // The DB itself may be the thing that's down -- swallow rather than mask the
                // original exception with a logging failure.
            }
        }

        public async Task<List<ApplicationErrorRow>> GetRecentAsync(int take = 100)
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<ApplicationErrorRow>(
                "SELECT TOP (@take) Id, ExceptionType, Message, StackTrace, RequestPath, UserName, CreatedAtUtc FROM dbo.ApplicationErrors ORDER BY CreatedAtUtc DESC;",
                new { take });
            return rows.ToList();
        }
    }

    public class ApplicationErrorRow
    {
        public int Id { get; set; }
        public string ExceptionType { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string? RequestPath { get; set; }
        public string? UserName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
