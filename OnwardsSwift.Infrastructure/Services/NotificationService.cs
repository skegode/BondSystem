using Dapper;
using Microsoft.Extensions.Configuration;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using System.Text;
using SendGrid;
using SendGrid.Helpers.Mail;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace OnwardsSwift.Infrastructure.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IConfiguration _config;
        private readonly DapperContext _ctx;

        public NotificationService(IConfiguration config, DapperContext ctx)
        {
            _config = config;
            _ctx = ctx;
        }

        public async Task SendSmsAsync(string phone, string message)
        {
            // SMS sending not implemented yet. Could integrate Africa's Talking here later.
            await Task.CompletedTask;
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
                        return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = true, ProviderMessageId = msgId, Attempts = attempts };
                    }
                    catch (MailKit.CommandException cmdEx)
                    {
                        lastEx = cmdEx;
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, cmdEx.Message);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }
                    catch (System.Net.Sockets.SocketException sockEx)
                    {
                        lastEx = sockEx;
                        await SaveNotificationRecord(to, subject, htmlBody ?? plainBody ?? string.Empty, sockEx.Message);
                        if (attempts > maxRetries) break;
                        await Task.Delay(500 * attempts);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
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
                    return new OnwardsSwift.Core.DTOs.EmailSendResult { Accepted = true, ProviderMessageId = msgId, Attempts = attempts };
                }
                catch (Exception ex)
                {
                    lastEx = ex;
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
