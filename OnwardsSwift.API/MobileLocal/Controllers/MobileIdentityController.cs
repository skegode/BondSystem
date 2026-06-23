using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;
using System.Text.Json;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[Route("api/iprs")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MobileIdentityController : MobileApiControllerBase
{
    private readonly LocalSqliteContext _db;

    public MobileIdentityController(LocalSqliteContext db)
    {
        _db = db;
    }

    [HttpPost("verify")]
    public async Task<ActionResult<ApiEnvelope<object>>> VerifyIprs([FromBody] IprsVerifyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdNumber) || string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
        {
            return FailEnvelope<object>(400, "idNumber, firstName, and lastName are required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();
        var normalized = request.IdNumber.Trim();

        var mockSuccess = normalized.Length >= 6;
        var payload = new
        {
            source = "mock-iprs",
            verified = mockSuccess,
            idNumber = normalized,
            firstName = request.FirstName.Trim(),
            middleName = request.MiddleName,
            lastName = request.LastName.Trim(),
            dateOfBirth = request.DateOfBirth,
            checkedAtUtc = DateTime.UtcNow
        };

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO iprs_verification_logs(user_id, id_number, first_name, middle_name, last_name, date_of_birth, success, upstream_payload_json, created_at_utc)
VALUES(@userId, @idNumber, @firstName, @middleName, @lastName, @dob, @success, @payload, @createdAt);
SELECT last_insert_rowid();", new
        {
            userId,
            idNumber = normalized,
            firstName = request.FirstName.Trim(),
            middleName = request.MiddleName,
            lastName = request.LastName.Trim(),
            dob = request.DateOfBirth,
            success = mockSuccess ? 1 : 0,
            payload = JsonSerializer.Serialize(payload),
            createdAt = DateTime.UtcNow.ToString("O")
        });

        return OkEnvelope<object>(new
        {
            logId = id,
            verified = mockSuccess,
            idNumber = normalized,
            fullName = $"{request.FirstName} {request.LastName}".Trim(),
            source = "mock-iprs"
        }, mockSuccess ? "IPRS verified" : "IPRS verification failed");
    }

    [HttpPost("kra-lookup")]
    public async Task<ActionResult<ApiEnvelope<object>>> LookupKra([FromBody] KraLookupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KraPin))
        {
            return FailEnvelope<object>(400, "kraPin is required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();
        var pin = request.KraPin.Trim().ToUpperInvariant();

        var mockSuccess = pin.Length >= 8;
        var payload = new
        {
            source = "mock-kra",
            found = mockSuccess,
            kraPin = pin,
            taxpayerName = request.TaxpayerName,
            checkedAtUtc = DateTime.UtcNow
        };

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var id = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO kra_verification_logs(user_id, kra_pin, taxpayer_name, success, upstream_payload_json, created_at_utc)
VALUES(@userId, @kraPin, @taxpayerName, @success, @payload, @createdAt);
SELECT last_insert_rowid();", new
        {
            userId,
            kraPin = pin,
            taxpayerName = request.TaxpayerName,
            success = mockSuccess ? 1 : 0,
            payload = JsonSerializer.Serialize(payload),
            createdAt = DateTime.UtcNow.ToString("O")
        });

        return OkEnvelope<object>(new
        {
            logId = id,
            kraPin = pin,
            taxpayerName = request.TaxpayerName,
            found = mockSuccess,
            source = "mock-kra"
        }, mockSuccess ? "KRA lookup success" : "KRA lookup failed");
    }
}
