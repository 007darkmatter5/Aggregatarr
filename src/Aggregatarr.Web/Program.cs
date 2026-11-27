using System.Security.Claims;
using Aggregatarr;
using Aggregatarr.Connectors;
using MudBlazor.Services;
using Aggregatarr.Models;
using Aggregatarr.Web;
using Aggregatarr.Web.Components;
using Aggregatarr.Web.Data;
using Aggregatarr.Web.Hosting;
using Aggregatarr.Web.Security;
using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MudBlazor;

if (args.Contains("--healthcheck"))
    Environment.Exit(await HealthProbe.RunAsync());

var builder = WebApplication.CreateBuilder(args);

var configPath = ConfigPath.Resolve(builder.Configuration);
Directory.CreateDirectory(configPath);
Directory.CreateDirectory(Path.Combine(configPath, "keys"));

var appOptions = new AppOptions();
builder.Configuration.GetSection("Aggregatarr").Bind(appOptions);
appOptions.ConfigPath = configPath;
if (string.Equals(Environment.GetEnvironmentVariable("AGGREGATARR_DEMO"), "true", StringComparison.OrdinalIgnoreCase)
    || string.Equals(builder.Configuration["Aggregatarr:DemoMode"], "true", StringComparison.OrdinalIgnoreCase))
    appOptions.DemoMode = true;

builder.Services.AddSingleton(appOptions);
builder.Services.AddSingleton<IClock, UtcClock>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(configPath, "keys")))
    .SetApplicationName("Aggregatarr");
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<PasswordService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(configPath, "aggregatarr.db")};Cache=Shared"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.Cookie.Name = "aggregatarr.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorPages();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<ServiceAdmin>();
builder.Services.AddScoped<MediaFetcher>();
builder.Services.AddScoped<IActionService, ActionService>();
builder.Services.AddSingleton<IServiceHttp, ServiceHttp>();
builder.Services.AddSingleton<DashboardStore>();
builder.Services.AddSingleton<DemoWorld>();
builder.Services.AddSingleton<IMediaConnector>(sp => new ServarrConnector(ServarrFlavor.Sonarr, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IMediaConnector>(sp => new ServarrConnector(ServarrFlavor.Radarr, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IMediaConnector>(sp => new ServarrConnector(ServarrFlavor.Lidarr, sp.GetRequiredService<ILoggerFactory>(), sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<IMediaConnector, ProwlarrConnector>();
builder.Services.AddSingleton<IMediaConnector, BazarrConnector>();
builder.Services.AddSingleton<IMediaConnector, QBittorrentConnector>();
builder.Services.AddSingleton<IMediaConnector, SabnzbdConnector>();
builder.Services.AddSingleton<ConnectorCatalog>();

if (appOptions.DemoMode)
{
    builder.Services.AddSingleton<DemoPulseService>();
    builder.Services.AddSingleton<IDashboardRefresh>(sp => sp.GetRequiredService<DemoPulseService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DemoPulseService>());
}
else
{
    builder.Services.AddSingleton<DashboardPoller>();
    builder.Services.AddSingleton<IDashboardRefresh>(sp => sp.GetRequiredService<DashboardPoller>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DashboardPoller>());
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    if (appOptions.DemoMode)
        await DemoSeeder.SeedAsync(scope.ServiceProvider);
}

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error");

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    await next();
});
app.UseStaticFiles();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (IsOpen(path))
    {
        await next();
        return;
    }

    var accounts = context.RequestServices.GetRequiredService<AccountService>();
    var any = await accounts.AnyAsync(context.RequestAborted);
    if (!any && !path.StartsWithSegments("/setup"))
    {
        context.Response.Redirect("/setup");
        return;
    }

    if (any && path.StartsWithSegments("/setup"))
    {
        context.Response.Redirect("/");
        return;
    }

    await next();
});
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Json(new { status = "ok" })).AllowAnonymous();
app.MapGet("/error", () => Results.Problem(title: "Aggregatarr hit an unexpected error.", statusCode: StatusCodes.Status500InternalServerError))
    .AllowAnonymous();
app.MapGet("/demo/poster/{slug}", (string slug) =>
{
    var safe = new string(slug.Where(char.IsLetterOrDigit).Take(24).ToArray());
    if (safe.Length == 0)
        safe = "A";
    var hue = Math.Abs(safe.GetHashCode(StringComparison.Ordinal)) % 360;
    var letter = char.ToUpperInvariant(safe[0]);
    var svg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 300">
          <defs>
            <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0" stop-color="hsl({hue} 48% 42%)"/>
              <stop offset="1" stop-color="hsl({(hue + 28) % 360} 42% 18%)"/>
            </linearGradient>
          </defs>
          <rect width="200" height="300" fill="url(#g)"/>
          <rect x="14" y="14" width="172" height="272" rx="10" fill="none" stroke="rgba(255,255,255,.35)"/>
          <text x="100" y="176" text-anchor="middle" font-family="Georgia, serif" font-size="78" fill="#f7f1e8">{letter}</text>
        </svg>
        """;
    return Results.Content(svg, "image/svg+xml");
}).AllowAnonymous();

app.MapGet("/media/{serviceId:guid}/{**path}", async (Guid serviceId, string path, ServiceAdmin admin, MediaFetcher media, CancellationToken cancellationToken) =>
{
    var connection = await admin.GetConnectionAsync(serviceId, cancellationToken);
    if (connection is null)
        return Results.NotFound();
    var file = await media.FetchAsync(connection, path, cancellationToken);
    return file is null ? Results.NotFound() : Results.File(file.Value.Bytes, file.Value.ContentType);
}).RequireAuthorization();

app.MapRazorPages();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static bool IsOpen(PathString path) =>
    path.StartsWithSegments("/healthz")
    || path.StartsWithSegments("/css")
    || path.StartsWithSegments("/js")
    || path.StartsWithSegments("/_framework")
    || path.StartsWithSegments("/_content")
    || path.StartsWithSegments("/_blazor")
    || path.StartsWithSegments("/icon.svg")
    || path.StartsWithSegments("/favicon");

namespace Aggregatarr.Web
{
    public static class AuthSignIn
    {
        public static Task SignInAsync(HttpContext http, Data.UserAccount user)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }

        public static string LocalReturn(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
                ? returnUrl
                : "/";
    }
}

public partial class Program;

/// <summary>
/// Used by the container HEALTHCHECK. The ASP.NET runtime image has neither curl nor wget,
/// so the probe is a second short-lived process of this same app.
/// </summary>
file static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await http.GetAsync("http://127.0.0.1:8080/healthz");
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && body.Contains("\"status\":\"ok\"", StringComparison.Ordinal) ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
