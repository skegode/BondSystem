using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.SignupWizard;

public sealed class NotificationOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationOutboxDispatcher> _logger;

    private static readonly SemaphoreSlim OutboxColumnsGate = new(1, 1);
    private static volatile bool _outboxColumnsEnsured;

    private const string EnsureOutboxColumnsSql = @"
IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'destination') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox ADD destination NVARCHAR(256) NULL;
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'template_key') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox ADD template_key NVARCHAR(80) NULL;
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_outbox', 'otp_request_id') IS NULL
BEGIN
    ALTER TABLE dbo.notification_outbox ADD otp_request_id BIGINT NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_dispatch_logs (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        outbox_id BIGINT NULL,
        otp_request_id BIGINT NULL,
        channel NVARCHAR(20) NOT NULL,
        destination NVARCHAR(256) NOT NULL,
        success BIT NOT NULL CONSTRAINT DF_notification_dispatch_logs_success DEFAULT(0),
        status NVARCHAR(20) NOT NULL,
        provider_response NVARCHAR(MAX) NULL,
        error_message NVARCHAR(MAX) NULL,
        created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_notification_dispatch_logs_outbox_id ON dbo.notification_dispatch_logs(outbox_id);
    CREATE INDEX IX_notification_dispatch_logs_otp_request_id ON dbo.notification_dispatch_logs(otp_request_id);
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'success') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs
    ADD success BIT NOT NULL CONSTRAINT DF_notification_dispatch_logs_success DEFAULT(0) WITH VALUES;
END;

IF OBJECT_ID('dbo.MobileOtpRequests', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.MobileOtpRequests', 'dispatch_status') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD dispatch_status NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_DispatchStatus DEFAULT('pending') WITH VALUES;
END;

IF OBJECT_ID('dbo.MobileOtpRequests', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.MobileOtpRequests', 'identifier_type') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD identifier_type NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_IdentifierType DEFAULT('phone') WITH VALUES;
END;

IF OBJECT_ID('dbo.MobileOtpRequests', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.MobileOtpRequests', 'preferred_channel') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD preferred_channel NVARCHAR(20) NOT NULL CONSTRAINT DF_MobileOtpRequests_PreferredChannel DEFAULT('sms') WITH VALUES;
END;

IF OBJECT_ID('dbo.MobileOtpRequests', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.MobileOtpRequests', 'delivery_channels') IS NULL
BEGIN
    ALTER TABLE dbo.MobileOtpRequests
    ADD delivery_channels NVARCHAR(100) NOT NULL CONSTRAINT DF_MobileOtpRequests_DeliveryChannels DEFAULT('sms') WITH VALUES;
END;";

    public NotificationOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Same one-time-per-app-lifetime guard as SignupWizardSchema.EnsureAsync — this DDL
    // was previously re-run on every 10-second dispatch tick.
    private static async Task EnsureOutboxColumnsAsync(System.Data.IDbConnection conn)
    {
        if (_outboxColumnsEnsured) return;

        await OutboxColumnsGate.WaitAsync();
        try
        {
            if (_outboxColumnsEnsured) return;
            await conn.ExecuteAsync(EnsureOutboxColumnsSql);
            _outboxColumnsEnsured = true;
        }
        finally
        {
            OutboxColumnsGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification outbox dispatcher loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dapper = scope.ServiceProvider.GetRequiredService<DapperContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<INotificationService>();

        using var conn = dapper.CreateConnection();
        await conn.OpenAsync(ct);
        await SignupWizardSchema.EnsureAsync(conn);
        await EnsureOutboxColumnsAsync(conn);

        var rows = (await conn.QueryAsync<dynamic>(@"
    SELECT TOP 20 id, wizard_session_id, channel, recipient, destination, template_key, subject, message_body, attempts, max_attempts, otp_request_id
FROM dbo.notification_outbox
WHERE status IN ('pending', 'failed')
  AND next_attempt_at_utc <= SYSUTCDATETIME()
  AND attempts < max_attempts
ORDER BY id ASC;")).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            long id = row.id;
            Guid wizardSessionId = row.wizard_session_id;
            string channel = Convert.ToString(row.channel) ?? string.Empty;
            string recipient = Convert.ToString(row.recipient) ?? string.Empty;
            string destination = Convert.ToString(row.destination) ?? recipient;
            string templateKey = Convert.ToString(row.template_key) ?? string.Empty;
            string subject = Convert.ToString(row.subject) ?? string.Empty;
            string messageBody = Convert.ToString(row.message_body) ?? string.Empty;
            int attempts = Convert.ToInt32(row.attempts);
            int maxAttempts = Convert.ToInt32(row.max_attempts);
            long? otpRequestId = row.otp_request_id is null ? null : Convert.ToInt64(row.otp_request_id);

            await conn.ExecuteAsync(@"
UPDATE dbo.notification_outbox
SET status = 'processing',
    updated_at_utc = SYSUTCDATETIME()
WHERE id = @id
  AND status IN ('pending', 'failed');", new { id });

            try
            {
                if (string.Equals(channel, "email", StringComparison.OrdinalIgnoreCase))
                {
                    var email = await notifier.SendEmailAsync(destination, subject, messageBody, messageBody);
                    if (!email.Accepted)
                    {
                        throw new InvalidOperationException(email.FailureReason ?? "Email not accepted by provider.");
                    }

                    await WriteDispatchLogAsync(conn, id, otpRequestId, channel, destination, "sent",
                        $"template={templateKey}; providerMessageId={email.ProviderMessageId ?? "n/a"}", null);
                }
                else if (string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase))
                {
                    await notifier.SendSmsAsync(destination, messageBody);

                    await WriteDispatchLogAsync(conn, id, otpRequestId, channel, destination, "sent",
                        $"template={templateKey}; providerResponse=accepted", null);
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported outbox channel: {channel}");
                }

                await conn.ExecuteAsync(@"
UPDATE dbo.notification_outbox
SET status = 'sent',
    attempts = @attempts,
    sent_at_utc = SYSUTCDATETIME(),
    updated_at_utc = SYSUTCDATETIME(),
    last_error = NULL
WHERE id = @id;", new { id, attempts = attempts + 1 });

                if (otpRequestId.HasValue)
                {
                    await conn.ExecuteAsync(@"
UPDATE dbo.MobileOtpRequests
SET dispatch_status = 'sent'
WHERE id = @id;", new { id = otpRequestId.Value });
                }

                await conn.ExecuteAsync(@"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.notification_outbox
    WHERE wizard_session_id = @wizardSessionId
      AND status <> 'sent'
)
BEGIN
    UPDATE dbo.signup_wizard_profiles
    SET credentials_dispatched = 1,
        dispatched_channels = (
            SELECT STRING_AGG(channel, ',')
            FROM (
                SELECT DISTINCT channel
                FROM dbo.notification_outbox
                WHERE wizard_session_id = @wizardSessionId
                  AND status = 'sent'
            ) c
        ),
        dispatched_at_utc = COALESCE(dispatched_at_utc, SYSUTCDATETIME()),
        updated_at_utc = SYSUTCDATETIME()
    WHERE wizard_session_id = @wizardSessionId;
END", new { wizardSessionId });
            }
            catch (Exception ex)
            {
                var nextAttempts = attempts + 1;
                var failedFinal = nextAttempts >= maxAttempts;
                var status = failedFinal ? "failed" : "pending";
                var delayMinutes = failedFinal ? 60 : Math.Min(15, Math.Max(1, nextAttempts));

                _logger.LogWarning(ex,
                    "Outbox dispatch failed. Id={OutboxId}, WizardSessionId={WizardSessionId}, Channel={Channel}, Attempt={Attempt}",
                    id,
                    wizardSessionId,
                    channel,
                    nextAttempts);

                await conn.ExecuteAsync(@"
UPDATE dbo.notification_outbox
SET status = @status,
    attempts = @attempts,
    last_error = @lastError,
    next_attempt_at_utc = DATEADD(MINUTE, @delayMinutes, SYSUTCDATETIME()),
    updated_at_utc = SYSUTCDATETIME()
WHERE id = @id;", new
                {
                    id,
                    status,
                    attempts = nextAttempts,
                    lastError = ex.Message,
                    delayMinutes
                });

                if (otpRequestId.HasValue)
                {
                    await conn.ExecuteAsync(@"
UPDATE dbo.MobileOtpRequests
SET dispatch_status = @dispatchStatus
WHERE id = @id;", new
                    {
                        id = otpRequestId.Value,
                        dispatchStatus = failedFinal ? "failed" : "pending"
                    });
                }

                await WriteDispatchLogAsync(conn, id, otpRequestId, channel, destination, "failed", null, ex.Message);
            }
        }
    }

    private static Task WriteDispatchLogAsync(
        System.Data.IDbConnection conn,
        long outboxId,
        long? otpRequestId,
        string channel,
        string destination,
        string status,
        string? providerResponse,
        string? errorMessage)
    {
        return conn.ExecuteAsync(@"
INSERT INTO dbo.notification_dispatch_logs
(
    outbox_id,
    otp_request_id,
    channel,
    destination,
    success,
    status,
    provider_response,
    error_message,
    created_at_utc
)
VALUES
(
    @outboxId,
    CASE
        WHEN @otpRequestId IS NULL THEN NULL
        WHEN OBJECT_ID('dbo.otp_requests', 'U') IS NOT NULL
             AND EXISTS (SELECT 1 FROM dbo.otp_requests WHERE id = @otpRequestId) THEN @otpRequestId
        ELSE NULL
    END,
    @channel,
    @destination,
    @success,
    @status,
    @providerResponse,
    @errorMessage,
    SYSUTCDATETIME()
);", new
        {
            outboxId,
            otpRequestId,
            channel,
            destination,
            success = string.Equals(status, "sent", StringComparison.OrdinalIgnoreCase),
            status,
            providerResponse,
            errorMessage
        });
    }
}
