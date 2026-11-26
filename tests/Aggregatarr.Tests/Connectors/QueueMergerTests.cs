using Aggregatarr.Connectors;
using Aggregatarr.Merging;
using Aggregatarr.Models;

namespace Aggregatarr.Tests.Connectors;

public sealed class QueueMergerTests
{
    [Fact]
    public void Matches_client_rows_to_arr_by_download_id_and_floats_failures()
    {
        var qbit = Guid.NewGuid();
        var sab = Guid.NewGuid();
        var sonarr = Guid.NewGuid();
        const string hash = "8c212779b4abde7c6bc608063a0d008b7e40ce32";

        var merged = QueueMerger.Merge(
        [
            Source(qbit, "qBittorrent", ServiceKind.QBittorrent, new RemoteQueueItem
            {
                Title = "Glass.Harbor.S02E04",
                DownloadId = hash.ToUpperInvariant(),
                ItemId = hash,
                Progress = 0.4,
                State = "Downloading",
                IsDownloadClient = true
            }),
            Source(sab, "SABnzbd", ServiceKind.Sabnzbd, new RemoteQueueItem
            {
                Title = "Broken.Release",
                ItemId = "SABnzbd_nzo_bad",
                DownloadId = "SABnzbd_nzo_bad",
                Progress = 0.1,
                State = "Failed",
                Severity = QueueSeverity.Failed,
                IsDownloadClient = true
            }),
            Source(sonarr, "Sonarr", ServiceKind.Sonarr, new RemoteQueueItem
            {
                Title = "The Glass Harbor",
                Subtitle = "S02E04 · Salt Line",
                DownloadId = hash,
                ItemId = "144",
                Progress = 0.4,
                State = "import blocked",
                Severity = QueueSeverity.Stuck,
                StatusDetail = "Import blocked",
                IsDownloadClient = false
            })
        ]);

        Assert.Equal(QueueSeverity.Failed, merged[0].Severity);
        var matched = merged.Single(i => i.ClientKind == ServiceKind.QBittorrent);
        Assert.Equal("Sonarr · The Glass Harbor", matched.MatchedTo);
        Assert.Equal("S02E04 · Salt Line", matched.Subtitle);
        Assert.Equal(QueueSeverity.Stuck, matched.Severity);
        Assert.Equal("Import blocked", matched.StatusDetail);
        Assert.True(matched.CanPause);
        Assert.Equal(sonarr, matched.ArrServiceId);
    }

    [Fact]
    public void Calendar_state_follows_file_and_air_time()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(CalendarState.Downloaded, CalendarClassifier.Classify(true, true, now.AddDays(-1), now));
        Assert.Equal(CalendarState.Missing, CalendarClassifier.Classify(true, false, now.AddHours(-2), now));
        Assert.Equal(CalendarState.Upcoming, CalendarClassifier.Classify(true, false, now.AddDays(1), now));
        Assert.Equal(CalendarState.Unmonitored, CalendarClassifier.Classify(false, false, now.AddDays(-1), now));
    }

    [Fact]
    public void Sanitizer_and_context_tostring_hide_secrets()
    {
        const string key = "super-secret-api-key-do-not-leak";
        const string password = "super-secret-password-do-not-leak";
        var cleaned = SecretSanitizer.Clean($"failed for {key} with {password}", key, password);
        Assert.DoesNotContain(key, cleaned);
        Assert.DoesNotContain(password, cleaned);
        Assert.Contains("***", cleaned);

        var context = new ConnectorContext
        {
            Http = new HttpClient(),
            BaseUri = new Uri("http://media.example:8989/"),
            ApiKey = key,
            Password = password
        };
        Assert.DoesNotContain(key, context.ToString());
        Assert.DoesNotContain(password, context.ToString());
    }

    private static QueueSource Source(Guid id, string name, ServiceKind kind, RemoteQueueItem item) => new()
    {
        ServiceId = id,
        ServiceName = name,
        Kind = kind,
        Item = item
    };
}
