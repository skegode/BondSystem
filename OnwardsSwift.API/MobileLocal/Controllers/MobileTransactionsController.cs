using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[Route("api/transactions")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MobileTransactionsController : MobileApiControllerBase
{
    private readonly LocalSqliteContext _db;

    public MobileTransactionsController(LocalSqliteContext db)
    {
        _db = db;
    }

    [HttpGet("history")]
    public async Task<ActionResult<ApiEnvelope<object>>> History([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = GetUserIdOrThrow();
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var offset = (page - 1) * pageSize;

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        // Return only the latest row per (source_type, source_id) so that each cheque/bond
        // submission appears once even though CreateRequest and SaveOfficialUse/SaveIndemnity
        // both insert into transaction_history.
        var total = await conn.ExecuteScalarAsync<long>(@"
SELECT COUNT(*) FROM (
  SELECT MAX(id) AS latest_id
  FROM transaction_history
  WHERE user_id=@userId
  GROUP BY source_type, source_id
)", new { userId });

        var rows = (await conn.QueryAsync<dynamic>(@"
SELECT t.id, t.source_type, t.source_id, t.title, t.amount, t.status, t.created_at_utc, t.metadata_json
FROM transaction_history t
INNER JOIN (
  SELECT MAX(id) AS latest_id
  FROM transaction_history
  WHERE user_id=@userId
  GROUP BY source_type, source_id
) latest ON t.id = latest.latest_id
ORDER BY datetime(t.created_at_utc) DESC
LIMIT @limit OFFSET @offset", new { userId, limit = pageSize, offset })).ToList();

        return OkEnvelope<object>(new
        {
            page,
            pageSize,
            total,
            items = rows
        });
    }
}
