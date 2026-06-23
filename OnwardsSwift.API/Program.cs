using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.FileProviders;
using OnwardsSwift.API.MobileLocal.Configuration;
using OnwardsSwift.API.MobileLocal.Services;
using OnwardsSwift.API.SignupWizard;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;
using QuestPDF.Infrastructure;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// QuestPDF license configuration (required by newer QuestPDF versions).
QuestPDF.Settings.License = LicenseType.Community;

// ── MVC ───────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

builder.Services.Configure<LocalApiOptions>(builder.Configuration.GetSection("LocalApi"));

var jwtKey = builder.Configuration["Jwt:Key"] ?? "OnwardsSwift_Local_Dev_Key_Replace_Immediately";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "OnwardsSwift";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OnwardsSwiftClients";

// ── Cookie + Bearer Authentication ───────────────────────────
builder.Services.AddAuthentication("Smart")
    .AddPolicyScheme("Smart", "Cookie or Bearer", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return JwtBearerDefaults.AuthenticationScheme;

            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.LoginPath        = "/Account/Login";
        o.LogoutPath       = "/Account/Logout";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan   = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.Name      = "OnwardsSwift.Auth";
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<SessionTokenValidator>();
                if (!await validator.IsTokenActiveAsync(context.Principal!))
                {
                    context.Fail("Session is revoked or expired.");
                }
            }
        };
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
builder.Services.AddSingleton<LocalSqliteContext>();
builder.Services.AddSingleton<LocalSchemaBootstrapper>();
builder.Services.AddSingleton<SessionTokenValidator>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<TokenService>();

// ── HTTP Client for External APIs ─────────────────────────────
builder.Services.AddHttpClient<IIprsService, IprsService>()
    .ConfigureHttpClient((sp, client) =>
    {
        var timeout = sp.GetRequiredService<IConfiguration>()["Iprs:TimeoutSeconds"];
        if (int.TryParse(timeout, out var seconds))
        {
            client.Timeout = TimeSpan.FromSeconds(seconds);
        }
        else
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        }
    });

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
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<OnwardsSwift.Infrastructure.Services.ApplicationErrorLogger>();
builder.Services.AddScoped<OnwardsSwift.Core.Interfaces.INotificationService, OnwardsSwift.Infrastructure.Services.NotificationService>();
builder.Services.AddHostedService<NotificationOutboxDispatcher>();

// ── Build ─────────────────────────────────────────────────────
var app = builder.Build();

// ── DB connectivity check ─────────────────────────────────────
// Each step is independently guarded: a transient DB hiccup during startup
// (app pool recycle, network blip) must not crash the whole app (HTTP 500.30).
// Schema/seed calls are idempotent, so skipping one on failure just means it
// retries on the next app start.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var ctx = scope.ServiceProvider.GetRequiredService<DapperContext>();
        logger.LogInformation("Database: {Status}", await ctx.CanConnectAsync() ? "OK ✓" : "FAILED ✗");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database connectivity check failed at startup.");
    }

    try
    {
        var localSchema = scope.ServiceProvider.GetRequiredService<LocalSchemaBootstrapper>();
        await localSchema.EnsureReadyAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Local SQLite schema bootstrap failed at startup.");
    }

    try
    {
        var permissions = scope.ServiceProvider.GetRequiredService<PermissionService>();
        await permissions.EnsureSchemaAndSeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Permission schema/seed failed at startup.");
    }

    try
    {
        var errorLogger = scope.ServiceProvider.GetRequiredService<OnwardsSwift.Infrastructure.Services.ApplicationErrorLogger>();
        await errorLogger.EnsureSchemaAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error logger schema bootstrap failed at startup.");
    }
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

// ── Path base (subdirectory hosting: /Onwards) ─────────────────
var pathBase = builder.Configuration["PathBase"] ?? "";
if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

var uploadsRootSetting = builder.Configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
    ? uploadsRootSetting
    : Path.Combine(app.Environment.ContentRootPath, uploadsRootSetting);

app.UseStaticFiles();

try
{
    Directory.CreateDirectory(uploadsRootPath);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsRootPath),
        RequestPath = "/uploads"
    });
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Could not initialize uploads root at {UploadsRoot}. App will start, but /uploads static files are disabled.", uploadsRootPath);
}

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");
app.MapControllers();

app.Run();
