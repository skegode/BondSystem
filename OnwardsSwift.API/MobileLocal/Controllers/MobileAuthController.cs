using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using OnwardsSwift.API.MobileLocal.Configuration;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;
using Microsoft.Extensions.Options;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Globalization;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[Route("api/auth")]
public class MobileAuthController : MobileApiControllerBase
{
    private readonly OtpService _otpService;
    private readonly TokenService _tokenService;
    private readonly DapperContext _dapper;
    private readonly LocalApiOptions _options;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MobileAuthController> _logger;
    private readonly OnwardsSwift.Infrastructure.Services.PermissionService _permissions;

    public MobileAuthController(
        OtpService otpService,
        TokenService tokenService,
        DapperContext dapper,
        INotificationService notificationService,
        ILogger<MobileAuthController> logger,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IOptions<LocalApiOptions> options,
        OnwardsSwift.Infrastructure.Services.PermissionService permissions)
    {
        _otpService = otpService;
        _tokenService = tokenService;
        _dapper = dapper;
        _notificationService = notificationService;
        _logger = logger;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _options = options.Value;
        _permissions = permissions;
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> Signup([FromBody] SignupRequest request)
    {
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var phone = NormalizePhone(request.Phone);
        var nationalId = NormalizeNationalId(request.NationalId);
        var email = NormalizeEmail(request.Email);
        var iprsReference = string.IsNullOrWhiteSpace(request.IprsReference) ? null : request.IprsReference.Trim();
        var kraPin = string.IsNullOrWhiteSpace(request.KraPin) ? null : request.KraPin.Trim().ToUpperInvariant();
        var gender = string.IsNullOrWhiteSpace(request.Gender) ? null : request.Gender.Trim();
        var postalAddress = string.IsNullOrWhiteSpace(request.PostalAddress) ? null : request.PostalAddress.Trim();
        var physicalAddress = string.IsNullOrWhiteSpace(request.PhysicalAddress) ? null : request.PhysicalAddress.Trim();
        var phoneTokens = BuildPhoneTokens(request.Phone);
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(nationalId) || string.IsNullOrWhiteSpace(email))
        {
            return FailEnvelope<object>(400, "fullName, phone, email, and nationalId are required.", "validation_error");
        }

        if (!IsValidPin(request.Pin))
        {
            return FailEnvelope<object>(400, "pin must be 4 to 8 digits.", "validation_error");
        }

        if (!request.IprsVerified)
        {
            return FailEnvelope<object>(400, "iprsVerified must be true.", "validation_error");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        var credentialPin = request.Pin.Trim();
        var pinHash = BCrypt.Net.BCrypt.HashPassword(credentialPin);
        (object dbValue, Guid? guidValue, int? intValue) resolvedSystemUserId;
        long localUserId;
        SystemUserProfileSnapshot profile;

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var signupSchemaError = await EnsureSystemUserColumnsAsync(conn, tx,
                "FullName",
                "Email",
                "Phone",
                "PinHash",
                "NationalId",
                "PinUpdatedAt",
                "IprsVerified",
                "IprsReference",
                "KraPin",
                "Gender",
                "PostalAddress",
                "PhysicalAddress",
                "UpdatedAt");
            if (signupSchemaError != null)
            {
                await tx.RollbackAsync();
                return signupSchemaError;
            }

            var matchedSystemUserIds = new List<(string key, (object dbValue, Guid? guidValue, int? intValue) id)>();

            void AddMatchedSystemUser(object? rawId)
            {
                if (rawId == null)
                {
                    return;
                }

                var resolved = ResolveSystemUserId(rawId);
                var key = resolved.guidValue?.ToString() ?? resolved.intValue?.ToString() ?? Convert.ToString(resolved.dbValue);
                if (string.IsNullOrWhiteSpace(key))
                {
                    return;
                }

                if (matchedSystemUserIds.All(x => !string.Equals(x.key, key, StringComparison.OrdinalIgnoreCase)))
                {
                    matchedSystemUserIds.Add((key, resolved));
                }
            }

            var existingByPhone = await conn.ExecuteScalarAsync<object?>(@"
SELECT TOP 1 Id
FROM SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') IN @phoneTokens
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { phoneTokens }, tx);
            AddMatchedSystemUser(existingByPhone);

            var existingByNationalId = await conn.ExecuteScalarAsync<object?>(@"
SELECT TOP 1 Id
FROM SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(UPPER(ISNULL(NationalId, '')), ' ', ''), '-', '') = @nationalId
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { nationalId }, tx);
            AddMatchedSystemUser(existingByNationalId);

            var existingByEmail = await conn.ExecuteScalarAsync<object?>(@"
SELECT TOP 1 Id
FROM SystemUsers
WHERE IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { email }, tx);
            AddMatchedSystemUser(existingByEmail);

            if (matchedSystemUserIds.Count > 1)
            {
                await tx.RollbackAsync();
                return FailEnvelope<object>(409, "Signup identifiers match different accounts.", "conflicting_identifiers");
            }

            if (matchedSystemUserIds.Count == 1)
            {
                resolvedSystemUserId = matchedSystemUserIds[0].id;

                await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET
    FullName = COALESCE(NULLIF(LTRIM(RTRIM(FullName)), ''), @fullName),
    NationalId = COALESCE(NULLIF(LTRIM(RTRIM(NationalId)), ''), @nationalId),
    Phone = COALESCE(NULLIF(LTRIM(RTRIM(Phone)), ''), @phone),
    Email = COALESCE(NULLIF(LTRIM(RTRIM(Email)), ''), @email),
    KraPin = COALESCE(NULLIF(LTRIM(RTRIM(KraPin)), ''), @kraPin),
    Gender = COALESCE(NULLIF(LTRIM(RTRIM(Gender)), ''), @gender),
    PostalAddress = COALESCE(NULLIF(LTRIM(RTRIM(PostalAddress)), ''), @postalAddress),
    PhysicalAddress = COALESCE(NULLIF(LTRIM(RTRIM(PhysicalAddress)), ''), @physicalAddress),
    IprsVerified = CASE WHEN ISNULL(IprsVerified, 0) = 1 THEN 1 ELSE @iprsVerified END,
    IprsReference = COALESCE(NULLIF(LTRIM(RTRIM(IprsReference)), ''), @iprsReference),
    PinHash = COALESCE(NULLIF(LTRIM(RTRIM(PinHash)), ''), @pinHash),
    PinUpdatedAt = CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(PinHash, ''))), '') IS NULL THEN SYSUTCDATETIME() ELSE PinUpdatedAt END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @id;", new
                {
                    fullName,
                    nationalId,
                    phone,
                    email,
                    kraPin,
                    gender,
                    postalAddress,
                    physicalAddress,
                    iprsVerified = request.IprsVerified,
                    iprsReference,
                    pinHash,
                    id = resolvedSystemUserId.dbValue
                }, tx);
            }
            else
            {
                var insertedSystemUserId = await conn.ExecuteScalarAsync<object>(@"
INSERT INTO SystemUsers
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
    @iprsVerified,
    @iprsReference,
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
                    email,
                    phone,
                    pinHash,
                    nationalId,
                    iprsVerified = request.IprsVerified,
                    iprsReference,
                    kraPin,
                    gender,
                    postalAddress,
                    physicalAddress
                }, tx);

                resolvedSystemUserId = ResolveSystemUserId(insertedSystemUserId);
            }

            profile = await conn.QueryFirstAsync<SystemUserProfileSnapshot>(@"
SELECT TOP 1
    FullName,
    NationalId,
    Phone,
    Email,
    KraPin,
    Gender,
    PostalAddress,
    PhysicalAddress,
    IprsReference,
    IprsVerified
FROM SystemUsers
WHERE Id = @id;", new { id = resolvedSystemUserId.dbValue }, tx);

            localUserId = await EnsureLocalUserAsync(conn, tx, profile.Phone ?? phone, profile.FullName ?? fullName, profile.Email ?? email);

            await tx.CommitAsync();
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            await tx.RollbackAsync();
            return FailEnvelope<object>(409, "An account with this identity already exists.", "conflict");
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        var channelsAttempted = new[] { "email", "sms" };
        var notificationAttempts = new List<NotificationAttemptResult>();
        var providerConfig = GetNotificationProviderConfigStatus();

        if (!providerConfig.smtpConfigured)
        {
            _logger.LogWarning("Signup notification provider config incomplete for SMTP. Missing: {MissingKeys}", string.Join(",", providerConfig.smtpMissing));
        }

        if (!providerConfig.smsConfigured)
        {
            _logger.LogWarning("Signup notification provider config incomplete for SMS. Missing: {MissingKeys}", string.Join(",", providerConfig.smsMissing));
        }

        var resolvedSystemUserIdText = resolvedSystemUserId.guidValue?.ToString() ?? resolvedSystemUserId.intValue?.ToString();
        var accountCreatedAtUtc = DateTime.UtcNow;

        // Email attempt
        if (!string.IsNullOrWhiteSpace(profile.Email) && providerConfig.smtpConfigured)
        {
            try
            {
                var subject = "Welcome to Onwards Swift";
                var htmlBody = $"<p>Account created.</p><p>Username: {phone}</p><p>Temporary PIN: {credentialPin}</p><p>Please change your PIN after first login.</p>";
                var plainBody = $"Account created. Username: {phone}. Temporary PIN: {credentialPin}. Please change your PIN after first login.";
                var sendResult = await WithSignupNotificationTimeoutAsync(
                    _notificationService.SendEmailAsync(profile.Email, subject, htmlBody, plainBody));

                var result = new NotificationAttemptResult
                {
                    Channel = "email",
                    Destination = profile.Email,
                    Sent = sendResult.Accepted,
                    ProviderMessageId = sendResult.ProviderMessageId,
                    FailureReason = sendResult.Accepted ? null : (sendResult.FailureReason ?? "email_send_failed")
                };
                notificationAttempts.Add(result);

                if (sendResult.Accepted)
                {
                    _logger.LogInformation("Signup notification attempt succeeded. userId={UserId}, channel={Channel}, destination={Destination}, providerMessageId={ProviderMessageId}",
                        resolvedSystemUserIdText,
                        result.Channel,
                        result.Destination,
                        result.ProviderMessageId ?? "n/a");
                }
                else
                {
                    _logger.LogError("Signup notification attempt failed. userId={UserId}, channel={Channel}, destination={Destination}, error={Error}",
                        resolvedSystemUserIdText,
                        result.Channel,
                        result.Destination,
                        result.FailureReason ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                var channelFailureReason = ex.Message;
                notificationAttempts.Add(new NotificationAttemptResult
                {
                    Channel = "email",
                    Destination = profile.Email ?? "(missing)",
                    Sent = false,
                    FailureReason = channelFailureReason
                });

                _logger.LogError(ex, "Signup notification attempt threw exception. userId={UserId}, channel={Channel}, destination={Destination}",
                    resolvedSystemUserIdText,
                    "email",
                    profile.Email);
            }
        }
        else
        {
            notificationAttempts.Add(new NotificationAttemptResult
            {
                Channel = "email",
                Destination = string.IsNullOrWhiteSpace(profile.Email) ? "(missing)" : profile.Email,
                Sent = false,
                FailureReason = string.IsNullOrWhiteSpace(profile.Email) ? "missing_email" : "smtp_not_configured"
            });

            _logger.LogWarning("Signup notification attempt skipped. userId={UserId}, channel={Channel}, destination={Destination}, reason={Reason}",
                resolvedSystemUserIdText,
                "email",
                string.IsNullOrWhiteSpace(profile.Email) ? "(missing)" : profile.Email,
                string.IsNullOrWhiteSpace(profile.Email) ? "missing_email" : "smtp_not_configured");
        }

        // SMS attempt
        if (providerConfig.smsConfigured)
        {
            try
            {
                    await WithSignupNotificationTimeoutAsync(
                        _notificationService.SendSmsAsync(phone, $"Account created. Username: {phone}. Temporary PIN: {credentialPin}. Please change your PIN after first login."));
                var result = new NotificationAttemptResult
                {
                    Channel = "sms",
                    Destination = phone,
                    Sent = true,
                    ProviderMessageId = null,
                    FailureReason = null
                };
                notificationAttempts.Add(result);

                _logger.LogInformation("Signup notification attempt succeeded. userId={UserId}, channel={Channel}, destination={Destination}, providerMessageId={ProviderMessageId}",
                    resolvedSystemUserIdText,
                    result.Channel,
                    result.Destination,
                    "not_provided");
            }
            catch (Exception ex)
            {
                var channelFailureReason = ex.Message;
                notificationAttempts.Add(new NotificationAttemptResult
                {
                    Channel = "sms",
                    Destination = phone,
                    Sent = false,
                    FailureReason = channelFailureReason
                });

                _logger.LogError(ex, "Signup notification attempt threw exception. userId={UserId}, channel={Channel}, destination={Destination}",
                    resolvedSystemUserIdText,
                    "sms",
                    phone);
            }
        }
        else
        {
            notificationAttempts.Add(new NotificationAttemptResult
            {
                Channel = "sms",
                Destination = phone,
                Sent = false,
                FailureReason = "sms_not_configured"
            });

            _logger.LogWarning("Signup notification attempt skipped. userId={UserId}, channel={Channel}, destination={Destination}, reason={Reason}",
                resolvedSystemUserIdText,
                "sms",
                phone,
                "sms_not_configured");
        }

        var notificationSent = notificationAttempts.Any(x => x.Sent);
        var providerMessageId = notificationAttempts
            .Select(x => x.ProviderMessageId)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        string? failureReason = null;
        if (!notificationSent)
        {
            var reasons = notificationAttempts
                .Where(x => !x.Sent)
                .Select(x => $"{x.Channel}:{x.FailureReason}")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            failureReason = reasons.Count == 0 ? "notification_send_failed" : string.Join("; ", reasons);
        }

        var signupMessage = notificationSent
            ? "Signup successful"
            : "Account created, but we could not deliver credentials via email/SMS. Please contact support or use in-app shown temporary PIN.";

        return StatusCode(201, ApiEnvelope<object>.Ok(new
        {
            systemUserId = resolvedSystemUserIdText,
            userId = localUserId,
            fullName = profile.FullName,
            phone = profile.Phone,
            email = profile.Email,
            nationalId = profile.NationalId,
            kraPin = profile.KraPin,
            gender = profile.Gender,
            postalAddress = profile.PostalAddress,
            physicalAddress = profile.PhysicalAddress,
            iprsReference = profile.IprsReference,
            iprsVerified = profile.IprsVerified,
            notificationSent,
            channelsAttempted,
            providerMessageId,
            failureReason,
            notificationAttempts,
            providerConfig = new
            {
                smtpConfigured = providerConfig.smtpConfigured,
                smtpMissing = providerConfig.smtpMissing,
                smsConfigured = providerConfig.smsConfigured,
                smsMissing = providerConfig.smsMissing
            },
            queueHealth = new
            {
                mode = "inline",
                enqueued = false,
                workerRunning = (bool?)null,
                retryPolicy = "provider_default",
                deadLetter = "not_applicable"
            },
            notificationEvaluatedAtUtc = accountCreatedAtUtc
        }, signupMessage));
    }

    // Signup blocks the HTTP response on these sends so the caller gets an accurate
    // notificationSent flag (the mobile app shows a "write down your PIN" warning when
    // false). A slow/unreachable SMTP or SMS provider must not turn that into a multi-minute
    // hang -- bound each attempt so a slow provider degrades to the existing failure path
    // (Sent=false) instead of stalling signup entirely.
    private static readonly TimeSpan SignupNotificationTimeout = TimeSpan.FromSeconds(10);

    private static async Task<TResult> WithSignupNotificationTimeoutAsync<TResult>(Task<TResult> task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(SignupNotificationTimeout));
        if (winner != task)
        {
            throw new TimeoutException($"Notification provider did not respond within {SignupNotificationTimeout.TotalSeconds}s.");
        }

        return await task;
    }

    private static async Task WithSignupNotificationTimeoutAsync(Task task)
    {
        var winner = await Task.WhenAny(task, Task.Delay(SignupNotificationTimeout));
        if (winner != task)
        {
            throw new TimeoutException($"Notification provider did not respond within {SignupNotificationTimeout.TotalSeconds}s.");
        }

        await task;
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<ActionResult<ApiEnvelope<object>>> Me()
    {
        var userId = GetUserIdOrThrow();

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        var schemaError = await EnsureSystemUserColumnsAsync(conn, null,
            "FullName",
            "Email",
            "Phone",
            "NationalId",
            "IprsVerified",
            "IprsReference",
            "KraPin",
            "Gender",
            "PostalAddress",
            "PhysicalAddress",
            "AlternativePhone",
            "PlaceOfWork",
            "WorkTelephone",
            "WorkPhysicalAddress",
            "ProfilePhotoPath",
            "ClientSignature",
            "ClientSignDate");
        if (schemaError != null)
        {
            return schemaError;
        }

        var mobileUser = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 id, phone, email
FROM dbo.MobileUsers
WHERE id = @id;", new { id = userId });

        if (mobileUser == null)
        {
            return FailEnvelope<object>(401, "Session user not found.", "invalid_session");
        }

        var mobilePhone = NormalizePhone(Convert.ToString(mobileUser.phone) ?? string.Empty);
        var mobileEmail = NormalizeEmail(Convert.ToString(mobileUser.email));
        var phoneTokens = BuildPhoneTokens(mobilePhone);

        const string profileColumns = @"
    FullName,
    NationalId,
    Phone,
    Email,
    KraPin,
    Gender,
    PostalAddress,
    PhysicalAddress,
    IprsReference,
    IprsVerified,
    AlternativePhone,
    PlaceOfWork,
    WorkTelephone,
    WorkPhysicalAddress,
    ProfilePhotoPath,
    ClientSignature,
    ClientSignDate";

        SystemUserProfileSnapshot? profile = null;
        if (phoneTokens.Length > 0)
        {
            foreach (var phoneToken in ((IEnumerable<string>)phoneTokens).Distinct(StringComparer.Ordinal))
            {
                profile = await conn.QueryFirstOrDefaultAsync<SystemUserProfileSnapshot>($@"
SELECT TOP 1
{profileColumns}
FROM SystemUsers
WHERE IsDeleted = 0
    AND REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') = @phoneToken
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { phoneToken });

                                if (profile != null)
                                {
                                        break;
                                }
                        }
        }

        if (profile == null && !string.IsNullOrWhiteSpace(mobileEmail))
        {
            profile = await conn.QueryFirstOrDefaultAsync<SystemUserProfileSnapshot>($@"
SELECT TOP 1
{profileColumns}
FROM SystemUsers
WHERE IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { email = mobileEmail });
        }

        if (profile == null)
        {
            return FailEnvelope<object>(404, "Profile not found in SystemUsers.", "profile_not_found");
        }

        return OkEnvelope<object>(new
        {
            fullName = profile.FullName,
            nationalId = profile.NationalId,
            phone = profile.Phone,
            email = profile.Email,
            kraPin = profile.KraPin,
            gender = profile.Gender,
            postalAddress = profile.PostalAddress,
            physicalAddress = profile.PhysicalAddress,
            iprsReference = profile.IprsReference,
            iprsVerified = profile.IprsVerified,
            alternativePhone = profile.AlternativePhone,
            placeOfWork = profile.PlaceOfWork,
            workTelephone = profile.WorkTelephone,
            workPhysicalAddress = profile.WorkPhysicalAddress,
            profilePhotoPath = profile.ProfilePhotoPath,
            profilePhotoUrl = string.IsNullOrWhiteSpace(profile.ProfilePhotoPath) ? null : $"/uploads/{profile.ProfilePhotoPath.TrimStart('/')}",
            clientSignature = profile.ClientSignature,
            clientSignDate = profile.ClientSignDate
        }, "Profile loaded");
    }

    // FullName/NationalId/KraPin/Gender/IprsReference are IPRS-verified identity facts and are
    // not user-editable here. Phone is intentionally excluded -- it's also the sign-in
    // identifier and there is no re-verification step in this flow; changing it needs a
    // dedicated, OTP-gated flow. AmountReceived is excluded entirely -- it's a financial
    // reconciliation figure (see StatementsScreen "Total Received"), not profile data; letting
    // a client edit it would let them misstate their own statement totals.
    [HttpPut("me")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<ActionResult<ApiEnvelope<object>>> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        var userId = GetUserIdOrThrow();

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        var schemaError = await EnsureSystemUserColumnsAsync(conn, null,
            "Email",
            "PostalAddress",
            "PhysicalAddress",
            "AlternativePhone",
            "PlaceOfWork",
            "WorkTelephone",
            "WorkPhysicalAddress",
            "ClientSignature",
            "ClientSignDate",
            "UpdatedAt");
        if (schemaError != null)
        {
            return schemaError;
        }

        var mobileUser = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 id, phone, email
FROM dbo.MobileUsers
WHERE id = @id;", new { id = userId });

        if (mobileUser == null)
        {
            return FailEnvelope<object>(401, "Session user not found.", "invalid_session");
        }

        var mobilePhone = NormalizePhone(Convert.ToString(mobileUser.phone) ?? string.Empty);
        var mobileEmail = NormalizeEmail(Convert.ToString(mobileUser.email));
        var phoneTokens = BuildPhoneTokens(mobilePhone);

        int? systemUserId = null;
        if (phoneTokens.Length > 0)
        {
            foreach (var phoneToken in ((IEnumerable<string>)phoneTokens).Distinct(StringComparer.Ordinal))
            {
                systemUserId = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id FROM SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') = @phoneToken
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { phoneToken });

                if (systemUserId.HasValue)
                {
                    break;
                }
            }
        }

        if (!systemUserId.HasValue && !string.IsNullOrWhiteSpace(mobileEmail))
        {
            systemUserId = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id FROM SystemUsers
WHERE IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { email = mobileEmail });
        }

        if (!systemUserId.HasValue)
        {
            return FailEnvelope<object>(404, "Profile not found in SystemUsers.", "profile_not_found");
        }

        var newEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return FailEnvelope<object>(400, "Email is required.", "validation_error");
        }

        var conflictId = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id FROM SystemUsers
WHERE IsDeleted = 0
  AND Id <> @id
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email;",
            new { id = systemUserId.Value, email = newEmail });

        if (conflictId.HasValue)
        {
            return FailEnvelope<object>(409, "This email address is already in use by another account.", "email_conflict");
        }

        var newSignature = string.IsNullOrWhiteSpace(request.ClientSignature) ? null : request.ClientSignature.Trim();

        await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET Email = @Email,
    PostalAddress = @PostalAddress,
    PhysicalAddress = @PhysicalAddress,
    AlternativePhone = @AlternativePhone,
    PlaceOfWork = @PlaceOfWork,
    WorkTelephone = @WorkTelephone,
    WorkPhysicalAddress = @WorkPhysicalAddress,
    ClientSignature = COALESCE(@ClientSignature, ClientSignature),
    ClientSignDate = CASE WHEN @ClientSignature IS NOT NULL THEN SYSUTCDATETIME() ELSE ClientSignDate END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;", new
        {
            Id = systemUserId.Value,
            Email = newEmail,
            PostalAddress = string.IsNullOrWhiteSpace(request.PostalAddress) ? null : request.PostalAddress.Trim(),
            PhysicalAddress = string.IsNullOrWhiteSpace(request.PhysicalAddress) ? null : request.PhysicalAddress.Trim(),
            AlternativePhone = string.IsNullOrWhiteSpace(request.AlternativePhone) ? null : request.AlternativePhone.Trim(),
            PlaceOfWork = string.IsNullOrWhiteSpace(request.PlaceOfWork) ? null : request.PlaceOfWork.Trim(),
            WorkTelephone = string.IsNullOrWhiteSpace(request.WorkTelephone) ? null : request.WorkTelephone.Trim(),
            WorkPhysicalAddress = string.IsNullOrWhiteSpace(request.WorkPhysicalAddress) ? null : request.WorkPhysicalAddress.Trim(),
            ClientSignature = newSignature
        });

        await conn.ExecuteAsync(
            "UPDATE dbo.MobileUsers SET email = @Email WHERE id = @Id;",
            new { Email = newEmail, Id = userId });

        var updated = await conn.QueryFirstOrDefaultAsync<SystemUserProfileSnapshot>(@"
SELECT TOP 1
    FullName, NationalId, Phone, Email, KraPin, Gender, PostalAddress, PhysicalAddress, IprsReference, IprsVerified,
    AlternativePhone, PlaceOfWork, WorkTelephone, WorkPhysicalAddress, ProfilePhotoPath, ClientSignature, ClientSignDate
FROM SystemUsers
WHERE Id = @Id;", new { Id = systemUserId.Value });

        return OkEnvelope<object>(new
        {
            fullName = updated!.FullName,
            nationalId = updated.NationalId,
            phone = updated.Phone,
            email = updated.Email,
            kraPin = updated.KraPin,
            gender = updated.Gender,
            postalAddress = updated.PostalAddress,
            physicalAddress = updated.PhysicalAddress,
            iprsReference = updated.IprsReference,
            iprsVerified = updated.IprsVerified,
            alternativePhone = updated.AlternativePhone,
            placeOfWork = updated.PlaceOfWork,
            workTelephone = updated.WorkTelephone,
            workPhysicalAddress = updated.WorkPhysicalAddress,
            profilePhotoPath = updated.ProfilePhotoPath,
            profilePhotoUrl = string.IsNullOrWhiteSpace(updated.ProfilePhotoPath) ? null : $"/uploads/{updated.ProfilePhotoPath.TrimStart('/')}",
            clientSignature = updated.ClientSignature,
            clientSignDate = updated.ClientSignDate
        }, "Profile updated");
    }

    [HttpPost("me/photo")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ApiEnvelope<object>>> UpdateProfilePhoto([FromForm] IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return FailEnvelope<object>(400, "A photo file is required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        var schemaError = await EnsureSystemUserColumnsAsync(conn, null, "ProfilePhotoPath", "UpdatedAt");
        if (schemaError != null)
        {
            return schemaError;
        }

        var systemUserId = await ResolveSystemUserIdAsync(conn, userId);
        if (!systemUserId.HasValue)
        {
            return FailEnvelope<object>(404, "Profile not found in SystemUsers.", "profile_not_found");
        }

        var saved = await SaveProfilePhotoAsync(systemUserId.Value, file);

        await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET ProfilePhotoPath = @Path, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;", new { Path = saved.relativePath, Id = systemUserId.Value });

        return OkEnvelope<object>(new { profilePhotoUrl = saved.publicUrl }, "Profile photo updated");
    }

    // Shared by UpdateProfilePhoto -- Me() and UpdateProfile() keep their own inline copies of
    // this lookup since they were already written and verified before this method existed.
    private async Task<int?> ResolveSystemUserIdAsync(SqlConnection conn, long mobileUserId)
    {
        var mobileUser = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 id, phone, email FROM dbo.MobileUsers WHERE id = @id;", new { id = mobileUserId });

        if (mobileUser == null)
        {
            return null;
        }

        var mobilePhone = NormalizePhone(Convert.ToString(mobileUser.phone) ?? string.Empty);
        var mobileEmail = NormalizeEmail(Convert.ToString(mobileUser.email));
        var phoneTokens = BuildPhoneTokens(mobilePhone);

        int? systemUserId = null;
        if (phoneTokens.Length > 0)
        {
            foreach (var phoneToken in ((IEnumerable<string>)phoneTokens).Distinct(StringComparer.Ordinal))
            {
                systemUserId = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id FROM SystemUsers
WHERE IsDeleted = 0
  AND REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') = @phoneToken
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { phoneToken });

                if (systemUserId.HasValue)
                {
                    break;
                }
            }
        }

        if (!systemUserId.HasValue && !string.IsNullOrWhiteSpace(mobileEmail))
        {
            systemUserId = await conn.ExecuteScalarAsync<int?>(@"
SELECT TOP 1 Id FROM SystemUsers
WHERE IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email
ORDER BY UpdatedAt DESC, CreatedAt DESC;", new { email = mobileEmail });
        }

        return systemUserId;
    }

    private async Task<(string relativePath, string publicUrl)> SaveProfilePhotoAsync(int systemUserId, IFormFile file)
    {
        var uploadsRoot = Path.Combine(_hostEnvironment.ContentRootPath, "wwwroot", "uploads",
            _options.UploadsSubFolder, "profile-photos", systemUserId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(uploadsRoot, safeName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = Path.Combine(_options.UploadsSubFolder, "profile-photos", systemUserId.ToString(), safeName)
            .Replace('\\', '/');
        return (relativePath, $"/uploads/{relativePath.TrimStart('/')}");
    }

    [HttpPost("signin")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> Signin([FromBody] SigninRequest request)
    {
        // Safety net: the permissions table auto-seeds on startup (see Program.cs), so this should
        // never actually trip in normal operation. It exists so the app can never silently run with
        // an undefined RBAC state -- if seeding ever fails, PIN sign-in refuses rather than proceeding.
        if (!await _permissions.IsInitializedAsync())
        {
            return FailEnvelope<object>(503, "System is not ready to sign in yet. Please try again shortly.", "permissions_not_initialized");
        }

        var phone = NormalizePhone(request.Phone);
        var phoneTokens = BuildPhoneTokens(request.Phone);
        if (string.IsNullOrWhiteSpace(phone) || !IsValidPin(request.Pin))
        {
            return FailEnvelope<object>(400, "phone and valid pin are required.", "validation_error");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();

        var signinSchemaError = await EnsureSystemUserColumnsAsync(conn, null,
            "Phone",
            "PinHash",
            "IprsVerified",
            "UpdatedAt");
        if (signinSchemaError != null)
        {
            return signinSchemaError;
        }

        var user = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, FullName, Email, Phone, PinHash, IprsVerified, IsActive, Role
FROM SystemUsers
    WHERE REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') IN @phoneTokens
    AND IsDeleted = 0;", new { phoneTokens });

        if (user == null || !(bool)user.IsActive)
        {
            return FailEnvelope<object>(401, "Invalid phone or PIN.", "invalid_credentials");
        }

        var pinHash = Convert.ToString(user.PinHash);
        if (string.IsNullOrWhiteSpace(pinHash) || !BCrypt.Net.BCrypt.Verify(request.Pin.Trim(), pinHash))
        {
            return FailEnvelope<object>(401, "Invalid phone or PIN.", "invalid_credentials");
        }

        // Per-user activation gate -- clients only (Role 4). Staff accounts are created directly
        // by an admin (not via self-service signup) so this "pending review" gate doesn't apply to them.
        var systemUserId = Convert.ToInt32(user.Id);
        const int clientRole = 4;
        if ((int)user.Role == clientRole &&
            !await _permissions.UserHasDirectPermissionAsync(systemUserId, OnwardsSwift.Infrastructure.Services.PermissionService.MobileLogin))
        {
            return FailEnvelope<object>(403, "You have not been granted access yet. Please contact support.", "account_pending_activation");
        }

        var signinUserId = ResolveSystemUserId((object?)user.Id);

        await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET LastLoginAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @id;", new { id = signinUserId.dbValue });

        var fullName = Convert.ToString(user.FullName) ?? "Onwards User";
        var email = Convert.ToString(user.Email);
        if (!string.IsNullOrWhiteSpace(email) && email.EndsWith("@onwardsswift.local", StringComparison.OrdinalIgnoreCase))
        {
            email = null;
        }

        // Always use the canonical phone from SystemUsers as the lookup key so that
        // a user signing in with a different format (07... vs +254...) doesn't create
        // a new MobileUsers row and silently orphan all their existing profile data.
        var canonicalPhone = NormalizePhone(Convert.ToString(user.Phone) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(canonicalPhone))
        {
            canonicalPhone = phone; // fallback to request-normalised if DB phone is unparseable
        }

        var localUserId = await EnsureLocalUserAsync(canonicalPhone, fullName, email);
        var authPayload = await CreateSessionTokenAsync(localUserId, canonicalPhone, fullName, email);
        return OkEnvelope<object>(authPayload, "Signin successful");
    }

    [HttpPost("pin/signin")]
    [AllowAnonymous]
    public Task<ActionResult<ApiEnvelope<object>>> SigninAliasOne([FromBody] SigninRequest request)
    {
        return Signin(request);
    }

    [HttpPost("signin/pin")]
    [AllowAnonymous]
    public Task<ActionResult<ApiEnvelope<object>>> SigninAliasTwo([FromBody] SigninRequest request)
    {
        return Signin(request);
    }

    [HttpPost("pin/reset")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> ResetPin([FromBody] PinResetRequest request)
    {
        var identity = ResolveIdentity(request.Identifier, request.Phone, request.Email, request.PhoneRaw);
        var newPin = GetEffectiveNewPin(request);

        if (!IsValidPin(newPin))
        {
            return FailEnvelope<object>(400, "newPin must be 4 to 8 digits.", "validation_error");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();

        var resetSchemaError = await EnsureSystemUserColumnsAsync(conn, null,
            "Phone",
            "Email",
            "PinHash",
            "PinUpdatedAt",
            "UpdatedAt");
        if (resetSchemaError != null)
        {
            return resetSchemaError;
        }

        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            if (!long.TryParse(request.RequestId.Trim(), out var requestId) || requestId <= 0)
            {
                return FailEnvelope<object>(400, "requestId is invalid.", "otp_invalid");
            }

            if (identity == null)
            {
                return FailEnvelope<object>(400, "phone, email, or identifier is required.", "validation_error");
            }

            await EnsureLiveAuthTablesAsync(conn);

            var otp = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT id, phone, purpose, status, expires_at_utc, consumed_at_utc, metadata_json
FROM dbo.MobileOtpRequests
WHERE id = @id
", new { id = requestId });

            if (otp == null)
            {
                return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
            }

            var otpIdentity = NormalizeIdentityValue(identity.Type, Convert.ToString(otp.phone));
            var purpose = Convert.ToString(otp.purpose) ?? string.Empty;
            var status = Convert.ToString(otp.status) ?? string.Empty;
            var consumedAt = Convert.ToString(otp.consumed_at_utc);
            var metadata = ParseOtpMetadata(Convert.ToString(otp.metadata_json));

            var storedIdentityType = string.IsNullOrWhiteSpace(metadata.IdentityType) ? identity.Type : metadata.IdentityType!;
            var storedIdentity = !string.IsNullOrWhiteSpace(metadata.IdentityValueNormalized)
                ? NormalizeIdentityValue(storedIdentityType, metadata.IdentityValueNormalized)
                : otpIdentity;
            var currentIdentity = NormalizeIdentityValue(storedIdentityType, identity.Value);

            if (!string.Equals(storedIdentity, currentIdentity, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(purpose, "trouble_signin", StringComparison.OrdinalIgnoreCase))
            {
                return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
            }

            if (!string.IsNullOrWhiteSpace(consumedAt) ||
                !string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase) ||
                !metadata.VerifiedForReset)
            {
                return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
            }

            var expiresAt = ReadUtcDateTime((object?)otp.expires_at_utc);

            if (expiresAt < DateTime.UtcNow)
            {
                await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='expired' WHERE id=@id", new { id = requestId });
                return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
            }

            dynamic? user;
            if (string.Equals(storedIdentityType, "email", StringComparison.OrdinalIgnoreCase))
            {
                user = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, Phone, NationalId, Email
FROM SystemUsers
WHERE LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) = @email
  AND IsDeleted = 0;", new { email = currentIdentity });
            }
            else
            {
                var phoneTokens = BuildPhoneTokens(currentIdentity);
                if (phoneTokens.Length == 0)
                {
                    return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
                }

                user = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, Phone, NationalId, Email
FROM SystemUsers
WHERE REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') IN @phoneTokens
  AND IsDeleted = 0;", new { phoneTokens });
            }

            if (user == null)
            {
                return FailEnvelope<object>(400, "Unable to reset PIN.", "reset_not_allowed");
            }

            var systemUserId = ResolveSystemUserId((object?)user.Id);
            var pinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
            await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET PinHash = @pinHash,
    PinUpdatedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @id;", new { pinHash, id = systemUserId.dbValue });

            metadata.ConsumedAtUtc = DateTime.UtcNow.ToString("O");
            await conn.ExecuteAsync(@"
UPDATE dbo.MobileOtpRequests
SET status='consumed',
    consumed_at_utc=@now,
    metadata_json=@metadata
WHERE id=@id", new
            {
                now = DateTime.UtcNow.ToString("O"),
                metadata = SerializeOtpMetadata(metadata),
                id = requestId
            });

            await WritePinResetAuditAsync(conn,
                phone: NormalizeIdentityValue("phone", Convert.ToString(user.Phone)),
                nationalId: NormalizeNationalId(Convert.ToString(user.NationalId)),
                requestId: requestId.ToString(),
                userIdGuid: systemUserId.guidValue,
                userIdInt: systemUserId.intValue);

            return OkEnvelope<object>(new { message = "PIN reset successful." }, "PIN reset successful.");
        }

        // Legacy compatibility path: phone + nationalId reset without verified requestId
        var phone = NormalizePhone(request.Phone);
        var nationalId = NormalizeNationalId(request.NationalId);
        var phoneTokensLegacy = BuildPhoneTokens(request.Phone);
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(nationalId))
        {
            return FailEnvelope<object>(400, "phone, nationalId, and requestId verification are required.", "validation_error");
        }

        var legacyUser = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, Phone, NationalId
FROM SystemUsers
WHERE REPLACE(REPLACE(REPLACE(ISNULL(Phone, ''), '+', ''), ' ', ''), '-', '') IN @phoneTokens
  AND REPLACE(REPLACE(UPPER(ISNULL(NationalId, '')), ' ', ''), '-', '') = @nationalId
  AND IsDeleted = 0;", new { phoneTokens = phoneTokensLegacy, nationalId });

        if (legacyUser == null)
        {
            return FailEnvelope<object>(404, "User not found.", "user_not_found");
        }

        var legacySystemUserId = ResolveSystemUserId((object?)legacyUser.Id);
        var legacyPinHash = BCrypt.Net.BCrypt.HashPassword(newPin);
        await conn.ExecuteAsync(@"
UPDATE SystemUsers
SET PinHash = @pinHash,
    PinUpdatedAt = SYSUTCDATETIME(),
    UpdatedAt = SYSUTCDATETIME()
    WHERE Id = @id;", new { pinHash = legacyPinHash, id = legacySystemUserId.dbValue });

        await WritePinResetAuditAsync(conn,
            phone: phone,
            nationalId: nationalId,
            requestId: request.RequestId,
            userIdGuid: legacySystemUserId.guidValue,
            userIdInt: legacySystemUserId.intValue);

        return OkEnvelope<object>(new { phone }, "PIN reset successful.");
    }

    [HttpPost("reset-pin")]
    [AllowAnonymous]
    public Task<ActionResult<ApiEnvelope<object>>> ResetPinAliasOne([FromBody] PinResetRequest request)
    {
        return ResetPin(request);
    }

    [HttpPost("trouble/reset-pin")]
    [AllowAnonymous]
    public Task<ActionResult<ApiEnvelope<object>>> ResetPinAliasTwo([FromBody] PinResetRequest request)
    {
        return ResetPin(request);
    }

    [HttpPost("otp/request")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> RequestOtp([FromBody] OtpRequestCommand request)
    {
        return await CreateOtpRequestAsync(request, request.Purpose);
    }

    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> VerifyOtp([FromBody] OtpVerifyCommand request)
    {
        return await VerifyOtpCoreAsync(request, request.Purpose);
    }

    [HttpPost("trouble/request")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> TroubleRequest([FromBody] OtpRequestCommand request)
    {
        if (!string.IsNullOrWhiteSpace(request.Purpose) &&
            !string.Equals(request.Purpose.Trim(), "trouble_signin", StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "purpose must be trouble_signin.", "validation_error");
        }

        return await CreateOtpRequestAsync(request, "trouble_signin");
    }

    [HttpPost("trouble/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiEnvelope<object>>> TroubleVerify([FromBody] OtpVerifyCommand request)
    {
        if (!string.IsNullOrWhiteSpace(request.Purpose) &&
            !string.Equals(request.Purpose.Trim(), "trouble_signin", StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "purpose must be trouble_signin.", "validation_error");
        }

        return await VerifyOtpCoreAsync(request, "trouble_signin");
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<ActionResult<ApiEnvelope<object>>> Logout([FromBody] LogoutRequest request)
    {
        var userId = GetUserIdOrThrow();
        var jti = request.Jti;
        if (string.IsNullOrWhiteSpace(jti))
        {
            jti = GetJti();
        }

        if (string.IsNullOrWhiteSpace(jti))
        {
            return FailEnvelope<object>(400, "Session id not found.", "missing_jti");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        const string sql = @"
UPDATE dbo.MobileSessions
SET is_revoked = 1,
    revoked_at_utc = @now
WHERE user_id = @userId AND jti = @jti AND is_revoked = 0;";

        var changed = await conn.ExecuteAsync(sql, new { userId, jti, now = DateTime.UtcNow });
        return OkEnvelope<object>(new { revoked = changed > 0, jti }, "Logout completed");
    }

    private async Task<ActionResult<ApiEnvelope<object>>> CreateOtpRequestAsync(OtpRequestCommand request, string forcedPurpose)
    {
        var purpose = string.IsNullOrWhiteSpace(forcedPurpose) ? "signin" : forcedPurpose.Trim().ToLowerInvariant();
        var identity = ResolveIdentity(request.Identifier, request.Phone, request.Email);
        if (identity == null)
        {
            return FailEnvelope<object>(400, "phone, email, or identifier is required.", "validation_error");
        }

        if (string.Equals(forcedPurpose, "trouble_signin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(purpose, "trouble_signin", StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "purpose must be trouble_signin.", "validation_error");
        }

        var code = _otpService.GenerateOtpCode();
        var hash = _otpService.HashOtp(code);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.Otp.ExpiryMinutes);
        var requestedChannels = NormalizeChannels(request.DeliveryChannels);
        var preferredChannel = ResolvePreferredChannel(identity.Type, requestedChannels);
        var dispatchStatus = "pending";
        var templateKey = string.Equals(purpose, "trouble_signin", StringComparison.OrdinalIgnoreCase)
            ? "otp_trouble_signin"
            : "otp_signin";

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);
        await SignupWizard.SignupWizardSchema.EnsureAsync(conn);

        using var tx = await conn.BeginTransactionAsync();
        try
        {
            const string markOldSql = @"
UPDATE dbo.MobileOtpRequests
SET status = 'superseded',
    dispatch_status = 'superseded'
WHERE phone = @identityValue
  AND purpose = @purpose
  AND status = 'pending';";

            await conn.ExecuteAsync(markOldSql, new { identityValue = identity.Value, purpose }, tx);

            const string createSql = @"
INSERT INTO dbo.MobileOtpRequests(phone, purpose, otp_hash, expires_at_utc, attempts, status, created_at_utc, metadata_json, identifier_type, preferred_channel, delivery_channels, dispatch_status)
VALUES(@phone, @purpose, @otpHash, @expiresAt, 0, 'pending', @createdAt, @meta, @identifierType, @preferredChannel, @deliveryChannels, @dispatchStatus);
SELECT CAST(SCOPE_IDENTITY() as bigint);";

            var otpMetadata = new OtpMetadata
            {
                IdentityType = identity.Type,
                IdentityValueNormalized = identity.Value,
                VerifiedForReset = false,
                CreatedAtUtc = now.ToString("O")
            };

            var otpRequestId = await conn.ExecuteScalarAsync<long>(createSql, new
            {
                phone = identity.Value,
                purpose,
                otpHash = hash,
                expiresAt,
                createdAt = now,
                meta = SerializeOtpMetadata(otpMetadata),
                identifierType = identity.Type,
                preferredChannel,
                deliveryChannels = string.Join(",", requestedChannels),
                dispatchStatus
            }, tx);

            var outboxChannel = string.Equals(identity.Type, "phone", StringComparison.OrdinalIgnoreCase) ? "sms" : "email";
            var outboxInsertSql = @"
INSERT INTO dbo.notification_outbox
(
    wizard_session_id,
    channel,
    recipient,
    destination,
    template_key,
    message_body,
    status,
    attempts,
    max_attempts,
    otp_request_id,
    created_at_utc,
    next_attempt_at_utc,
    updated_at_utc
)
VALUES
(
    @wizardSessionId,
    @channel,
    @recipient,
    @destination,
    @templateKey,
    @messageBody,
    'pending',
    0,
    5,
    @otpRequestId,
    SYSUTCDATETIME(),
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);
SELECT CAST(SCOPE_IDENTITY() as bigint);";

            var outboxId = await conn.ExecuteScalarAsync<long>(outboxInsertSql, new
            {
                wizardSessionId = Guid.Empty,
                channel = outboxChannel,
                recipient = identity.Value,
                destination = identity.Value,
                templateKey,
                messageBody = $"Your OTP code is {code}. It expires in {_options.Otp.ExpiryMinutes} minutes.",
                otpRequestId
            }, tx);

            if (outboxId <= 0)
            {
                throw new InvalidOperationException("Failed to enqueue OTP notification.");
            }

            await tx.CommitAsync();

            return OkEnvelope<object>(new
            {
                requestId = otpRequestId,
                otpRequestId,
                identity = identity.Value,
                identityType = identity.Type,
                requested = requestedChannels,
                preferred = preferredChannel,
                enqueued = true,
                queuedChannel = outboxChannel,
                purpose,
                expiry = expiresAt,
                expiresAtUtc = expiresAt,
                dispatchStatus,
                templateKey,
                devOtpCode = _hostEnvironment.IsProduction() ? null : code
            }, "OTP sent");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "OTP request enqueue failed for {Identity}", identity.Value);
            return FailEnvelope<object>(500, "Failed to enqueue OTP notification.", "otp_enqueue_failed");
        }
    }

    private async Task<ActionResult<ApiEnvelope<object>>> VerifyOtpCoreAsync(OtpVerifyCommand request, string forcedPurpose)
    {
        if (string.IsNullOrWhiteSpace(request.Otp))
        {
            return FailEnvelope<object>(400, "otp is required.", "validation_error");
        }

        var purpose = string.IsNullOrWhiteSpace(forcedPurpose) ? "signin" : forcedPurpose.Trim().ToLowerInvariant();

        if (string.Equals(purpose, "trouble_signin", StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyTroubleOtpAsync(request);
        }

        var phone = NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(phone))
        {
            return FailEnvelope<object>(400, "phone is required.", "validation_error");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        const string getOtpSql = @"
SELECT id, otp_hash, expires_at_utc, attempts, status
FROM dbo.MobileOtpRequests
WHERE phone = @phone
  AND purpose = @purpose
  AND status = 'pending'
ORDER BY id DESC
";

        var otp = await conn.QueryFirstOrDefaultAsync<dynamic>(getOtpSql, new { phone, purpose });
        if (otp == null)
        {
            return FailEnvelope<object>(404, "No active OTP request found.", "otp_not_found");
        }

        var otpId = (long)otp.id;
        var attempts = (long)otp.attempts;
        var expiresAt = ReadUtcDateTime((object?)otp.expires_at_utc);
        if (DateTime.UtcNow > expiresAt)
        {
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='expired' WHERE id=@id", new { id = otpId });
            return FailEnvelope<object>(400, "OTP expired.", "otp_expired");
        }

        if (attempts >= _options.Otp.MaxAttempts)
        {
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='blocked' WHERE id=@id", new { id = otpId });
            return FailEnvelope<object>(429, "Maximum OTP attempts reached.", "otp_blocked");
        }

        if (!_otpService.VerifyOtp(request.Otp.Trim(), (string)otp.otp_hash))
        {
            var nextAttempts = attempts + 1;
            var nextStatus = nextAttempts >= _options.Otp.MaxAttempts ? "blocked" : "pending";
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET attempts=@attempts, status=@status WHERE id=@id",
                new { attempts = nextAttempts, status = nextStatus, id = otpId });
            return FailEnvelope<object>(400, "Invalid OTP code.", "otp_invalid");
        }

        await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='verified', consumed_at_utc=@now WHERE id=@id",
            new { now = DateTime.UtcNow, id = otpId });

        const string getUserSql = "SELECT TOP 1 id, full_name, phone, email FROM dbo.MobileUsers WHERE phone = @phone;";
        var user = await conn.QueryFirstOrDefaultAsync<dynamic>(getUserSql, new { phone });
        long userId;
        string fullName;
        string? email;

        if (user == null)
        {
            fullName = "Onwards User";
            email = null;
            userId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO dbo.MobileUsers(full_name, phone, email, status, created_at_utc, updated_at_utc)
VALUES(@fullName, @phone, NULL, 'active', @now, @now);
SELECT CAST(SCOPE_IDENTITY() as bigint);", new { fullName, phone, now = DateTime.UtcNow });
        }
        else
        {
            userId = (long)user.id;
            fullName = (string)user.full_name;
            email = (string?)user.email;
        }

        var expiryHours = int.TryParse(_configuration["Jwt:ExpiryHours"], out var configuredHours) && configuredHours > 0
            ? configuredHours
            : 8;

        var expiresAtUtc = DateTime.UtcNow.AddHours(expiryHours);
        var jti = TokenService.NewJti();
        var token = _tokenService.CreateToken(userId, phone, fullName, jti, expiresAtUtc);

        await conn.ExecuteAsync(@"
INSERT INTO dbo.MobileSessions(user_id, jti, token_hash, expires_at_utc, is_revoked, created_at_utc)
VALUES(@userId, @jti, @tokenHash, @expiresAt, 0, @createdAt);", new
        {
            userId,
            jti,
            tokenHash = TokenService.ComputeSha256(token),
            expiresAt = expiresAtUtc,
            createdAt = DateTime.UtcNow
        });

        return OkEnvelope<object>(new
        {
            token,
            tokenType = "Bearer",
            expiresAtUtc,
            sessionJti = jti,
            user = new
            {
                userId,
                fullName,
                phone,
                email
            }
        }, "OTP verified");
    }

    private async Task<ActionResult<ApiEnvelope<object>>> VerifyTroubleOtpAsync(OtpVerifyCommand request)
    {
        var identity = ResolveIdentity(request.Identifier, request.Phone, request.Email);
        if (identity == null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            return FailEnvelope<object>(400, "requestId and one identity (phone, email, or identifier) are required.", "validation_error");
        }

        if (!long.TryParse(request.RequestId.Trim(), out var requestId) || requestId <= 0)
        {
            return FailEnvelope<object>(400, "requestId is invalid.", "validation_error");
        }

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        var otp = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT id, phone, purpose, otp_hash, expires_at_utc, attempts, status, consumed_at_utc, metadata_json
FROM dbo.MobileOtpRequests
WHERE id = @id
", new { id = requestId });

        if (otp == null)
        {
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_invalid");
        }

        var purpose = Convert.ToString(otp.purpose) ?? string.Empty;
        if (!string.Equals(purpose, "trouble_signin", StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_invalid");
        }

        var status = Convert.ToString(otp.status) ?? string.Empty;
        if (!string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_invalid");
        }

        var consumedAt = Convert.ToString(otp.consumed_at_utc);
        if (!string.IsNullOrWhiteSpace(consumedAt))
        {
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_invalid");
        }

        var expiresAt = ReadUtcDateTime((object?)otp.expires_at_utc);

        if (expiresAt < DateTime.UtcNow)
        {
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='expired' WHERE id=@id", new { id = requestId });
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_expired");
        }

        var metadata = ParseOtpMetadata(Convert.ToString(otp.metadata_json));
        var storedIdentityType = string.IsNullOrWhiteSpace(metadata.IdentityType) ? identity.Type : metadata.IdentityType!;
        var storedIdentity = !string.IsNullOrWhiteSpace(metadata.IdentityValueNormalized)
            ? NormalizeIdentityValue(storedIdentityType, metadata.IdentityValueNormalized)
            : NormalizeIdentityValue(storedIdentityType, Convert.ToString(otp.phone));
        var requestIdentity = NormalizeIdentityValue(storedIdentityType, identity.Value);

        if (!string.Equals(storedIdentity, requestIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return FailEnvelope<object>(400, "Invalid or expired verification request.", "otp_invalid");
        }

        var attempts = (long)otp.attempts;
        if (attempts >= _options.Otp.MaxAttempts)
        {
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET status='blocked' WHERE id=@id", new { id = requestId });
            return FailEnvelope<object>(429, "Maximum OTP attempts reached.", "otp_blocked");
        }

        if (!_otpService.VerifyOtp(request.Otp.Trim(), (string)otp.otp_hash))
        {
            var nextAttempts = attempts + 1;
            var nextStatus = nextAttempts >= _options.Otp.MaxAttempts ? "blocked" : "pending";
            await conn.ExecuteAsync("UPDATE dbo.MobileOtpRequests SET attempts=@attempts, status=@status WHERE id=@id",
                new { attempts = nextAttempts, status = nextStatus, id = requestId });
            return FailEnvelope<object>(400, "Invalid OTP code.", "otp_invalid");
        }

        metadata.IdentityType = storedIdentityType;
        metadata.IdentityValueNormalized = storedIdentity;
        metadata.VerifiedForReset = true;
        metadata.VerifiedAtUtc = DateTime.UtcNow.ToString("O");

        await conn.ExecuteAsync(@"
UPDATE dbo.MobileOtpRequests
SET status='verified',
    metadata_json=@metadata
WHERE id=@id", new
        {
            metadata = SerializeOtpMetadata(metadata),
            id = requestId
        });

        return OkEnvelope<object>(new
        {
            requestId,
            expiry = expiresAt,
            identity = storedIdentity,
            identityType = storedIdentityType,
            verified = true
        }, "OTP verified");
    }

    private static string NormalizePhone(string phone)
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

    private static string NormalizeIdentityValue(string identityType, string? identityValue)
    {
        if (string.Equals(identityType, "email", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeEmail(identityValue);
        }

        return NormalizePhone(identityValue ?? string.Empty);
    }

    private static ResolvedIdentity? ResolveIdentity(string? identifier, string? phone, string? email, string? phoneRaw = null)
    {
        if (!string.IsNullOrWhiteSpace(identifier))
        {
            var trimmedIdentifier = identifier.Trim();
            if (trimmedIdentifier.Contains("@", StringComparison.Ordinal))
            {
                var normalizedEmail = NormalizeEmail(trimmedIdentifier);
                return string.IsNullOrWhiteSpace(normalizedEmail) ? null : new ResolvedIdentity("email", normalizedEmail);
            }

            var normalizedPhone = NormalizePhone(trimmedIdentifier);
            return string.IsNullOrWhiteSpace(normalizedPhone) ? null : new ResolvedIdentity("phone", normalizedPhone);
        }

        var normalizedPhoneFromField = NormalizePhone(phone ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedPhoneFromField) && !string.IsNullOrWhiteSpace(phoneRaw))
        {
            normalizedPhoneFromField = NormalizePhone(phoneRaw);
        }

        if (!string.IsNullOrWhiteSpace(normalizedPhoneFromField))
        {
            return new ResolvedIdentity("phone", normalizedPhoneFromField);
        }

        var normalizedEmailFromField = NormalizeEmail(email);
        if (!string.IsNullOrWhiteSpace(normalizedEmailFromField))
        {
            return new ResolvedIdentity("email", normalizedEmailFromField);
        }

        return null;
    }

    private static string GetEffectiveNewPin(PinResetRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.NewPin)
            ? request.NewPin.Trim()
            : (request.NewPinAlt?.Trim() ?? string.Empty);
    }

    private static DateTime ReadUtcDateTime(object? raw)
    {
        if (raw is DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            };
        }

        if (raw is DateTimeOffset dto)
        {
            return dto.UtcDateTime;
        }

        var text = Convert.ToString(raw);
        if (string.IsNullOrWhiteSpace(text))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedInvariant))
        {
            return parsedInvariant;
        }

        if (DateTime.TryParse(text, out var parsedLocal))
        {
            return DateTime.SpecifyKind(parsedLocal, DateTimeKind.Utc);
        }

        return DateTime.MinValue;
    }

    private static OtpMetadata ParseOtpMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new OtpMetadata();
        }

        try
        {
            return JsonSerializer.Deserialize<OtpMetadata>(raw) ?? new OtpMetadata();
        }
        catch
        {
            return new OtpMetadata();
        }
    }

    private static string SerializeOtpMetadata(OtpMetadata metadata)
    {
        return JsonSerializer.Serialize(metadata);
    }

    private static (object dbValue, Guid? guidValue, int? intValue) ResolveSystemUserId(object? rawId)
    {
        if (rawId is Guid guid)
        {
            return (guid, guid, null);
        }

        if (rawId is int idInt)
        {
            return (idInt, null, idInt);
        }

        if (rawId is long idLong && idLong >= int.MinValue && idLong <= int.MaxValue)
        {
            var converted = (int)idLong;
            return (converted, null, converted);
        }

        var text = Convert.ToString(rawId)?.Trim();
        if (Guid.TryParse(text, out var parsedGuid))
        {
            return (parsedGuid, parsedGuid, null);
        }

        if (int.TryParse(text, out var parsedInt))
        {
            return (parsedInt, null, parsedInt);
        }

        throw new InvalidOperationException("Unsupported SystemUsers.Id type. Expected int or Guid.");
    }

    private async Task WritePinResetAuditAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        string phone,
        string nationalId,
        string? requestId,
        Guid? userIdGuid,
        int? userIdInt)
    {
        try
        {
            await conn.ExecuteAsync(@"
IF OBJECT_ID('dbo.pin_reset_audit', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.pin_reset_audit', 'user_id') IS NOT NULL
    BEGIN
        INSERT INTO dbo.pin_reset_audit(user_id, phone, national_id, request_id, reset_at, success, note)
        VALUES(@userIdInt, @phone, @nationalId, @requestId, SYSUTCDATETIME(), 1, 'pin_reset_success')
    END
    ELSE IF COL_LENGTH('dbo.pin_reset_audit', 'UserId') IS NOT NULL
    BEGIN
        INSERT INTO dbo.pin_reset_audit(UserId, Phone, NationalId, RequestId, CreatedAt)
        VALUES(@userIdGuid, @phone, @nationalId, @requestId, SYSUTCDATETIME())
    END
END", new
            {
                userIdGuid,
                userIdInt,
                phone = string.IsNullOrWhiteSpace(phone) ? "" : phone,
                nationalId = string.IsNullOrWhiteSpace(nationalId) ? "" : nationalId,
                requestId
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write pin_reset_audit for identity {Identity}", string.IsNullOrWhiteSpace(phone) ? "email" : phone);
        }
    }

    private static bool IsValidPin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        return Regex.IsMatch(pin.Trim(), "^[0-9]{4,8}$");
    }

    private static string NormalizeNationalId(string nationalId)
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

    private static string[] BuildPhoneTokens(string phoneInput)
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

    private async Task<long> EnsureLocalUserAsync(string phone, string fullName, string? email)
    {
        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);
        return await EnsureLocalUserAsync(conn, null, phone, fullName, email);
    }

    private async Task<long> EnsureLocalUserAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        DbTransaction? tx,
        string phone,
        string fullName,
        string? email)
    {
        var dapperTx = tx;

        var normalizedEmail = string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();

        var existingUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT TOP 1 id, email FROM dbo.MobileUsers WHERE phone=@phone",
            new { phone }, dapperTx);

        // Fallback: search by all known phone token variants so that a format
        // difference between signup and signin never orphans the existing row.
        if (existingUser == null)
        {
            var phoneTokensForLookup = BuildPhoneTokens(phone);
            if (phoneTokensForLookup.Length > 0)
            {
                existingUser = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT TOP 1 id, email FROM dbo.MobileUsers WHERE REPLACE(REPLACE(REPLACE(ISNULL(phone,''),'+',''),' ',''),'-','') IN @tokens",
                    new { tokens = phoneTokensForLookup }, dapperTx);

                // Normalize the stored phone to canonical form while we have the row
                if (existingUser != null)
                {
                    await conn.ExecuteAsync(
                        "UPDATE dbo.MobileUsers SET phone=@phone, updated_at_utc=@now WHERE id=@id",
                        new { phone, now = DateTime.UtcNow.ToString("O"), id = (long)existingUser.id }, dapperTx);
                }
            }
        }

        var now = DateTime.UtcNow.ToString("O");
        if (existingUser != null)
        {
            var existingUserId = (long)existingUser.id;
            string? emailToPersist = normalizedEmail;

            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                var emailOwnedByAnother = await conn.ExecuteScalarAsync<long?>(
                    "SELECT TOP 1 id FROM dbo.MobileUsers WHERE email=@email AND id<>@id",
                    new { email = normalizedEmail, id = existingUserId }, dapperTx);

                if (emailOwnedByAnother.HasValue)
                {
                    // Keep current email when target email belongs to another local user.
                    emailToPersist = Convert.ToString(existingUser.email);
                }
            }

            await conn.ExecuteAsync(@"
UPDATE dbo.MobileUsers
SET full_name = @fullName,
    email = @email,
    updated_at_utc = @now
WHERE id = @id", new
            {
                id = existingUserId,
                fullName,
                email = emailToPersist,
                now
            }, dapperTx);

            return existingUserId;
        }

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var existingByEmailId = await conn.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id FROM dbo.MobileUsers WHERE email=@email",
                new { email = normalizedEmail }, dapperTx);

            if (existingByEmailId.HasValue)
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.MobileUsers
SET full_name = @fullName,
    phone = @phone,
    updated_at_utc = @now
WHERE id = @id", new
                {
                    id = existingByEmailId.Value,
                    fullName,
                    phone,
                    now
                }, dapperTx);

                return existingByEmailId.Value;
            }
        }

        try
        {
            return await conn.ExecuteScalarAsync<long>(@"
INSERT INTO dbo.MobileUsers(full_name, phone, email, status, created_at_utc, updated_at_utc)
VALUES(@fullName, @phone, @email, 'active', @now, @now);
SELECT CAST(SCOPE_IDENTITY() as bigint);", new
            {
                fullName,
                phone,
                email = normalizedEmail,
                now
            }, dapperTx);
        }
        catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627)
        {
            // Race-safe fallback for UNIQUE collisions.
            var existingId = await conn.ExecuteScalarAsync<long?>(
                "SELECT TOP 1 id FROM dbo.MobileUsers WHERE phone=@phone", new { phone }, dapperTx)
                ?? await conn.ExecuteScalarAsync<long?>(
                    "SELECT TOP 1 id FROM dbo.MobileUsers WHERE email=@email", new { email = normalizedEmail }, dapperTx);

            if (existingId.HasValue)
            {
                await conn.ExecuteAsync(@"
UPDATE dbo.MobileUsers
SET full_name = @fullName,
    updated_at_utc = @now
WHERE id = @id", new { id = existingId.Value, fullName, now }, dapperTx);
                return existingId.Value;
            }

            throw;
        }
    }

    private async Task<object> CreateSessionTokenAsync(long userId, string phone, string fullName, string? email)
    {
        var expiryHours = int.TryParse(_configuration["Jwt:ExpiryHours"], out var configuredHours) && configuredHours > 0
            ? configuredHours
            : 8;

        var expiresAtUtc = DateTime.UtcNow.AddHours(expiryHours);
        var jti = TokenService.NewJti();
        var token = _tokenService.CreateToken(userId, phone, fullName, jti, expiresAtUtc);

        using var conn = _dapper.CreateConnection();
        await conn.OpenAsync();
        await EnsureLiveAuthTablesAsync(conn);

        await conn.ExecuteAsync(@"
    INSERT INTO dbo.MobileSessions(user_id, jti, token_hash, expires_at_utc, is_revoked, created_at_utc)
VALUES(@userId, @jti, @tokenHash, @expiresAt, 0, @createdAt);", new
        {
            userId,
            jti,
            tokenHash = TokenService.ComputeSha256(token),
            expiresAt = expiresAtUtc,
            createdAt = DateTime.UtcNow
        });

        return new
        {
            token,
            tokenType = "Bearer",
            expiresAtUtc,
            sessionJti = jti,
            user = new
            {
                userId,
                fullName,
                phone,
                email
            }
        };
    }

    private async Task<ActionResult<ApiEnvelope<object>>?> EnsureSystemUserColumnsAsync(
        Microsoft.Data.SqlClient.SqlConnection conn,
        DbTransaction? tx,
        params string[] requiredColumns)
    {
        var existingColumns = (await conn.QueryAsync<string>(@"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.objects o ON o.object_id = c.object_id
WHERE o.type = 'U'
  AND o.name = 'SystemUsers'
  AND SCHEMA_NAME(o.schema_id) = 'dbo';", transaction: tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = requiredColumns.Where(c => !existingColumns.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length == 0)
        {
            return null;
        }

        var list = string.Join(", ", missing);
        _logger.LogError("SystemUsers schema is missing required columns: {MissingColumns}", list);
        return FailEnvelope<object>(500, $"Database schema is missing required columns in SystemUsers: {list}", "schema_mismatch");
    }

    private async Task EnsureLiveAuthTablesAsync(Microsoft.Data.SqlClient.SqlConnection conn)
    {
        const string sql = @"
IF OBJECT_ID('dbo.MobileUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MobileUsers (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        full_name NVARCHAR(200) NOT NULL,
        phone NVARCHAR(64) NOT NULL UNIQUE,
        email NVARCHAR(256) NULL UNIQUE,
        status NVARCHAR(32) NOT NULL DEFAULT('active'),
        created_at_utc DATETIME2(7) NOT NULL,
        updated_at_utc DATETIME2(7) NULL
    );
END;

IF OBJECT_ID('dbo.MobileOtpRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MobileOtpRequests (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        phone NVARCHAR(256) NOT NULL,
        purpose NVARCHAR(64) NOT NULL,
        otp_hash NVARCHAR(256) NOT NULL,
        expires_at_utc DATETIME2(7) NOT NULL,
        attempts INT NOT NULL DEFAULT(0),
        status NVARCHAR(32) NOT NULL,
        created_at_utc DATETIME2(7) NOT NULL,
        consumed_at_utc DATETIME2(7) NULL,
        metadata_json NVARCHAR(MAX) NULL,
        identifier_type NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_IdentifierType DEFAULT('phone'),
        preferred_channel NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_PreferredChannel DEFAULT('sms'),
        delivery_channels NVARCHAR(100) NOT NULL CONSTRAINT DF_MobileOtpRequests_DeliveryChannels DEFAULT('sms'),
        dispatch_status NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_DispatchStatus DEFAULT('pending')
    );

    CREATE INDEX IX_MobileOtpRequests_PhonePurposeStatus ON dbo.MobileOtpRequests(phone, purpose, status);
END;

IF COL_LENGTH('dbo.MobileOtpRequests', 'identifier_type') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD identifier_type NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_IdentifierType DEFAULT('phone') WITH VALUES;
END;

IF COL_LENGTH('dbo.MobileOtpRequests', 'preferred_channel') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD preferred_channel NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_PreferredChannel DEFAULT('sms') WITH VALUES;
END;

IF COL_LENGTH('dbo.MobileOtpRequests', 'delivery_channels') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD delivery_channels NVARCHAR(100) NOT NULL CONSTRAINT DF_MobileOtpRequests_DeliveryChannels DEFAULT('sms') WITH VALUES;
END;

IF COL_LENGTH('dbo.MobileOtpRequests', 'dispatch_status') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD dispatch_status NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_DispatchStatus DEFAULT('pending') WITH VALUES;
END;

IF OBJECT_ID('dbo.MobileSessions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MobileSessions (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        user_id BIGINT NOT NULL,
        jti NVARCHAR(128) NOT NULL UNIQUE,
        token_hash NVARCHAR(128) NOT NULL,
        expires_at_utc DATETIME2(7) NOT NULL,
        is_revoked BIT NOT NULL DEFAULT(0),
        created_at_utc DATETIME2(7) NOT NULL,
        revoked_at_utc DATETIME2(7) NULL
    );

    CREATE INDEX IX_MobileSessions_UserJti ON dbo.MobileSessions(user_id, jti);
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'destination') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox
    ADD destination NVARCHAR(256) NULL;
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'template_key') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox
    ADD template_key NVARCHAR(80) NULL;
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'otp_request_id') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox
    ADD otp_request_id BIGINT NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_dispatch_logs (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        outbox_id BIGINT NULL,
        otp_request_id BIGINT NULL,
        channel NVARCHAR(20) NOT NULL,
        destination NVARCHAR(256) NOT NULL,
        status NVARCHAR(20) NOT NULL,
        provider_response NVARCHAR(MAX) NULL,
        error_message NVARCHAR(MAX) NULL,
        created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_notification_dispatch_logs_outbox_id ON dbo.notification_dispatch_logs(outbox_id);
    CREATE INDEX IX_notification_dispatch_logs_otp_request_id ON dbo.notification_dispatch_logs(otp_request_id);
END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'outbox_id') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD outbox_id BIGINT NULL;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'otp_request_id') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD otp_request_id BIGINT NULL;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'channel') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD channel NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_channel DEFAULT('sms') WITH VALUES;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'destination') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD destination NVARCHAR(256) NOT NULL CONSTRAINT DF_notification_dispatch_logs_destination DEFAULT('') WITH VALUES;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'status') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD status NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_status DEFAULT('sent') WITH VALUES;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'provider_response') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD provider_response NVARCHAR(MAX) NULL;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'error_message') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD error_message NVARCHAR(MAX) NULL;
    END;

    IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
       AND COL_LENGTH('dbo.notification_dispatch_logs', 'created_at_utc') IS NULL
    BEGIN
        ALTER TABLE dbo.notification_dispatch_logs ADD created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;
    END;
";

        await conn.ExecuteAsync(sql);
    }

    private static List<string> NormalizeChannels(List<string>? inputChannels)
    {
        if (inputChannels == null || inputChannels.Count == 0)
        {
            return new List<string> { "sms" };
        }

        var normalized = inputChannels
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x is "sms" or "email")
            .Distinct()
            .ToList();

        return normalized.Count == 0 ? new List<string> { "sms" } : normalized;
    }

    private static string ResolvePreferredChannel(string identityType, List<string> requestedChannels)
    {
        if (string.Equals(identityType, "email", StringComparison.OrdinalIgnoreCase))
        {
            return requestedChannels.Contains("email") ? "email" : "sms";
        }

        return requestedChannels.Contains("sms") ? "sms" : "email";
    }

    private bool HasSmsConfig()
    {
        var apiKey = _configuration["AfricasTalking:ApiKey"];
        var username = _configuration["AfricasTalking:Username"];

        var hasKey = !string.IsNullOrWhiteSpace(apiKey) && !apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
        var hasUser = !string.IsNullOrWhiteSpace(username) && !username.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

        return hasKey && hasUser;
    }

    private (bool smtpConfigured, List<string> smtpMissing, bool smsConfigured, List<string> smsMissing) GetNotificationProviderConfigStatus()
    {
        var smtpMissing = new List<string>();
        var smsMissing = new List<string>();

        var smtpHost = _configuration["EmailSettings:Host"] ?? _configuration["EmailSettings:SmtpHost"] ?? _configuration["Smtp:Host"];
        var smtpPort = _configuration["EmailSettings:Port"] ?? _configuration["EmailSettings:SmtpPort"] ?? _configuration["Smtp:Port"];
        var smtpUser = _configuration["EmailSettings:Username"] ?? _configuration["EmailSettings:GmailUsername"] ?? _configuration["Smtp:Username"];
        var smtpFrom = _configuration["EmailSettings:FromAddress"] ?? _configuration["Smtp:FromEmail"] ?? _configuration["SendGrid:FromEmail"];

        if (string.IsNullOrWhiteSpace(smtpHost) || smtpHost.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smtpMissing.Add("smtp_host");
        if (string.IsNullOrWhiteSpace(smtpPort) || smtpPort.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smtpMissing.Add("smtp_port");
        if (string.IsNullOrWhiteSpace(smtpUser) || smtpUser.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smtpMissing.Add("smtp_user");
        if (string.IsNullOrWhiteSpace(smtpFrom) || smtpFrom.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smtpMissing.Add("smtp_from");

        var smsApiKey = _configuration["AfricasTalking:ApiKey"];
        var smsUsername = _configuration["AfricasTalking:Username"];
        var smsSenderId = _configuration["AfricasTalking:SenderId"];
        var smsTemplateApproved = _configuration["AfricasTalking:TemplateApproved"];

        if (string.IsNullOrWhiteSpace(smsApiKey) || smsApiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smsMissing.Add("sms_api_key");
        if (string.IsNullOrWhiteSpace(smsUsername) || smsUsername.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smsMissing.Add("sms_username");
        if (string.IsNullOrWhiteSpace(smsSenderId) || smsSenderId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) smsMissing.Add("sms_sender_id");
        if (string.IsNullOrWhiteSpace(smsTemplateApproved)) smsMissing.Add("sms_template_approval_unknown");

        var smtpConfigured = smtpMissing.Count == 0;
        var smsConfigured = smsMissing.Count == 0;
        return (smtpConfigured, smtpMissing, smsConfigured, smsMissing);
    }

    private sealed class NotificationAttemptResult
    {
        public string Channel { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public bool Sent { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? FailureReason { get; set; }
    }

    private sealed class SystemUserProfileSnapshot
    {
        public string? FullName { get; set; }
        public string? NationalId { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? KraPin { get; set; }
        public string? Gender { get; set; }
        public string? PostalAddress { get; set; }
        public string? PhysicalAddress { get; set; }
        public string? IprsReference { get; set; }
        public bool IprsVerified { get; set; }
        public string? AlternativePhone { get; set; }
        public string? PlaceOfWork { get; set; }
        public string? WorkTelephone { get; set; }
        public string? WorkPhysicalAddress { get; set; }
        public string? ProfilePhotoPath { get; set; }
        public string? ClientSignature { get; set; }
        public DateTime? ClientSignDate { get; set; }
    }

    private sealed record ResolvedIdentity(string Type, string Value);

    private sealed class OtpMetadata
    {
        public string? IdentityType { get; set; }
        public string? IdentityValueNormalized { get; set; }
        public bool VerifiedForReset { get; set; }
        public string? CreatedAtUtc { get; set; }
        public string? VerifiedAtUtc { get; set; }
        public string? ConsumedAtUtc { get; set; }
    }
}
