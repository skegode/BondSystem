using Dapper;
using System.Security.Claims;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.MobileLocal.Services;

public class SessionTokenValidator
{
    private readonly DapperContext _context;

    public SessionTokenValidator(DapperContext context)
    {
        _context = context;
    }

    public async Task<bool> IsTokenActiveAsync(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var jti = principal.FindFirstValue("jti");

        if (!long.TryParse(sub, out var userId) || string.IsNullOrWhiteSpace(jti))
        {
            return false;
        }

        using var conn = _context.CreateConnection();
        await conn.OpenAsync();

        var sql = @"
SELECT COUNT(1)
FROM dbo.MobileSessions
WHERE user_id = @userId
  AND jti = @jti
  AND is_revoked = 0
  AND expires_at_utc > @nowUtc";

                var count = await conn.ExecuteScalarAsync<long>(sql, new { userId, jti, nowUtc = DateTime.UtcNow });
        return count > 0;
    }
}
