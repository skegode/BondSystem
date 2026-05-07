using Microsoft.AspNetCore.Authentication.Cookies;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF license configuration (required by newer QuestPDF versions).
QuestPDF.Settings.License = LicenseType.Community;

// ── MVC ───────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ── Cookie Authentication ─────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath        = "/Account/Login";
        o.LogoutPath       = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan   = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.Name      = "OnwardsSwift.Auth";
    });

builder.Services.AddAuthorization();

// ── Session (for flash messages) ──────────────────────────────
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
});

builder.Services.AddMemoryCache();

// ── Dapper & Business Services ────────────────────────────────
builder.Services.AddSingleton<DapperContext>();
builder.Services.AddScoped<IBidBondService,         BidBondService>();
builder.Services.AddScoped<IInvoiceDiscountService, InvoiceDiscountService>();
builder.Services.AddScoped<IChequeDiscountService,  ChequeDiscountService>();
builder.Services.AddScoped<IClientService,          ClientService>();
builder.Services.AddScoped<ICalculatorService,      CalculatorService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<OnwardsSwift.Core.Interfaces.INotificationService, OnwardsSwift.Infrastructure.Services.NotificationService>();

// ── Build ─────────────────────────────────────────────────────
var app = builder.Build();

// ── DB connectivity check ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var ctx    = scope.ServiceProvider.GetRequiredService<DapperContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Database: {Status}", await ctx.CanConnectAsync() ? "OK ✓" : "FAILED ✗");
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

// ── Path base (subdirectory hosting: /Onwards) ─────────────────
var pathBase = builder.Configuration["PathBase"] ?? "";
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
