using System.Net;
using System.Text.RegularExpressions;
using Aggregatarr.Web.Services;
using Aggregatarr.Web.Theme;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregatarr.Tests;

public sealed class ThemeFactory : WebApplicationFactory<Program>
{
    public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "aggregatarr-theme", Guid.NewGuid().ToString("N"));

    public ThemeFactory() => Directory.CreateDirectory(ConfigDirectory);

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Aggregatarr:ConfigPath", ConfigDirectory);
        builder.UseSetting("Aggregatarr:DemoMode", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ConfigDirectory))
            Directory.Delete(ConfigDirectory, recursive: true);
    }
}

public sealed class ThemeTests : IClassFixture<ThemeFactory>
{
    private readonly ThemeFactory _factory;

    public ThemeTests(ThemeFactory factory) => _factory = factory;

    [Fact]
    public async Task Theme_defaults_to_dark_and_lcars_is_stored_for_the_next_request()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var themes = scope.ServiceProvider.GetRequiredService<ThemeSettings>();
            Assert.Equal(ThemeChoice.Dark, await themes.GetAsync());
            await themes.SetAsync("not-a-theme");
            Assert.Equal(ThemeChoice.Dark, await themes.GetAsync());
            await themes.SetAsync(ThemeChoice.Lcars);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var themes = scope.ServiceProvider.GetRequiredService<ThemeSettings>();
            Assert.Equal(ThemeChoice.Lcars, await themes.GetAsync());
            var db = scope.ServiceProvider.GetRequiredService<Aggregatarr.Web.Data.AppDbContext>();
            Assert.Equal(ThemeChoice.Lcars, await db.Settings.Where(s => s.Key == ThemeChoice.Key).Select(s => s.Value).SingleAsync());
        }

        Assert.Equal(ThemeChoice.Lcars, _factory.Services.GetRequiredService<ThemePreference>().Current);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var setupPage = await client.GetAsync("/setup");
        var setupHtml = await setupPage.Content.ReadAsStringAsync();
        Assert.Contains("data-theme=\"lcars\"", setupHtml, StringComparison.Ordinal);
        var token = Regex.Match(setupHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        var created = await client.PostAsync("/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "theme",
            ["Password"] = "correct-horse",
            ["Confirm"] = "correct-horse",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var settings = await client.GetStringAsync("/settings");
        Assert.Contains("LCARS", settings, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"lcars\"", settings, StringComparison.Ordinal);
    }
}
