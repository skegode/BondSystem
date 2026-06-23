using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using OnwardsSwift.API.SignupWizard;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;

namespace OnwardsSwift.API.Controllers;

[ApiController]
[Route("api/signup-wizard")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class SignupWizardController : ControllerBase
{
    private readonly DapperContext _dapper;
    private readonly IIprsService _iprsService;
    private readonly ILogger<SignupWizardController> _logger;

    public SignupWizardController(
        DapperContext dapper,
        IIprsService iprsService,
        ILogger<SignupWizardController> logger)
    {
        _dapper = dapper;
        _iprsService = iprsService;
        _logger = logger;
    }

    [HttpPost("step1-verify")]
    public async Task<IActionResult> Step1Verify([FromBody] Step1VerifyRequest request)
    {
        var correlationId = GetCorrelationId();
        var fullName = request.FullName?.Trim();
        var nationalId = NormalizeNationalId(request.NationalId);

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(nationalId))
        {
            return Error(400, "validation_error", "fullName and nationalId are required.", correlationId);
        }

        var iprsResult = await _iprsService.VerifyIdentityAsync(nationalId, fullName);
        if (!iprsResult.Success || !iprsResult.IdentityVerified)
        {
            _logger.LogWarning("IPRS failed for signup wizard. CorrelationId={CorrelationId}, NationalId={NationalId}, Message={Message}",
                correlationId, nationalId, iprsResult.Message);
            return Error(502, "iprs_failed", iprsResult.Message ?? "IPRS verification failed.", correlationId);
        }

        var resolvedFullName = string.IsNullOrWhiteSpace(iprsResult.FullName) ? fullName : iprsResult.FullName.Trim();
        var iprsKraPin = string.IsNullOrWhiteSpace(iprsResult.KraPin) ? null : iprsResult.KraPin.Trim().ToUpperInvariant();
        var iprsPhone = NormalizePhone(iprsResult.Phone);
        var iprsGender = string.IsNullOrWhiteSpace(iprsResult.Gender) ? null : iprsResult.Gender.Trim();

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await SignupWizardSchema.EnsureAsync(conn);

        // SERIALIZABLE: the "find my in-progress session for this nationalId" SELECT and the
        // INSERT it falls back to (when none exists) must be atomic. Under READ COMMITTED, two
        // concurrent Step1Verify calls for the same nationalId can both miss the SELECT and both
        // generate a fresh wizard_session_id, leaving two in-progress profile rows for one person.
        // The filtered unique index on national_id (account_created = 0) is the schema backstop.
        using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

        var wizardSessionId = await conn.ExecuteScalarAsync<Guid?>(@"
SELECT TOP 1 wizard_session_id
FROM dbo.signup_wizard_profiles
WHERE national_id = @nationalId
  AND account_created = 0
ORDER BY created_at_utc DESC;", new { nationalId }, tx) ?? Guid.NewGuid();

        var payload = string.IsNullOrWhiteSpace(iprsResult.RawResponse)
            ? JsonSerializer.Serialize(new
            {
                iprsResult.IdNumber,
                iprsResult.FullName,
                iprsResult.KraPin,
                iprsResult.Phone,
                iprsResult.Gender,
                iprsResult.DateOfBirth,
                iprsResult.Message,
                iprsResult.Reference,
                identity_verified = iprsResult.IdentityVerified
            })
            : iprsResult.RawResponse;

        await conn.ExecuteAsync(@"
IF EXISTS (SELECT 1 FROM dbo.signup_wizard_profiles WHERE wizard_session_id = @wizardSessionId)
BEGIN
    UPDATE dbo.signup_wizard_profiles
    SET full_name = COALESCE(NULLIF(@resolvedFullName, ''), full_name),
        national_id = @nationalId,
        kra_pin = COALESCE(NULLIF(@iprsKraPin, ''), kra_pin),
        phone = CASE WHEN @iprsPhone = '' THEN phone ELSE COALESCE(@iprsPhone, phone) END,
        gender = COALESCE(NULLIF(@iprsGender, ''), gender),
        identity_verified = 1,
        verified_at_utc = SYSUTCDATETIME(),
        iprs_reference = @iprsReference,
        iprs_payload_json = @payload,
        current_step = CASE WHEN current_step < 1 THEN 1 ELSE current_step END,
        wizard_status = 'step1_verified',
        correlation_id = @correlationId,
        updated_at_utc = SYSUTCDATETIME()
    WHERE wizard_session_id = @wizardSessionId;
END
ELSE
BEGIN
    INSERT INTO dbo.signup_wizard_profiles
    (
        wizard_session_id,
        full_name,
        national_id,
        kra_pin,
        phone,
        gender,
        identity_verified,
        verified_at_utc,
        iprs_reference,
        iprs_payload_json,
        current_step,
        wizard_status,
        correlation_id,
        created_at_utc,
        updated_at_utc
    )
    VALUES
    (
        @wizardSessionId,
        @resolvedFullName,
        @nationalId,
        @iprsKraPin,
        CASE WHEN @iprsPhone = '' THEN NULL ELSE @iprsPhone END,
        @iprsGender,
        1,
        SYSUTCDATETIME(),
        @iprsReference,
        @payload,
        1,
        'step1_verified',
        @correlationId,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );
END", new
        {
            wizardSessionId,
            resolvedFullName,
            nationalId,
            iprsKraPin,
            iprsPhone,
            iprsGender,
            iprsReference = iprsResult.Reference,
            payload,
            correlationId
        }, tx);

        tx.Commit();

        var profile = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 full_name, national_id, kra_pin, phone, gender
FROM dbo.signup_wizard_profiles
WHERE wizard_session_id = @wizardSessionId;", new { wizardSessionId });

        return Ok(new
        {
            success = true,
            wizard_session_id = wizardSessionId,
            correlation_id = correlationId,
            fullName = Convert.ToString(profile?.full_name),
            nationalId = Convert.ToString(profile?.national_id),
            kraPin = Convert.ToString(profile?.kra_pin),
            phone = Convert.ToString(profile?.phone),
            gender = Convert.ToString(profile?.gender)
        });
    }

    [HttpPost("step2")]
    public async Task<IActionResult> Step2([FromBody] JsonElement payload)
    {
        var correlationId = GetCorrelationId();
        if (!TryReadWizardSessionId(payload, out var wizardSessionId))
        {
            return Error(400, "validation_error", "wizard_session_id is required.", correlationId);
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await SignupWizardSchema.EnsureAsync(conn);

        var fullName = ReadString(payload, "fullName", "full_name");
        var email = NormalizeEmail(ReadString(payload, "email"));
        var phone = NormalizePhone(ReadString(payload, "phone"));
        var nationalId = NormalizeNationalId(ReadString(payload, "nationalId", "national_id"));
        var kraPin = ReadString(payload, "kraPin", "kra_pin");
        var gender = ReadString(payload, "gender");
        var postalAddress = ReadString(payload, "postalAddress", "postal_address");
        var physicalAddress = ReadString(payload, "physicalAddress", "physical_address");

        var affected = await conn.ExecuteAsync(@"
UPDATE dbo.signup_wizard_profiles
SET full_name = COALESCE(@fullName, full_name),
    email = CASE WHEN @email = '' THEN NULL ELSE COALESCE(@email, email) END,
    phone = CASE WHEN @phone = '' THEN NULL ELSE COALESCE(@phone, phone) END,
    national_id = COALESCE(NULLIF(@nationalId, ''), national_id),
    kra_pin = COALESCE(@kraPin, kra_pin),
    gender = COALESCE(@gender, gender),
    postal_address = COALESCE(@postalAddress, postal_address),
    physical_address = COALESCE(@physicalAddress, physical_address),
    step2_payload_json = @payloadJson,
    current_step = CASE WHEN current_step < 2 THEN 2 ELSE current_step END,
    wizard_status = 'step2_saved',
    correlation_id = @correlationId,
    updated_at_utc = SYSUTCDATETIME()
WHERE wizard_session_id = @wizardSessionId;", new
        {
            wizardSessionId,
            fullName,
            email,
            phone,
            nationalId,
            kraPin,
            gender,
            postalAddress,
            physicalAddress,
            payloadJson = payload.GetRawText(),
            correlationId
        });

        if (affected == 0)
        {
            return Error(400, "validation_error", "wizard_session_id not found.", correlationId);
        }

        return Ok(new
        {
            success = true,
            wizard_session_id = wizardSessionId,
            current_step = 2,
            correlation_id = correlationId
        });
    }

    [HttpPost("step3")]
    public async Task<IActionResult> Step3([FromBody] JsonElement payload)
    {
        var correlationId = GetCorrelationId();
        if (!TryReadWizardSessionId(payload, out var wizardSessionId))
        {
            return Error(400, "validation_error", "wizard_session_id is required.", correlationId);
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await SignupWizardSchema.EnsureAsync(conn);

        var workDetailsJson = TryReadJson(payload, "workDetails", "work_details") ?? payload.GetRawText();
        var signatureMetadataJson = TryReadJson(payload, "signature", "signatureMetadata", "signature_metadata");
        var photoMetadataJson = TryReadJson(payload, "photo", "photoMetadata", "photo_metadata");

        var affected = await conn.ExecuteAsync(@"
UPDATE dbo.signup_wizard_profiles
SET work_details_json = @workDetailsJson,
    signature_metadata_json = @signatureMetadataJson,
    photo_metadata_json = @photoMetadataJson,
    current_step = CASE WHEN current_step < 3 THEN 3 ELSE current_step END,
    wizard_status = 'step3_saved',
    correlation_id = @correlationId,
    updated_at_utc = SYSUTCDATETIME()
WHERE wizard_session_id = @wizardSessionId;", new
        {
            wizardSessionId,
            workDetailsJson,
            signatureMetadataJson,
            photoMetadataJson,
            correlationId
        });

        if (affected == 0)
        {
            return Error(400, "validation_error", "wizard_session_id not found.", correlationId);
        }

        return Ok(new
        {
            success = true,
            wizard_session_id = wizardSessionId,
            current_step = 3,
            correlation_id = correlationId
        });
    }

    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] WizardSessionRequest request)
    {
        var correlationId = GetCorrelationId();
        if (!Guid.TryParse(request.WizardSessionId, out var wizardSessionId))
        {
            return Error(400, "validation_error", "wizard_session_id is required.", correlationId);
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await SignupWizardSchema.EnsureAsync(conn);

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var requiredSystemUserColumns = new[]
            {
                "FullName",
                "Email",
                "Phone",
                "PasswordHash",
                "PinHash",
                "NationalId",
                "PinUpdatedAt",
                "IprsVerified",
                "IprsReference",
                "KraPin",
                "Gender",
                "PostalAddress",
                "PhysicalAddress",
                "Role",
                "IsActive",
                "CreatedAt",
                "UpdatedAt",
                "IsDeleted"
            };

            var missingColumns = await GetMissingSystemUserColumnsAsync(conn, tx, requiredSystemUserColumns);
            if (missingColumns.Count > 0)
            {
                await tx.RollbackAsync();
                return Error(500, "validation_error", $"SystemUsers schema missing columns: {string.Join(", ", missingColumns)}", correlationId);
            }

            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT wizard_session_id, full_name, national_id, email, phone, kra_pin, gender, postal_address, physical_address,
       identity_verified, account_created, wizard_status
FROM dbo.signup_wizard_profiles WITH (UPDLOCK, ROWLOCK)
WHERE wizard_session_id = @wizardSessionId;", new { wizardSessionId }, tx);

            if (row == null)
            {
                await tx.RollbackAsync();
                return Error(400, "validation_error", "wizard_session_id not found.", correlationId);
            }

            var fullName = Convert.ToString(row.full_name)?.Trim();
            var nationalId = NormalizeNationalId(Convert.ToString(row.national_id));
            var email = NormalizeEmail(Convert.ToString(row.email));
            var phone = NormalizePhone(Convert.ToString(row.phone));
            var iprsVerified = Convert.ToBoolean(row.identity_verified);
            var accountCreated = Convert.ToBoolean(row.account_created);

            if (accountCreated)
            {
                await tx.RollbackAsync();
                return Error(409, "conflict", "Account already exists for this wizard session.", correlationId);
            }

            if (!iprsVerified)
            {
                await tx.RollbackAsync();
                return Error(400, "validation_error", "Identity must be verified before submit.", correlationId);
            }

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(nationalId))
            {
                await tx.RollbackAsync();
                return Error(400, "validation_error", "Missing required profile fields (full_name, national_id).", correlationId);
            }

            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            {
                await tx.RollbackAsync();
                return Error(400, "validation_error", "At least one contact channel (phone or email) is required.", correlationId);
            }

            var phoneTokens = BuildPhoneTokens(phone);
            if (phoneTokens.Length > 0)
            {
                var phoneExists = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') IN @phoneTokens;",
                    new { phoneTokens }, tx);
                if (phoneExists > 0)
                {
                    await tx.RollbackAsync();
                    return Error(409, "conflict", "An account with this phone already exists.", correlationId);
                }
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var emailExists = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.SystemUsers
WHERE IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email;",
                    new { email }, tx);
                if (emailExists > 0)
                {
                    await tx.RollbackAsync();
                    return Error(409, "conflict", "An account with this email already exists.", correlationId);
                }
            }

            var nationalIdExists = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1)
FROM dbo.SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(UPPER(ISNULL(NationalId, '')), ' ', ''), '-', '') = @nationalId;",
                new { nationalId }, tx);
            if (nationalIdExists > 0)
            {
                await tx.RollbackAsync();
                return Error(409, "conflict", "An account with this nationalId already exists.", correlationId);
            }

            var username = !string.IsNullOrWhiteSpace(email) ? email : phone;
            var generatedPin = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var pinHash = BCrypt.Net.BCrypt.HashPassword(generatedPin);

            var insertedSystemUserId = await conn.ExecuteScalarAsync<object>(@"
INSERT INTO dbo.SystemUsers
(
    FullName,
    Email,
    Phone,
    PasswordHash,
    PinHash,
    NationalId,
    PinUpdatedAt,
    IprsVerified,
    IprsReference,
    KraPin,
    Gender,
    PostalAddress,
    PhysicalAddress,
    Role,
    IsActive,
    CreatedAt,
    UpdatedAt,
    IsDeleted
)
OUTPUT INSERTED.Id
VALUES
(
    @fullName,
    @email,
    @phone,
    '',
    @pinHash,
    @nationalId,
    SYSUTCDATETIME(),
    1,
    NULL,
    @kraPin,
    @gender,
    @postalAddress,
    @physicalAddress,
    4,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    0
);", new
            {
                fullName,
                email = string.IsNullOrWhiteSpace(email) ? $"wizard_{Guid.NewGuid():N}@onwardsswift.local" : email,
                phone,
                pinHash,
                nationalId,
                kraPin = Convert.ToString(row.kra_pin),
                gender = Convert.ToString(row.gender),
                postalAddress = Convert.ToString(row.postal_address),
                physicalAddress = Convert.ToString(row.physical_address)
            }, tx);

            var resolvedSystemUserId = ResolveSystemUserId(insertedSystemUserId);

            var updateCount = await conn.ExecuteAsync(@"
UPDATE dbo.signup_wizard_profiles
SET generated_username = @username,
    generated_pin_hash = @pinHash,
    account_created = 1,
    account_created_at_utc = SYSUTCDATETIME(),
    current_step = 4,
    wizard_status = 'completed',
    system_user_id = @systemUserId,
    system_user_id_int = @systemUserIdInt,
    correlation_id = @correlationId,
    updated_at_utc = SYSUTCDATETIME()
WHERE wizard_session_id = @wizardSessionId;", new
            {
                wizardSessionId,
                username,
                pinHash,
                systemUserId = resolvedSystemUserId.guidValue,
                systemUserIdInt = resolvedSystemUserId.intValue,
                correlationId
            }, tx);

            if (updateCount == 0)
            {
                await tx.RollbackAsync();
                return Error(500, "dispatch_failed", "Failed to finalize wizard profile.", correlationId);
            }

            var smsBody = $"Welcome to OnwardsSwift. Username: {username}. PIN: {generatedPin}";
            var emailSubject = "Your OnwardsSwift credentials";
            var emailBody = $"Welcome to OnwardsSwift.<br/>Username: <b>{username}</b><br/>PIN: <b>{generatedPin}</b>";

            if (!string.IsNullOrWhiteSpace(phone))
            {
                await conn.ExecuteAsync(@"
INSERT INTO dbo.notification_outbox
(
    wizard_session_id,
    channel,
    recipient,
    subject,
    message_body,
    status,
    attempts,
    max_attempts,
    created_at_utc,
    next_attempt_at_utc,
    updated_at_utc
)
VALUES
(
    @wizardSessionId,
    'sms',
    @phone,
    NULL,
    @smsBody,
    'pending',
    0,
    5,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);", new { wizardSessionId, phone, smsBody }, tx);
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                await conn.ExecuteAsync(@"
INSERT INTO dbo.notification_outbox
(
    wizard_session_id,
    channel,
    recipient,
    subject,
    message_body,
    status,
    attempts,
    max_attempts,
    created_at_utc,
    next_attempt_at_utc,
    updated_at_utc
)
VALUES
(
    @wizardSessionId,
    'email',
    @email,
    @emailSubject,
    @emailBody,
    'pending',
    0,
    5,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);", new { wizardSessionId, email, emailSubject, emailBody }, tx);
            }

            await tx.CommitAsync();

            return StatusCode(201, new
            {
                success = true,
                status = "created",
                wizard_session_id = wizardSessionId,
                correlation_id = correlationId
            });
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            await tx.RollbackAsync();
            _logger.LogWarning(ex, "Signup wizard submit conflict. CorrelationId={CorrelationId}, WizardSessionId={WizardSessionId}", correlationId, wizardSessionId);
            return Error(409, "conflict", "Account already exists.", correlationId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Signup wizard submit failed. CorrelationId={CorrelationId}, WizardSessionId={WizardSessionId}", correlationId, wizardSessionId);
            return Error(500, "dispatch_failed", "Signup submit failed.", correlationId);
        }
    }

    private string GetCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-ID", out var correlation) && !string.IsNullOrWhiteSpace(correlation))
        {
            return correlation.ToString();
        }

        return HttpContext.TraceIdentifier;
    }

    private IActionResult Error(int statusCode, string code, string message, string correlationId)
    {
        return StatusCode(statusCode, new
        {
            success = false,
            error = new
            {
                code,
                message
            },
            correlation_id = correlationId
        });
    }

    private static bool TryReadWizardSessionId(JsonElement payload, out Guid wizardSessionId)
    {
        wizardSessionId = Guid.Empty;
        var raw = ReadString(payload, "wizard_session_id", "wizardSessionId");
        return Guid.TryParse(raw, out wizardSessionId);
    }

    private static string? ReadString(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetPropertyCaseInsensitive(payload, key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? TryReadJson(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetPropertyCaseInsensitive(payload, key, out var value))
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement payload, string key, out JsonElement value)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeNationalId(string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(nationalId))
        {
            return string.Empty;
        }

        return new string(nationalId
            .Trim()
            .Where(ch => !char.IsWhiteSpace(ch) && ch != '-')
            .ToArray())
            .ToUpperInvariant();
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("254", StringComparison.Ordinal) && digits.Length == 12)
        {
            return $"+{digits}";
        }

        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length == 10)
        {
            return $"+254{digits[1..]}";
        }

        if (digits.Length == 9 && digits.StartsWith("7", StringComparison.Ordinal))
        {
            return $"+254{digits}";
        }

        if (trimmed.StartsWith("+", StringComparison.Ordinal) && digits.StartsWith("254", StringComparison.Ordinal) && digits.Length == 12)
        {
            return $"+{digits}";
        }

        return string.Empty;
    }

    private static string NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? string.Empty
            : email.Trim().ToLowerInvariant();
    }

    private static string[] BuildPhoneTokens(string? phoneInput)
    {
        var normalized = NormalizePhone(phoneInput);
        var compactInput = new string((phoneInput ?? string.Empty).Where(char.IsDigit).ToArray());
        var compactNormalized = new string((normalized ?? string.Empty).Where(char.IsDigit).ToArray());

        var values = new HashSet<string>(StringComparer.Ordinal)
        {
            compactInput,
            compactNormalized
        };

        if (compactNormalized.StartsWith("254", StringComparison.Ordinal) && compactNormalized.Length == 12)
        {
            values.Add("0" + compactNormalized[3..]);
        }

        if (compactInput.StartsWith("0", StringComparison.Ordinal) && compactInput.Length == 10)
        {
            values.Add("254" + compactInput[1..]);
        }

        if (compactInput.Length == 9 && compactInput.StartsWith("7", StringComparison.Ordinal))
        {
            values.Add("254" + compactInput);
            values.Add("0" + compactInput);
        }

        return values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
    }

    private static (Guid? guidValue, int? intValue) ResolveSystemUserId(object? rawId)
    {
        if (rawId is Guid guid)
        {
            return (guid, null);
        }

        if (rawId is int idInt)
        {
            return (null, idInt);
        }

        if (rawId is long idLong && idLong >= int.MinValue && idLong <= int.MaxValue)
        {
            return (null, (int)idLong);
        }

        var text = Convert.ToString(rawId)?.Trim();
        if (Guid.TryParse(text, out var parsedGuid))
        {
            return (parsedGuid, null);
        }

        if (int.TryParse(text, out var parsedInt))
        {
            return (null, parsedInt);
        }

        return (null, null);
    }

    private static async Task<List<string>> GetMissingSystemUserColumnsAsync(
        SqlConnection conn,
        System.Data.Common.DbTransaction tx,
        IEnumerable<string> requiredColumns)
    {
        var existingColumns = (await conn.QueryAsync<string>(@"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.type = 'U'
  AND o.name = 'SystemUsers'
  AND SCHEMA_NAME(o.schema_id) = 'dbo';", transaction: tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requiredColumns
            .Where(c => !existingColumns.Contains(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
