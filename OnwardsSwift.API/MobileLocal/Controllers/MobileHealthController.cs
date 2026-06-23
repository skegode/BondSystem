using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.API.MobileLocal.Contracts;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[ApiController]
[Route("api")]
public class MobileHealthController : ControllerBase
{
    [HttpGet("ping")]
    public ActionResult<ApiEnvelope<object>> Ping()
    {
        return Ok(ApiEnvelope<object>.Ok(new
        {
            service = "OnwardsSwift.MobileLocalApi",
            status = "ok",
            utc = DateTime.UtcNow
        }, "pong"));
    }

    [HttpGet("/health")]
    public ActionResult<ApiEnvelope<object>> Health()
    {
        return Ok(ApiEnvelope<object>.Ok(new
        {
            service = "OnwardsSwift.MobileLocalApi",
            status = "healthy",
            utc = DateTime.UtcNow
        }, "healthy"));
    }
}
