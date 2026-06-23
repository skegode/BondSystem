using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using System.Text;
using SendGrid;
using SendGrid.Helpers.Mail;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace OnwardsSwift.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IConfiguration _config;
        private readonly DapperContext _ctx;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(IConfiguration config, DapperContext ctx, ILogger<NotificationService> logger)
        {
            _config = config;
            _ctx = ctx;
            _logger = logger;
        }

        public async Task SendSmsAsync(string phone, string message)
        {
            if (string.IsNullOrWhiteSpace(phone))
                throw new ArgumentException("Phone number is required.", nameof(phone));

            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("SMS message is required.", nameof(message));

            // Every OTP/signup SMS this method handles is also recorded into the shared
            // Notifications.dbo.SMS table (used by other in-house systems) regardless of
            // whether the direct Africa's Talking call below succeeds -- so the message is
            // never lost to view even if the provider rejects it.
            var sent = false;
            try
            {
                var apiKey = _config["AfricasTalking:ApiKey"];
                var username = _config["AfricasTalking:Username"];
                var senderId = _config["AfricasTalking:SenderId"];
                var templateApproved = _config["AfricasTalking:TemplateApproved"];

                if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("AfricasTalking:ApiKey is not configured.");

                if (string.IsNullOrWhiteSpace(username) || username.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("AfricasTalking:Username is not configured.");

                // Sending without a registered Sender ID can return "Success" from Africa's
                // Talking while the carrier silently drops the message (confirmed by a real
                // test send) -- so both required before this method will even attempt a send,
                // regardless of which caller invokes it (signup, OTP dispatcher, etc.).
                if (string.IsNullOrWhiteSpace(senderId) || senderId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("AfricasTalking:SenderId is not configured -- messages sent without an approved Sender ID are not reliably delivered.");

                if (string.IsNullOrWhiteSpace(templateApproved) || !string.Equals(templateApproved, "true", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("AfricasTalking:TemplateApproved must be set to \"true\" once the Sender ID/template is confirmed approved with Africa's Talking.");

                var normalizedPhone = NormalizePhoneNumber(phone);
                if (string.IsNullOrWhiteSpace(normalizedPhone))
                    throw new InvalidOperationException($"Invalid SMS destination phone number: {phone}");

                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.africastalking.com/version1/messaging");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                request.Headers.TryAddWithoutValidation("apiKey", apiKey);

                var payload = new Dictionary<string, string>
                {
                    ["username"] = username,
                    ["to"] = normalizedPhone,
                    ["message"] = message
                };

                if (!string.IsNullOrWhiteSpace(senderId) && !senderId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
                {
                    payload["from"] = senderId;
                }

                request.Content = new FormUrlEncodedContent(payload);

                using var response = await client.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    var reason = Truncate(responseBody, 500);
                    _logger.LogWarning("SMS provider HTTP failure. Status={Status}. Body={Body}", (int)response.StatusCode, reason);
                    throw new InvalidOperationException($"SMS provider rejected request ({(int)response.StatusCode}): {reason}");
                }

                _logger.LogInformation("SMS provider raw response for {Phone}: {Body}", normalizedPhone, Truncate(responseBody, 1000));

                if (!IsSmsProviderResponseSuccessful(responseBody, out var providerReason))
                {
                    var reason = Truncate(providerReason ?? responseBody, 500);
                    _logger.LogWarning("SMS provider logical failure for {Phone}. Reason={Reason}", normalizedPhone, reason);
                    throw new InvalidOperationException($"SMS provider did not accept message: {reason}");
                }

                _logger.LogInformation("SMS accepted by provider for {Phone}", normalizedPhone);
                sent = true;
            }
            finally
            {
                await DumpSmsToNotificationsDbAsync(phone, message, sent);
            }
        }

        // OnwardsSwift.SendSmsAsync is only ever used for OTP (sign-in / forgot-PIN) and signup
        // credential messages -- shared EntityId for OnwardsSwift in this cross-application table
        // is 24. Failures here must never break the actual SMS flow, so they're swallowed.
        private async Task DumpSmsToNotificationsDbAsync(string rawPhone, string message, bool sent)
        {
            const int onwardsSwiftEntityId = 24;

            try
            {
                var connectionString = _config.GetConnectionString("NotificationsDb");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return;
                }

                var smsTo = ToNotificationsDbPhoneFormat(rawPhone);

                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                await conn.ExecuteAsync(@"
INSERT INTO dbo.SMS (smsMessage, smsto, EntityId, CreateDate, isSent, DateSend)
VALUES (@Message, @SmsTo, @EntityId, GETDATE(), @IsSent, CASE WHEN @IsSent = 1 THEN GETDATE() ELSE NULL END);",
                    new { Message = message, SmsTo = smsTo, EntityId = onwardsSwiftEntityId, IsSent = sent ? 1 : 0 });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record SMS into shared Notifications.dbo.SMS for {Phone}", rawPhone);
            }
        }

        // Matches the existing convention in Notifications.dbo.SMS: digits only, with the 254
        // country code, no leading '+'.
        private static string ToNotificationsDbPhoneFormat(string rawPhone)
        {
            var digits = new string(rawPhone.Where(char.IsDigit).ToArray());

            if (digits.Length == 9)
                return $"254{digits}";

            if (digits.Length == 10 && digits.StartsWith("0", StringComparison.Ordinal))
                return $"254{digits.Substring(1)}";

            return digits;
        }

        private static bool IsSmsProviderResponseSuccessful(string? responseBody, out string? reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                reason = "Empty response body from SMS provider.";
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("SMSMessageData", out var data))
                {
                    reason = "Missing SMSMessageData in provider response.";
                    return false;
                }

                if (!data.TryGetProperty("Recipients", out var recipients) || recipients.ValueKind != JsonValueKind.Array)
                {
                    reason = "Missing Recipients array in provider response.";
                    return false;
                }

                var hasSuccess = false;
                string? firstFailure = null;

                foreach (var recipient in recipients.EnumerateArray())
                {
                    var status = recipient.TryGetProperty("status", out var statusValue)
                        ? statusValue.GetString()
                        : null;

                    // AT statusCode: 100=Processed, 101=Sent, 102=Queued are success.
                    // 401=RiskHold, 402=InvalidSenderId, 403=InvalidPhoneNumber,
                    // 404=UnsupportedNumberType, 405=InsufficientBalance, 406=UserInBlacklist,
                    // 407=CouldNotRoute, 500=InternalServerError, 501=GatewayError, 502=RejectedByGateway
                    int? statusCode = null;
                    if (recipient.TryGetProperty("statusCode", out var statusCodeEl) &&
                        statusCodeEl.ValueKind == JsonValueKind.Number)
                    {
                        statusCode = statusCodeEl.GetInt32();
                    }

                    var isSuccess = statusCode.HasValue
                        ? statusCode.Value is 100 or 101 or 102
                        : (!string.IsNullOrWhiteSpace(status) &&
                           (status.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                            status.Contains("sent", StringComparison.OrdinalIgnoreCase) ||
                            status.Contains("queued", StringComparison.OrdinalIgnoreCase)));

                    if (isSuccess)
                    {
                        hasSuccess = true;
                        continue;
                    }

                    if (firstFailure == null)
                    {
                        var number = recipient.TryGetProperty("number", out var numberValue)
                            ? numberValue.GetString()
                            : "unknown";

                        firstFailure = $"number={number}, statusCode={statusCode?.ToString() ?? "n/a"}, status={status ?? "unknown"}";
                    }
                }

                if (!hasSuccess)
                {
                    reason = firstFailure ?? "Provider returned no successful recipient status.";
                    return false;
                }

                return true;
            }
            catch (JsonException ex)
            {
                reason = $"Failed to parse SMS provider response: {ex.Message}";
                return false;
            }
        }

        private static string? NormalizePhoneNumber(string phone)
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return null;

            if (digits.Length == 9)
                return $"+254{digits}";

            if (digits.Length == 10 && digits.StartsWith("0", StringComparison.Ordinal))
                return $"+254{digits.Substring(1)}";

            if (digits.Length == 12 && digits.StartsWith("254", StringComparison.Ordinal))
                return $"+{digits}";

            if (digits.Length >= 10 && phone.Trim().StartsWith("+", StringComparison.Ordinal))
                return $"+{digits}";

            return null;
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value ?? string.Empty;

            return value.Substring(0, maxLength) + "...";
        }

        public async Task<OnwardsSwift.Core.DTOs.EmailSendResult> SendEmailAsync(string to, string subject, string htmlBody, string? plainBody = null)
        {
            // Resolve settings (EmailSettings preferred, then legacy Smtp aliases)
            string? host = _config["EmailSettings:Host"] ?? _config["EmailSettings:SmtpHost"] ?? _config["Smtp:Host"];
            string? portVal = _config["EmailSettings:Port"] ?? _config["EmailSettings:SmtpPort"] ?? _config["Smtp:Port"];
            string? username = _config["EmailSettings:Username"] ?? _config["EmailSettings:GmailUsername"] ?? _config["Smtp:Username"];
            string? password = _config["EmailSettings:Password"] ?? _config["EmailSettings:GmailAppPassword"] ?? _config["Smtp:Password"];
            string? from = _config["EmailSettings:FromAddress"] ?? _config["Smtp:FromEmail"] ?? _config["SendGrid:FromEmail"];
            string? tlsMode = _config["EmailSettings:TlsMode"] ?? _config["Smtp:TlsMode"] ?? _config["Smtp:UseSsl"];

            int port = int.TryParse(portVal, out var p) ? p : 587;
            bool useStartTls = true;
            if (!string.IsNullOrWhiteSpace(tlsMode))
            {
                var m = tlsMode.Trim().ToLowerInvariant();
                useStartTls = m.Contains("starttls") || m == "true" || m == "starttls:true" || m == "starttls";
            }

            // Validate recipient email format
            try { _ = new System.Net.Mail.MailAddress(to); }
            catch (Exception ex)
            {
                return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = false, FailureReason = "Invalid recipient email: " + ex.Message, Attempts = 0 };
            }

            int attempts = 0;
            const int maxRetries = 2; // retry up to 2 times (total attempts = maxRetries + 1)
            Exception? lastEx = null;

            if (!string.IsNullOrWhiteSpace(host))
            {
                var fromName = _config["EmailSettings:FromName"] ?? _config["Smtp:FromName"] ?? _config["SendGrid:FromName"] ?? "Onwards Swift";
                while (attempts <= maxRetries)
                {
                    attempts++;
                    try
                    {
                        var msg = new MimeMessage();
                        msg.From.Add(new MailboxAddress(fromName, from ?? username ?? "noreply@onwardsswift.com"));
                        msg.To.Add(MailboxAddress.Parse(to));
                        msg.Subject = subject;
                        var builder = new BodyBuilder();
                        if (!string.IsNullOrEmpty(htmlBody)) builder.HtmlBody = htmlBody;
                        builder.TextBody = plainBody ?? (htmlBody != null ? System.Text.RegularExpressions.Regex.Replace(htmlBody, "<.*?>", string.Empty) : string.Empty);
                        msg.Body = builder.ToMessageBody();

                        using var client = new SmtpClient();
                        var secureOption = useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
                        await client.ConnectAsync(host, port, secureOption);
                        if (!string.IsNullOrWhiteSpace(username))
                            await client.AuthenticateAsync(username, password ?? string.Empty);
                        await client.SendAsync(msg);
                        await client.DisconnectAsync(true);

                        var msgId = msg.Headers.Contains("Message-Id") ? msg.Headers["Message-Id"] : null;
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, null, true);
                        _logger.LogInformation("SMTP email accepted for {Recipient} via {Host}:{Port}", to, host, port);
                        return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = true, ProviderMessageId = msgId, Attempts = attempts };
                    }
                    catch (MailKit.CommandException cmdEx)
                    {
                        lastEx = cmdEx;
                        _logger.LogWarning(cmdEx, "SMTP command failure for {Recipient} via {Host}:{Port}", to, host, port);
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, cmdEx.Message);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }
                    catch (System.Net.Sockets.SocketException sockEx)
                    {
                        lastEx = sockEx;
                        _logger.LogWarning(sockEx, "SMTP socket failure for {Recipient} via {Host}:{Port}", to, host, port);
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, sockEx.Message);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        _logger.LogWarning(ex, "SMTP generic failure for {Recipient} via {Host}:{Port}", to, host, port);
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, ex.Message);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }
                }
            }

            // SendGrid fallback
            var sendGridKey = _config["SendGrid:ApiKey"];
            if (string.IsNullOrWhiteSpace(sendGridKey))
            {
                _logger.LogWarning("No SMTP or SendGrid configured for email delivery.");
                return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = false, FailureReason = lastEx?.Message ?? "No SMTP or SendGrid configured", Attempts = attempts };
            }

            attempts = 0;
            while (attempts <= maxRetries)
            {
                attempts++;
                try
                {
                    var sg = new SendGridClient(sendGridKey);
                    var fromAddr = _config["SendGrid:FromEmail"] ?? from ?? username ?? "noreply@onwardsswift.com";
                    var fromName = _config["SendGrid:FromName"] ?? _config["Smtp:FromName"] ?? "Onwards Swift";
                    var fromEmail = new EmailAddress(fromAddr, fromName);
                    var msg2 = MailHelper.CreateSingleEmail(fromEmail, new EmailAddress(to), subject, plainBody ?? string.Empty, htmlBody ?? string.Empty);
                    var resp = await sg.SendEmailAsync(msg2);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = await resp.Body.ReadAsStringAsync();
                        _logger.LogWarning("SendGrid rejected email for {Recipient}. Status={Status}. Body={Body}", to, (int)resp.StatusCode, err);
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, err);
                        lastEx = new Exception(err);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }

                    string? msgId = null;
                    try
                    {
                        if (resp.Headers != null && resp.Headers.TryGetValues("X-Message-Id", out var vals))
                            msgId = string.Join(";", vals);
                    }
                    catch { }

                    await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, null, true);
                    _logger.LogInformation("SendGrid accepted email for {Recipient}. MessageId={MessageId}", to, msgId ?? "n/a");
                    return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = true, ProviderMessageId = msgId, Attempts = attempts };
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    _logger.LogWarning(ex, "SendGrid exception while sending to {Recipient}", to);
                    await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, ex.Message);
                    if (attempts > maxRetries) break;
                    await Task.Delay(500 * attempts);
                }
            }

            return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = false, FailureReason = lastEx?.Message, Attempts = attempts };
        }

        private async Task SaveNotificationRecord(string recipient, string subject, string body, string? error = null, bool sent = false)
        {
            try
            {
                using var conn = _ctx.Create();
                await conn.ExecuteAsync(@"
INSERT INTO Notifications (Recipient, Subject, Body, IsSent, SentAt, ErrorMessage, CreatedAt)
VALUES (@Recipient,@Subject,@Body,@IsSent,CASE WHEN @IsSent=1 THEN GETUTCDATE() ELSE NULL END,@Error,GETUTCDATE())",
                    new { Recipient = recipient, Subject = subject, Body = body, IsSent = sent ? 1 : 0, Error = error });
            }
            catch
            {
                // swallow - saving notification should not break flow
            }
        }
    }
}
