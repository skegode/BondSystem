using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Infrastructure.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.Extensions.Logging;

namespace OnwardsSwift.API.Controllers
{
    public class AccountController : Controller
    {
        private readonly DapperContext _ctx;
        private readonly OnwardsSwift.Core.Interfaces.INotificationService _notifier;
        private readonly ILogger<AccountController> _logger;
        public AccountController(DapperContext ctx, OnwardsSwift.Core.Interfaces.INotificationService notifier, ILogger<AccountController> logger)
        {
            _ctx = ctx;
            _notifier = notifier;
            _logger = logger;
        }

        // ── Login ─────────────────────────────────────────────
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
           
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginRequest model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid) return View(model);

            using var conn = _ctx.Create();

            // 1. Fetch the user from the database
            // Only allow active, non-deleted users to proceed
            const string sql = @"
        SELECT Id, FullName, Email, Role, PasswordHash 
        FROM SystemUsers 
        WHERE Email = @E AND IsActive = 1 AND IsDeleted = 0";

            var u = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { E = model.Email });

            // 2. Validate Password and User Existence
            if (u != null && (string)u.PasswordHash == Hash(model.Password))
            {
                // Audit Trail: Log the login time
                await conn.ExecuteAsync(
                    "UPDATE SystemUsers SET LastLoginAt =GETDATE() WHERE Id = @Id",
                    new { Id =u.Id }
                );

                string roleName = ((UserRole)(int)u.Role).ToString();

                // 3. Populate Session for UI personalization
                HttpContext.Session.SetInt32("UserId", (int)u.Id);
                HttpContext.Session.SetString("UserName", (string)u.FullName);
                HttpContext.Session.SetString("UserEmail", (string)u.Email);
                HttpContext.Session.SetString("UserRole", roleName);

                // 4. Issue the Authentication Cookie (The Guardrail for [Authorize])
                await SignIn(((int)u.Id).ToString(), (string)u.Email, (string)u.FullName, roleName);

                // 5. Secure Redirect
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Home");
            }

            // 3. Generic error message to prevent account harvesting
            ModelState.AddModelError("", "Invalid email or password.");
            return View(model);
        }

        // ── Logout ────────────────────────────────────────────
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ── Forgot Password ─────────────────────────────────────
        [HttpGet]
        public IActionResult ForgotPassword() => View(new ForgotPasswordRequest());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest model)
        {
            if (!ModelState.IsValid) return View(model);

            using var conn = _ctx.Create();
            await EnsurePasswordResetTableAsync(conn);
            var u = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Email FROM SystemUsers WHERE Email=@E AND IsActive=1 AND IsDeleted=0",
                new { E = model.Email });

            // For security, do not reveal whether the email exists. If user exists,
            // a real implementation would generate a reset token and email it.
            // Here we simply redirect to a confirmation page.

            if (u != null)
            {
                var tokenBytes = RandomNumberGenerator.GetBytes(32);
                var token = Convert.ToBase64String(tokenBytes);
                var encoded = System.Net.WebUtility.UrlEncode(token);
                var tokenHash = HashResetToken(token);

                // Invalidate previous active reset tokens for this email, then persist new token.
                await conn.ExecuteAsync(@"
                    UPDATE PasswordResetTokens
                    SET UsedAt = GETUTCDATE()
                    WHERE Email = @Email AND UsedAt IS NULL AND ExpiresAt > GETUTCDATE();

                    INSERT INTO PasswordResetTokens (Email, TokenHash, ExpiresAt, CreatedAt)
                    VALUES (@Email, @TokenHash, DATEADD(MINUTE, 30, GETUTCDATE()), GETUTCDATE());",
                    new { Email = (string)u.Email, TokenHash = tokenHash });

                var resetUrl = Url.Action("ResetPassword", "Account", new { email = model.Email, token = encoded }, Request.Scheme ?? "https");

                var subject = "Reset your Onwards Swift password";
                var htmlBody = $"<p>Hello,</p><p>We received a request to reset your password. Click the link below to reset it:</p><p><a href=\"{resetUrl}\">Reset password</a></p><p>If you didn't request this, you can ignore this message.</p><p>— Onwards Swift</p>";
                var plainBody = $"Hello,\n\nWe received a request to reset your password. Use this link to reset it: {resetUrl}\n\nIf you didn't request this, you can ignore this message.\n\n— Onwards Swift";

                var sendResult = await _notifier.SendEmailAsync((string)u.Email, subject, htmlBody, plainBody);
                if (!sendResult.Accepted)
                {
                    _logger.LogWarning("ForgotPassword email send failed for {Email}. Attempts={Attempts}. Reason={Reason}",
                        (string)u.Email,
                        sendResult.Attempts,
                        sendResult.FailureReason ?? "unknown");
                }
                else
                {
                    _logger.LogInformation("ForgotPassword email accepted for {Email}. Attempts={Attempts}. ProviderMessageId={MessageId}",
                        (string)u.Email,
                        sendResult.Attempts,
                        sendResult.ProviderMessageId ?? "n/a");
                }
            }

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation() => View();

        // ── Reset Password via Token ─────────────────────────
        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["Error"] = "Invalid password reset link.";
                return RedirectToAction(nameof(Login));
            }

            var model = new ResetPasswordViaTokenRequest
            {
                Email = email,
                ResetToken = token
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViaTokenRequest model)
        {
            if (!ModelState.IsValid) return View(model);

            using var conn = _ctx.Create();
            await EnsurePasswordResetTableAsync(conn);

            var u = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, Email FROM SystemUsers WHERE Email=@E AND IsActive=1 AND IsDeleted=0",
                new { E = model.Email });

            if (u == null)
            {
                ModelState.AddModelError("", "Invalid or expired reset link.");
                return View(model);
            }

            var decoded = WebUtility.UrlDecode(model.ResetToken);
            var tokenHash = HashResetToken(decoded);

            var tokenRow = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT TOP 1 Id
                FROM PasswordResetTokens
                WHERE Email = @Email
                  AND TokenHash = @TokenHash
                  AND UsedAt IS NULL
                  AND ExpiresAt > GETUTCDATE()
                ORDER BY CreatedAt DESC",
                new { Email = model.Email, TokenHash = tokenHash });

            if (tokenRow == null)
            {
                ModelState.AddModelError("", "Invalid or expired reset link.");
                return View(model);
            }

            await conn.ExecuteAsync(@"
                UPDATE SystemUsers
                SET PasswordHash = @H, UpdatedAt = GETUTCDATE()
                WHERE Email = @Email AND IsActive = 1 AND IsDeleted = 0;",
                new { H = Hash(model.NewPassword), Email = model.Email });

            await conn.ExecuteAsync(
                "UPDATE PasswordResetTokens SET UsedAt = GETUTCDATE() WHERE Id = @Id",
                new { Id = (int)tokenRow.Id });

            return RedirectToAction(nameof(ResetPasswordConfirmation));
        }

        [HttpGet]
        public IActionResult ResetPasswordConfirmation() => View();

        // ── Change Password ───────────────────────────────────
        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View(new ChangePasswordRequest());

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest model)
        {
            if (!ModelState.IsValid) return View(model);
            var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
            using var conn = _ctx.Create();
            var u = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, PasswordHash FROM SystemUsers WHERE Email=@E AND IsActive=1 AND IsDeleted=0",
                new { E = email });
            if (u == null || (string)u.PasswordHash != Hash(model.CurrentPassword))
            {
                ModelState.AddModelError("", "Current password is incorrect.");
                return View(model);
            }
            await conn.ExecuteAsync("UPDATE SystemUsers SET PasswordHash=@H,UpdatedAt=GETUTCDATE() WHERE Id=@Id",
                new { H = Hash(model.NewPassword), Id = (Guid)u.Id });
            TempData["Success"] = "Password changed successfully.";
            return RedirectToAction("Index", "Home");
        }

        // ── Helpers ───────────────────────────────────────────
        private async Task SignIn(string id, string email, string fullName, string role)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, id),
                new(ClaimTypes.Email,          email),
                new(ClaimTypes.Name,           fullName),
                new(ClaimTypes.Role,           role),
                new("role",                    role)
            };
            var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTime.UtcNow.AddHours(8) });
        }

        private static string Hash(string pw)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(pw + "OnwardsSwiftSalt2026")));
        }

        private static string HashResetToken(string token)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
        }

        private static async Task EnsurePasswordResetTableAsync(System.Data.IDbConnection conn)
        {
            await conn.ExecuteAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PasswordResetTokens' AND xtype='U')
                BEGIN
                    CREATE TABLE PasswordResetTokens (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Email NVARCHAR(255) NOT NULL,
                        TokenHash NVARCHAR(88) NOT NULL,
                        ExpiresAt DATETIME2 NOT NULL,
                        UsedAt DATETIME2 NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                    );

                    CREATE INDEX IX_PasswordResetTokens_Email ON PasswordResetTokens (Email);
                    CREATE INDEX IX_PasswordResetTokens_ExpiresAt ON PasswordResetTokens (ExpiresAt);
                END");
        }
    }
}
