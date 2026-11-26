using System.Net;
using Aggregatarr.Connectors;
using Aggregatarr.Models;

namespace Aggregatarr.Tests.Connectors;

public sealed class SonarrConnectorTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Poll_reads_queue_calendar_history_and_health()
    {
        using var harness = ArrHarness("sonarr");
        var connector = new ServarrConnector(ServarrFlavor.Sonarr, harness.Factory, Clock);

        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);

        Assert.Equal("4.0.15.2942", snapshot.Version);
        Assert.Contains(snapshot.Warnings, w => w.Level == "warning" && w.Message.Contains("RSS sync", StringComparison.Ordinal));
        var item = Assert.Single(snapshot.Queue);
        Assert.Equal("The Glass Harbor", item.Title);
        Assert.Equal("S02E04 · Salt Line", item.Subtitle);
        Assert.Equal("8c212779b4abde7c6bc608063a0d008b7e40ce32", item.DownloadId);
        Assert.Equal("144", item.ItemId);
        Assert.Equal(QueueSeverity.Stuck, item.Severity);
        Assert.Contains("Import blocked", item.StatusDetail, StringComparison.Ordinal);
        Assert.True(item.Progress is > 0.7 and < 0.8);

        Assert.Equal(2, snapshot.Calendar.Count);
        Assert.Equal("The Glass Harbor", snapshot.Calendar[0].Title);
        Assert.Equal("S02E04", snapshot.Calendar[0].Detail);
        Assert.False(snapshot.Calendar[0].HasFile);
        Assert.Equal("/MediaCover/12/poster.jpg".TrimStart('/'), snapshot.Calendar[0].PosterPath);
        Assert.Equal(88, snapshot.Calendar[0].SearchId);

        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Grab && a.Title == "The Glass Harbor");
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Failure);
        Assert.All(harness.Handler.Calls, call =>
        {
            Assert.Equal(ConnectorHarness.ApiKey, call.Headers["X-Api-Key"]);
            Assert.DoesNotContain(ConnectorHarness.ApiKey, call.Path, StringComparison.Ordinal);
        });
        harness.AssertNoSecrets();
    }

    [Fact]
    public async Task Search_posts_episode_command_without_putting_the_key_in_the_url()
    {
        using var harness = ArrHarness("sonarr");
        var connector = new ServarrConnector(ServarrFlavor.Sonarr, harness.Factory, Clock);

        await connector.ExecuteAsync(harness.Context(), new SearchIdsAction([88]), CancellationToken.None);
        await connector.ExecuteAsync(harness.Context(), new SearchAllMissingAction(), CancellationToken.None);

        var bodies = harness.Handler.Calls.Where(c => c.Path.Contains("/command", StringComparison.Ordinal)).Select(c => c.Body).ToList();
        Assert.Contains(bodies, b => b is not null && b.Contains("EpisodeSearch", StringComparison.Ordinal) && b.Contains("88", StringComparison.Ordinal));
        Assert.Contains(bodies, b => b is not null && b.Contains("MissingEpisodeSearch", StringComparison.Ordinal));
        Assert.All(harness.Handler.Calls, c => Assert.DoesNotContain(ConnectorHarness.ApiKey, c.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Error_response_does_not_echo_the_api_key()
    {
        using var harness = new ConnectorHarness((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent($"{{\"message\":\"bad {ConnectorHarness.ApiKey}\"}}")
            });
        var connector = new ServarrConnector(ServarrFlavor.Sonarr, harness.Factory, Clock);
        var result = await connector.TestAsync(harness.Context(), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.DoesNotContain(ConnectorHarness.ApiKey, result.Error);
        harness.AssertNoSecrets();
    }

    private static ConnectorHarness ArrHarness(string name) => new((request, _) =>
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path.Contains("/system/status", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load($"{name}-status.json"));
        if (path.Contains("/health", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load($"{name}-health.json"));
        if (path.Contains("/queue/", StringComparison.Ordinal))
            return HttpJson.Json("{}");
        if (path.Contains("/queue", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load($"{name}-queue.json"));
        if (path.Contains("/calendar", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load($"{name}-calendar.json"));
        if (path.Contains("/history", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load($"{name}-history.json"));
        if (path.Contains("/command", StringComparison.Ordinal))
            return HttpJson.Json("{\"name\":\"ok\"}");
        return HttpJson.Json("{}", HttpStatusCode.NotFound);
    });
}

public sealed class RadarrConnectorTests
{
    [Fact]
    public async Task Poll_maps_movie_queue_and_release_date()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        using var harness = new ConnectorHarness((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/system/status")) return HttpJson.Json(Fixtures.Load("radarr-status.json"));
            if (path.Contains("/health")) return HttpJson.Json(Fixtures.Load("radarr-health.json"));
            if (path.Contains("/queue")) return HttpJson.Json(Fixtures.Load("radarr-queue.json"));
            if (path.Contains("/calendar")) return HttpJson.Json(Fixtures.Load("radarr-calendar.json"));
            if (path.Contains("/history")) return HttpJson.Json(Fixtures.Load("radarr-history.json"));
            return HttpJson.Json("{}");
        });
        var connector = new ServarrConnector(ServarrFlavor.Radarr, harness.Factory, clock);
        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);

        Assert.Equal("5.18.4.9674", snapshot.Version);
        var movie = Assert.Single(snapshot.Queue);
        Assert.Equal("Northline", movie.Title);
        Assert.Equal("2026", movie.Subtitle);
        Assert.Equal("NORTHLINEHASH00000000000000000001", movie.DownloadId);
        var release = Assert.Single(snapshot.Calendar);
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 0, 0, 0, TimeSpan.Zero), release.When);
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Import);
        await connector.ExecuteAsync(harness.Context(), new SearchIdsAction([21]), CancellationToken.None);
        Assert.Contains(harness.Handler.Calls, c => c.Body is not null && c.Body.Contains("MoviesSearch", StringComparison.Ordinal) && c.Body.Contains("movieIds", StringComparison.Ordinal));
        harness.AssertNoSecrets();
    }
}

public sealed class LidarrConnectorTests
{
    [Fact]
    public async Task Poll_maps_album_queue_and_health_error()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));
        using var harness = new ConnectorHarness((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/system/status")) return HttpJson.Json(Fixtures.Load("lidarr-status.json"));
            if (path.Contains("/health")) return HttpJson.Json(Fixtures.Load("lidarr-health.json"));
            if (path.Contains("/queue")) return HttpJson.Json(Fixtures.Load("lidarr-queue.json"));
            if (path.Contains("/calendar")) return HttpJson.Json(Fixtures.Load("lidarr-calendar.json"));
            if (path.Contains("/history")) return HttpJson.Json(Fixtures.Load("lidarr-history.json"));
            if (path.Contains("/command")) return HttpJson.Json("{\"name\":\"ok\"}");
            return HttpJson.Json("{}");
        });
        var connector = new ServarrConnector(ServarrFlavor.Lidarr, harness.Factory, clock);
        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);

        Assert.Equal("2.11.2.4629", snapshot.Version);
        Assert.Contains(snapshot.Warnings, w => w.Level == "error" && w.Message.Contains("qBittorrent", StringComparison.Ordinal));
        var album = Assert.Single(snapshot.Queue);
        Assert.Equal("Northline", album.Title);
        Assert.Equal("Paper Weather", album.Subtitle);
        var release = Assert.Single(snapshot.Calendar);
        Assert.Equal("Northline", release.Title);
        Assert.False(release.HasFile);
        Assert.Equal(40, release.SearchId);
        await connector.ExecuteAsync(harness.Context(), new SearchAllMissingAction(), CancellationToken.None);
        Assert.Contains(harness.Handler.Calls, c => c.Body is not null && c.Body.Contains("MissingAlbumSearch", StringComparison.Ordinal));
        Assert.Contains(harness.Handler.Calls, c => c.Path.StartsWith("/api/v1/", StringComparison.Ordinal) || c.Path.Contains("api/v1/", StringComparison.Ordinal));
        harness.AssertNoSecrets();
    }
}

public sealed class ProwlarrConnectorTests
{
    [Fact]
    public async Task Poll_reads_status_health_and_grabs()
    {
        using var harness = new ConnectorHarness((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/system/status")) return HttpJson.Json(Fixtures.Load("prowlarr-status.json"));
            if (path.Contains("/health")) return HttpJson.Json(Fixtures.Load("prowlarr-health.json"));
            if (path.Contains("/history")) return HttpJson.Json(Fixtures.Load("prowlarr-history.json"));
            return HttpJson.Json("{}");
        });
        var connector = new ProwlarrConnector(harness.Factory);
        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);
        Assert.Equal("1.31.2.4978", snapshot.Version);
        Assert.Contains(snapshot.Warnings, w => w.Message.Contains("Indexers are unavailable", StringComparison.Ordinal));
        var grab = Assert.Single(snapshot.Activity);
        Assert.Equal(ActivityKind.Grab, grab.Kind);
        Assert.Equal("Glass.Harbor.S02E04.2160p", grab.Title);
        Assert.Contains("Example Indexer", grab.Detail);
        Assert.All(harness.Handler.Calls, c => Assert.Equal(ConnectorHarness.ApiKey, c.Headers["X-Api-Key"]));
        harness.AssertNoSecrets();
    }
}

public sealed class BazarrConnectorTests
{
    [Fact]
    public async Task Poll_reads_wrapped_status_health_and_history()
    {
        using var harness = new ConnectorHarness((request, _) =>
        {
            var path = request.RequestUri?.PathAndQuery ?? "";
            if (path.Contains("/system/status")) return HttpJson.Json(Fixtures.Load("bazarr-status.json"));
            if (path.Contains("/system/health")) return HttpJson.Json(Fixtures.Load("bazarr-health.json"));
            if (path.Contains("/episodes/history")) return HttpJson.Json(Fixtures.Load("bazarr-episodes-history.json"));
            if (path.Contains("/movies/history")) return HttpJson.Json(Fixtures.Load("bazarr-movies-history.json"));
            return HttpJson.Json("{}");
        });
        var connector = new BazarrConnector(harness.Factory);
        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);
        Assert.Equal("1.5.1", snapshot.Version);
        Assert.Contains(snapshot.Warnings, w => w.Level == "error" && w.Message.Contains("Sonarr connection failed", StringComparison.Ordinal));
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Subtitle && a.Title == "The Glass Harbor" && a.Detail!.Contains("English", StringComparison.Ordinal));
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Failure && a.Title == "Meridian Dogs");
        Assert.All(harness.Handler.Calls, c =>
        {
            Assert.Equal(ConnectorHarness.ApiKey, c.Headers["X-API-KEY"]);
            Assert.DoesNotContain(ConnectorHarness.ApiKey, c.Path, StringComparison.Ordinal);
        });
        harness.AssertNoSecrets();
    }
}

public sealed class QBittorrentConnectorTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 26, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Poll_keeps_active_downloads_and_skips_seeding()
    {
        using var harness = QbitHarness(_ => HttpJson.Text("Ok."));
        var connector = new QBittorrentConnector(harness.Factory, Clock);
        var snapshot = await connector.PollAsync(harness.Context(apiKey: null, username: "demo-user", password: ConnectorHarness.Password), PollDepth.Full, CancellationToken.None);

        Assert.Equal("v5.0.2", snapshot.Version);
        Assert.Equal(4, snapshot.Queue.Count);
        Assert.Contains(snapshot.Queue, q => q.State == "Error" && q.Severity == QueueSeverity.Failed);
        Assert.Contains(snapshot.Queue, q => q.State == "Stalled" && q.Severity == QueueSeverity.Stuck);
        Assert.Contains(snapshot.Queue, q => q.State == "Downloading" && q.SpeedBytesPerSecond == 18700000);
        Assert.Contains(snapshot.Queue, q => q.IsPaused);
        Assert.DoesNotContain(snapshot.Queue, q => q.Title.Contains("Seeding", StringComparison.Ordinal));
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Complete && a.Title == "Already.Seeding.iso");
        Assert.Contains(harness.Handler.Calls, c => c.Path.Contains("/auth/login", StringComparison.Ordinal) && c.Body!.Contains("demo-user", StringComparison.Ordinal));
        Assert.All(harness.Handler.Calls, c => Assert.Contains("http://media.example:8989/", c.Headers["Referer"], StringComparison.Ordinal));
        harness.AssertNoSecrets();
    }

    [Fact]
    public async Task Pause_uses_stop_on_webapi_2_11_and_pause_on_older_servers()
    {
        using var modern = QbitHarness(request => request.RequestUri!.PathAndQuery.Contains("/stop", StringComparison.Ordinal)
            ? HttpJson.Text("ok")
            : HttpJson.Text("Ok."));
        var connector = new QBittorrentConnector(modern.Factory, Clock);
        var context = modern.Context(apiKey: null, username: "demo-user", password: ConnectorHarness.Password);
        await connector.ExecuteAsync(context, new PauseItemAction("abc"), CancellationToken.None);
        Assert.Contains(modern.Handler.Calls, c => c.Path.Contains("/torrents/stop", StringComparison.Ordinal) && c.Body!.Contains("hashes=abc", StringComparison.Ordinal));
        Assert.DoesNotContain(modern.Handler.Calls, c => c.Path.Contains("/torrents/pause", StringComparison.Ordinal));
        modern.AssertNoSecrets();

        using var legacy = QbitHarness(request =>
        {
            var path = request.RequestUri!.PathAndQuery;
            if (path.Contains("/stop", StringComparison.Ordinal))
                return HttpJson.Text("missing", HttpStatusCode.NotFound);
            return HttpJson.Text(path.Contains("/auth/login", StringComparison.Ordinal) ? "Ok." : "ok");
        });
        var old = new QBittorrentConnector(legacy.Factory, Clock);
        await old.ExecuteAsync(legacy.Context(apiKey: null, username: "demo-user", password: ConnectorHarness.Password), new PauseAllAction(), CancellationToken.None);
        Assert.Contains(legacy.Handler.Calls, c => c.Path.Contains("/torrents/pause", StringComparison.Ordinal) && c.Body!.Contains("hashes=all", StringComparison.Ordinal));
        legacy.AssertNoSecrets();
    }

    [Fact]
    public async Task Login_failure_does_not_log_the_password()
    {
        using var harness = QbitHarness(_ => HttpJson.Text("Fails."));
        var connector = new QBittorrentConnector(harness.Factory, Clock);
        var result = await connector.TestAsync(harness.Context(apiKey: null, username: "demo-user", password: ConnectorHarness.Password), CancellationToken.None);
        Assert.False(result.Ok);
        Assert.DoesNotContain(ConnectorHarness.Password, result.Error);
        harness.AssertNoSecrets();
    }

    private static ConnectorHarness QbitHarness(Func<HttpRequestMessage, HttpResponseMessage> mutate) => new((request, _) =>
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path.Contains("/auth/login", StringComparison.Ordinal))
            return mutate(request);
        if (path.Contains("/app/version", StringComparison.Ordinal))
            return HttpJson.Text("v5.0.2");
        if (path.Contains("/torrents/info", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load("qbittorrent-torrents.json"));
        if (path.Contains("/torrents/", StringComparison.Ordinal))
            return mutate(request);
        return HttpJson.Text("nope", HttpStatusCode.NotFound);
    });
}

public sealed class SabnzbdConnectorTests
{
    [Fact]
    public async Task Poll_reads_queue_history_and_warnings_with_header_auth()
    {
        using var harness = SabHarness();
        var connector = new SabnzbdConnector(harness.Factory);
        var snapshot = await connector.PollAsync(harness.Context(), PollDepth.Full, CancellationToken.None);

        Assert.Equal("4.4.1", snapshot.Version);
        Assert.False(snapshot.ClientPaused);
        Assert.Equal(2, snapshot.Queue.Count);
        var downloading = snapshot.Queue.Single(q => q.ItemId == "SABnzbd_nzo_loworbit");
        Assert.Equal("Downloading", downloading.State);
        Assert.Equal(0.41, downloading.Progress, 2);
        Assert.True(downloading.SpeedBytesPerSecond > 8_000_000);
        var stuck = snapshot.Queue.Single(q => q.ItemId == "SABnzbd_nzo_saltroad");
        Assert.Equal(QueueSeverity.Stuck, stuck.Severity);
        Assert.True(stuck.IsPaused);
        Assert.Contains(snapshot.Warnings, w => w.Level == "error" && w.Message.Contains("news.example", StringComparison.Ordinal));
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Complete);
        Assert.Contains(snapshot.Activity, a => a.Kind == ActivityKind.Failure && a.Detail == "Not enough repair blocks");
        Assert.All(harness.Handler.Calls, call =>
        {
            Assert.Equal(ConnectorHarness.ApiKey, call.Headers["X-Api-Key"]);
            Assert.DoesNotContain("apikey=", call.Path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ConnectorHarness.ApiKey, call.Path, StringComparison.Ordinal);
        });
        harness.AssertNoSecrets();
    }

    [Fact]
    public async Task Pause_resume_and_delete_use_queue_names()
    {
        using var harness = SabHarness();
        var connector = new SabnzbdConnector(harness.Factory);
        var context = harness.Context();
        await connector.ExecuteAsync(context, new PauseItemAction("SABnzbd_nzo_loworbit"), CancellationToken.None);
        await connector.ExecuteAsync(context, new ResumeAllAction(), CancellationToken.None);
        await connector.ExecuteAsync(context, new RemoveItemAction("SABnzbd_nzo_saltroad"), CancellationToken.None);

        Assert.Contains(harness.Handler.Calls, c => c.Path.Contains("mode=queue", StringComparison.Ordinal) && c.Path.Contains("name=pause", StringComparison.Ordinal) && c.Path.Contains("SABnzbd_nzo_loworbit", StringComparison.Ordinal));
        Assert.Contains(harness.Handler.Calls, c => c.Path.Contains("mode=resume", StringComparison.Ordinal));
        Assert.Contains(harness.Handler.Calls, c => c.Path.Contains("name=delete", StringComparison.Ordinal));
        harness.AssertNoSecrets();
    }

    private static ConnectorHarness SabHarness() => new((request, _) =>
    {
        var path = request.RequestUri?.PathAndQuery ?? "";
        if (path.Contains("mode=version", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load("sabnzbd-version.json"));
        if (path.Contains("mode=queue", StringComparison.Ordinal) && !path.Contains("name=", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load("sabnzbd-queue.json"));
        if (path.Contains("mode=history", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load("sabnzbd-history.json"));
        if (path.Contains("mode=warnings", StringComparison.Ordinal))
            return HttpJson.Json(Fixtures.Load("sabnzbd-warnings.json"));
        return HttpJson.Json("{\"status\":true}");
    });
}
