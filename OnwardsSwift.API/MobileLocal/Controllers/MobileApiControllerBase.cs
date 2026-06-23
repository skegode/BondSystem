using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.API.MobileLocal.Contracts;
using System.Security.Claims;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[ApiController]
public abstract class MobileApiControllerBase : ControllerBase
{
    protected long GetUserIdOrThrow()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!long.TryParse(sub, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid token subject.");
        }

        return userId;
    }

    protected string? GetJti() => User.FindFirstValue("jti");

    protected string ToPublicUrl(string relativePath)
    {
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value!.TrimEnd('/') : "";
        return $"{pathBase}/uploads/{clean}";
    }

    protected ActionResult<ApiEnvelope<T>> OkEnvelope<T>(T data, string message = "ok")
    {
        return Ok(ApiEnvelope<T>.Ok(data, message));
    }

    protected ActionResult<ApiEnvelope<T>> FailEnvelope<T>(int statusCode, string message, string errorCode)
    {
        return StatusCode(statusCode, ApiEnvelope<T>.Fail(message, errorCode));
    }
}
