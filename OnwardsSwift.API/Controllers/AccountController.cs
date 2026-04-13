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

namespace OnwardsSwift.API.Controllers
{
    public class AccountController : Controller
    {
        private readonly DapperContext _ctx;
        public AccountController(DapperContext ctx) => _ctx = ctx;

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
    }
}
