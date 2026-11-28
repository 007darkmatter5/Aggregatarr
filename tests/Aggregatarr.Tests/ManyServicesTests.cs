using System.Net;
using System.Text.RegularExpressions;
using Aggregatarr.Models;
using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregatarr.Tests;

public sealed class ManyServicesFactory : WebApplicationFactory<Program>
{
    public string ConfigDirectory { get; } = Path.Combine(Path.GetTempPath(), "aggregatarr-many", Guid.NewGuid().ToString("N"));

    public ManyServicesFactory() => Directory.CreateDirectory(ConfigDirectory);

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("Aggregatarr:ConfigPath", ConfigDirectory);
        builder.UseSetting("Aggregatarr:DemoMode", "false");
        builder.UseSetting("Aggregatarr:PollIntervalSeconds", "1");
        builder.UseSetting("Aggregatarr:ServiceTimeoutSeconds", "1");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(ConfigDirectory))
            Directory.Delete(ConfigDirectory, recursive: true);
    }
}

public sealed class ManyServicesTests : IClassFixture<ManyServicesFactory>
{
    private readonly ManyServicesFactory _factory;

    public ManyServicesTests(ManyServicesFactory factory) => _factory = factory;

    [Fact]
    public async Task Eight_services_including_two_sonarrs_are_stored_and_aggregated()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var setup = await client.GetAsync("/setup");
        var html = await setup.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        var created = await client.PostAsync("/setup", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = "mark",
            ["Password"] = "correct-horse",
            ["Confirm"] = "correct-horse",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var names = new[]
        {
            "Sonarr 4K", "Sonarr Anime", "Radarr", "Lidarr", "Prowlarr", "Bazarr", "qBittorrent", "SABnzbd"
        };

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<ServiceAdmin>();
            for (var i = 0; i < names.Length; i++)
            {
                var kind = i switch
                {
                    0 or 1 => ServiceKind.Sonarr,
                    2 => ServiceKind.Radarr,
                    3 => ServiceKind.Lidarr,
                    4 => ServiceKind.Prowlarr,
                    5 => ServiceKind.Bazarr,
                    6 => ServiceKind.QBittorrent,
                    _ => ServiceKind.Sabnzbd
                };
                var draft = new ServiceDraft
                {
                    Kind = kind,
                    Name = names[i],
                    BaseUrl = $"http://127.0.0.1:{20000 + i}/",
                    Enabled = true,
                    ApiKey = kind.UsesApiKey() ? $"key-{i}-not-real" : null,
                    Username = kind.UsesPassword() ? "qbuser" : null,
                    Password = kind.UsesPassword() ? "qb-password-not-real" : null
                };
                var (ok, error) = await admin.SaveAsync(draft);
                Assert.True(ok, error);
            }

            var listed = await admin.ListAsync();
            Assert.Equal(8, listed.Count);
            Assert.Equal(2, listed.Count(s => s.Kind == ServiceKind.Sonarr));
            Assert.Equal(names.OrderBy(n => n), listed.Select(s => s.Name).OrderBy(n => n));

            var second = new ServiceDraft
            {
                Id = listed.Single(s => s.Name == "Sonarr Anime").Id,
                Kind = ServiceKind.Sonarr,
                Name = "Sonarr Anime",
                BaseUrl = "http://127.0.0.1:20001/",
                ApiKey = "replacement-key-not-real",
                Enabled = true
            };
            var (updated, updateError) = await admin.SaveAsync(second);
            Assert.True(updated, updateError);
            Assert.Equal(8, (await admin.ListAsync()).Count);
        }

        var settings = await client.GetStringAsync("/settings");
        foreach (var name in names)
            Assert.Contains(name, settings);

        var store = _factory.Services.GetRequiredService<DashboardStore>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && store.Current.Services.Count < 8)
            await Task.Delay(200);

        Assert.Equal(8, store.Current.Services.Count);
        Assert.Equal(2, store.Current.Services.Count(s => s.Kind == ServiceKind.Sonarr && s.Name.StartsWith("Sonarr", StringComparison.Ordinal)));
        Assert.Contains(store.Current.Services, s => s.Name == "qBittorrent");
        Assert.Contains(store.Current.Services, s => s.Name == "SABnzbd");
        Assert.Contains(store.Current.Services, s => s.Name == "Bazarr");
        Assert.All(store.Current.Services, s => Assert.False(s.Online));

        var alerts = await client.GetStringAsync("/alerts");
        var queue = await client.GetStringAsync("/queue");
        var calendar = await client.GetStringAsync("/calendar");
        var home = await client.GetStringAsync("/");
        foreach (var name in names)
        {
            Assert.Contains(name, alerts);
            Assert.Contains(name, home);
        }

        Assert.Contains("qBittorrent", queue);
        Assert.Contains("SABnzbd", queue);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/calendar")).StatusCode);
        Assert.DoesNotContain("key-0-not-real", settings + home + alerts + queue + calendar);
        Assert.DoesNotContain("qb-password-not-real", settings + home + alerts + queue + calendar);
    }
}
