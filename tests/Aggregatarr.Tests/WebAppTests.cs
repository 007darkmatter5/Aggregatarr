using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aggregatarr.Models;
using Aggregatarr.Web.Data;
using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregatarr.Tests;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "aggregatarr-tests", Guid.NewGuid().ToString("N"));

    public AppFactory()
    {
        Directory.CreateDirectory(ConfigDirectory);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Aggregatarr:ConfigPath", ConfigDirectory);
        builder.UseSetting("Aggregatarr:DemoMode", "false");
        builder.UseSetting("Aggregatarr:ServiceTimeoutSeconds", "2");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ConfigDirectory))
            Directory.Delete(ConfigDirectory, recursive: true);
    }
}

public sealed class WebAppTests : IClassFixture<AppFactory>
{
    public const string Secret = "super-secret-api-key-do-not-leak";
    public const string Password = "super-secret-password-do-not-leak";
    private readonly AppFactory _factory;

    public WebAppTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_is_anonymous_and_only_reports_ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.EnumerateObject().Skip(1).Any());
        Assert.DoesNotContain(Secret, body);
    }

    [Fact]
    public async Task Setup_then_pages_do_not_render_stored_secrets()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var setupPage = await client.GetAsync("/setup");
        Assert.Equal(HttpStatusCode.OK, setupPage.StatusCode);
        var html = await setupPage.Content.ReadAsStringAsync();
        var token = ReadToken(html);

        var created = await client.PostAsync("/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "ada",
            ["Password"] = "correct-horse",
            ["Confirm"] = "correct-horse",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<ServiceAdmin>();
            var (ok, error) = await admin.SaveAsync(new ServiceDraft
            {
                Kind = ServiceKind.Sonarr,
                Name = "Living Room Sonarr",
                BaseUrl = "http://127.0.0.1:9/",
                ApiKey = Secret,
                Enabled = true
            });
            Assert.True(ok, error);

            var (qbitOk, qbitError) = await admin.SaveAsync(new ServiceDraft
            {
                Kind = ServiceKind.QBittorrent,
                Name = "Gluetun qBit",
                BaseUrl = "http://127.0.0.1:10/",
                Username = "demo-user",
                Password = Password,
                Enabled = true
            });
            Assert.True(qbitOk, qbitError);

            var listed = await admin.ListAsync();
            var serialized = JsonSerializer.Serialize(listed);
            Assert.DoesNotContain(Secret, serialized);
            Assert.DoesNotContain(Password, serialized);
            Assert.Contains("HasApiKey", serialized);
            Assert.DoesNotContain("ApiKeyProtected", serialized);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.Services.ToListAsync();
            Assert.All(rows, row =>
            {
                Assert.DoesNotContain(Secret, row.ApiKeyProtected ?? "");
                Assert.DoesNotContain(Password, row.PasswordProtected ?? "");
            });
            Assert.Contains(rows, row => row.ApiKeyProtected is not null && row.ApiKeyProtected != Secret);
        }

        foreach (var path in new[] { "/", "/settings", "/queue", "/calendar", "/activity", "/alerts" })
        {
            var page = await client.GetAsync(path);
            var body = await page.Content.ReadAsStringAsync();
            Assert.True(page.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)page.StatusCode}: {body[..Math.Min(body.Length, 1500)]}");
            Assert.DoesNotContain(Secret, body);
            Assert.DoesNotContain(Password, body);
        }
    }

    private static string ReadToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"", RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Antiforgery token was not rendered.");
        return match.Groups[1].Value;
    }
}
