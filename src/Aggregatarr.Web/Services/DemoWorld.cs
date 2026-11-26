using Aggregatarr.Models;

namespace Aggregatarr.Web.Services;

public static class DemoIds
{
    public static readonly Guid Sonarr = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Radarr = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Lidarr = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid Prowlarr = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid Bazarr = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid Qbit = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid Sab = Guid.Parse("10000000-0000-0000-0000-000000000007");
}

public sealed class DemoJob
{
    public required string ItemId { get; init; }
    public required Guid ClientId { get; init; }
    public required string ClientName { get; init; }
    public required ServiceKind ClientKind { get; init; }
    public Guid? ArrServiceId { get; init; }
    public string? ArrQueueId { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? MatchedTo { get; init; }
    public double Progress { get; set; }
    public double BaseSpeed { get; init; }
    public double Speed { get; set; }
    public long Size { get; init; }
    public required string State { get; set; }
    public string StateOverride { get; set; } = "";
    public QueueSeverity Severity { get; init; }
    public string? Detail { get; init; }
    public bool Paused { get; set; }
    public bool Removed { get; set; }
    public bool CanPause { get; init; }
    public bool CanRemove { get; init; } = true;
    public double Phase { get; init; }
}

/// <summary>
/// In-memory sample library used when AGGREGATARR_DEMO=true. Nothing here is a real host or credential.
/// </summary>
public sealed class DemoWorld
{
    private readonly object _gate = new();
    private readonly List<DemoJob> _jobs;
    private readonly List<ActivityItem> _activity;
    private int _searchSerial;

    public DemoWorld(IClock clock)
    {
        _jobs = SeedJobs();
        _activity = SeedActivity(clock.UtcNow);
    }

    public void Apply(Guid serviceId, MediaAction action, string? target)
    {
        lock (_gate)
        {
            switch (action)
            {
                case PauseItemAction pause:
                    Touch(pause.ItemId, job => { job.Paused = true; job.Speed = 0; job.StateOverride = "Paused"; });
                    break;
                case ResumeItemAction resume:
                    Touch(resume.ItemId, job => { job.Paused = false; job.Speed = job.BaseSpeed; job.StateOverride = ""; });
                    break;
                case PauseAllAction:
                    foreach (var job in _jobs.Where(j => j.ClientId == serviceId && j.CanPause))
                    {
                        job.Paused = true;
                        job.Speed = 0;
                        job.StateOverride = "Paused";
                    }
                    break;
                case ResumeAllAction:
                    foreach (var job in _jobs.Where(j => j.ClientId == serviceId && j.CanPause))
                    {
                        job.Paused = false;
                        job.Speed = job.BaseSpeed;
                        job.StateOverride = "";
                    }
                    break;
                case RemoveItemAction remove:
                    Touch(remove.ItemId, job => job.Removed = true);
                    break;
                case SearchIdsAction or SearchAllMissingAction:
                    _searchSerial++;
                    _activity.Insert(0, new ActivityItem
                    {
                        Key = "demo-search-" + _searchSerial,
                        When = DateTimeOffset.UtcNow,
                        Source = serviceId == DemoIds.Radarr ? ServiceKind.Radarr : serviceId == DemoIds.Lidarr ? ServiceKind.Lidarr : ServiceKind.Sonarr,
                        SourceName = serviceId == DemoIds.Radarr ? "Radarr" : serviceId == DemoIds.Lidarr ? "Lidarr" : "Sonarr",
                        Kind = ActivityKind.Grab,
                        Title = string.IsNullOrWhiteSpace(target) ? "Missing search" : target,
                        Detail = "Search requested from Aggregatarr"
                    });
                    break;
            }
        }
    }

    public DashboardSnapshot Build(DateTimeOffset now, bool advance)
    {
        lock (_gate)
        {
            if (advance)
            {
                var wave = 0.82 + 0.18 * Math.Sin(now.ToUnixTimeMilliseconds() / 900.0);
                foreach (var job in _jobs.Where(j => !j.Removed && !j.Paused && j.Severity == QueueSeverity.Normal && j.BaseSpeed > 0))
                {
                    job.Speed = job.BaseSpeed * (0.75 + 0.25 * Math.Sin(now.ToUnixTimeSeconds() / 2.5 + job.Phase));
                    var delta = job.Speed * 2 / Math.Max(job.Size, 1);
                    job.Progress = Math.Min(0.97, job.Progress + delta);
                }

                _ = wave;
            }

            var queue = _jobs.Where(j => !j.Removed).Select(ToQueue).OrderByDescending(q => q.Severity).ThenByDescending(q => q.SpeedBytesPerSecond ?? 0).ToList();
            return new DashboardSnapshot
            {
                UpdatedAt = now,
                DemoMode = true,
                Queue = queue,
                Calendar = Calendar(now),
                Activity = _activity.OrderByDescending(a => a.When).Take(40).ToList(),
                Services = Services(),
                Alerts = Alerts()
            };
        }
    }

    private void Touch(string itemId, Action<DemoJob> change)
    {
        var job = _jobs.FirstOrDefault(j => j.ItemId == itemId || j.ArrQueueId == itemId);
        if (job is not null)
            change(job);
    }

    private static QueueItem ToQueue(DemoJob job)
    {
        var state = string.IsNullOrEmpty(job.StateOverride) ? job.State : job.StateOverride;
        var paused = job.Paused || state == "Paused";
        var remaining = (long)(job.Size * (1 - Math.Clamp(job.Progress, 0, 1)));
        TimeSpan? eta = job.Speed > 1 && !paused ? TimeSpan.FromSeconds(remaining / job.Speed) : null;
        return new QueueItem
        {
            Key = job.ClientId.ToString("N") + ":" + job.ItemId,
            Title = job.Title,
            Subtitle = job.Subtitle,
            MatchedTo = job.MatchedTo,
            ClientName = job.ClientName,
            ClientKind = job.ClientKind,
            ClientServiceId = job.ClientId,
            ClientItemId = job.CanPause ? job.ItemId : null,
            ArrServiceId = job.ArrServiceId,
            ArrQueueId = job.ArrQueueId,
            Progress = job.Progress,
            SizeBytes = job.Size,
            RemainingBytes = remaining,
            SpeedBytesPerSecond = paused ? 0 : job.Speed,
            Eta = eta,
            State = state,
            Severity = job.Severity,
            StatusDetail = job.Detail,
            CanPause = job.CanPause,
            IsPaused = paused,
            CanRemove = job.CanRemove
        };
    }

    private static List<DemoJob> SeedJobs() =>
    [
        new DemoJob
        {
            ItemId = "q-fail",
            ClientId = DemoIds.Qbit,
            ClientName = "qBittorrent",
            ClientKind = ServiceKind.QBittorrent,
            ArrServiceId = DemoIds.Sonarr,
            ArrQueueId = "arr-fail",
            Title = "Glass.Harbor.S02E04.2160p.WEB.x265",
            Subtitle = "S02E04 · Salt Line",
            MatchedTo = "Sonarr · The Glass Harbor",
            Progress = 0.72,
            Size = 4_180_000_000,
            State = "Error",
            Severity = QueueSeverity.Failed,
            Detail = "Import blocked: no video file in the download is eligible.",
            CanPause = true,
            Phase = 0.2
        },
        new DemoJob
        {
            ItemId = "q-stall",
            ClientId = DemoIds.Qbit,
            ClientName = "qBittorrent",
            ClientKind = ServiceKind.QBittorrent,
            ArrServiceId = DemoIds.Radarr,
            Title = "Northline.2026.1080p.BluRay.x264",
            Subtitle = "2026",
            MatchedTo = "Radarr · Northline",
            Progress = 0.12,
            Size = 8_640_000_000,
            State = "Stalled",
            Severity = QueueSeverity.Stuck,
            Detail = "No peers are sending data.",
            CanPause = true,
            Phase = 1.1
        },
        new DemoJob
        {
            ItemId = "s-dup",
            ClientId = DemoIds.Sab,
            ClientName = "SABnzbd",
            ClientKind = ServiceKind.Sabnzbd,
            ArrServiceId = DemoIds.Radarr,
            Title = "Salt.Road.2025.2160p.WEB.H265",
            Subtitle = "2025",
            MatchedTo = "Radarr · Salt Road",
            Progress = 0.08,
            Size = 6_200_000_000,
            State = "Paused",
            Severity = QueueSeverity.Stuck,
            Detail = "DUPLICATE",
            Paused = true,
            CanPause = true,
            Phase = 2
        },
        new DemoJob
        {
            ItemId = "q-run",
            ClientId = DemoIds.Qbit,
            ClientName = "qBittorrent",
            ClientKind = ServiceKind.QBittorrent,
            ArrServiceId = DemoIds.Sonarr,
            Title = "Paper.Satellites.S01E08.1080p.WEB.H264",
            Subtitle = "S01E08 · Burn List",
            MatchedTo = "Sonarr · Paper Satellites",
            Progress = 0.64,
            BaseSpeed = 18_700_000,
            Speed = 18_700_000,
            Size = 2_460_000_000,
            State = "Downloading",
            CanPause = true,
            Phase = 0.4
        },
        new DemoJob
        {
            ItemId = "s-run",
            ClientId = DemoIds.Sab,
            ClientName = "SABnzbd",
            ClientKind = ServiceKind.Sabnzbd,
            ArrServiceId = DemoIds.Sonarr,
            Title = "Low.Orbit.Club.S03E02.720p.WEB.x264",
            Subtitle = "S03E02 · Window Seat",
            MatchedTo = "Sonarr · Low Orbit Club",
            Progress = 0.41,
            BaseSpeed = 8_250_000,
            Speed = 8_250_000,
            Size = 1_120_000_000,
            State = "Downloading",
            CanPause = true,
            Phase = 1.7
        },
        new DemoJob
        {
            ItemId = "q-meta",
            ClientId = DemoIds.Qbit,
            ClientName = "qBittorrent",
            ClientKind = ServiceKind.QBittorrent,
            ArrServiceId = DemoIds.Lidarr,
            Title = "Northline - Paper Weather",
            Subtitle = "Album",
            MatchedTo = "Lidarr · Northline",
            Progress = 0.03,
            BaseSpeed = 420_000,
            Speed = 420_000,
            Size = 310_000_000,
            State = "Fetching metadata",
            CanPause = true,
            Phase = 2.4
        },
        new DemoJob
        {
            ItemId = "arr-import",
            ClientId = DemoIds.Sonarr,
            ClientName = "Sonarr",
            ClientKind = ServiceKind.Sonarr,
            ArrServiceId = DemoIds.Sonarr,
            ArrQueueId = "arr-import",
            Title = "The Glass Harbor",
            Subtitle = "S02E03 · High Water",
            MatchedTo = "Sonarr queue",
            Progress = 1,
            Size = 1_840_000_000,
            State = "Importing",
            Detail = "Downloaded. Waiting on the import.",
            CanPause = false,
            Phase = 0
        }
    ];

    private static List<ActivityItem> SeedActivity(DateTimeOffset now) =>
    [
        new() { Key = "a1", When = now.AddMinutes(-6), Source = ServiceKind.Sonarr, SourceName = "Sonarr", Kind = ActivityKind.Import, Title = "The Glass Harbor", Detail = "Imported S02E02 · 1080p WEB" },
        new() { Key = "a2", When = now.AddMinutes(-18), Source = ServiceKind.Radarr, SourceName = "Radarr", Kind = ActivityKind.Grab, Title = "Northline", Detail = "Grabbed from Example Indexer · BluRay-1080p" },
        new() { Key = "a3", When = now.AddMinutes(-27), Source = ServiceKind.QBittorrent, SourceName = "qBittorrent", Kind = ActivityKind.Complete, Title = "Paper.Satellites.S01E07", Detail = "Completed in qBittorrent" },
        new() { Key = "a4", When = now.AddMinutes(-41), Source = ServiceKind.Bazarr, SourceName = "Bazarr", Kind = ActivityKind.Subtitle, Title = "The Glass Harbor", Detail = "Subtitle downloaded · S02E02 · English · ExampleSubs" },
        new() { Key = "a5", When = now.AddHours(-2), Source = ServiceKind.Sonarr, SourceName = "Sonarr", Kind = ActivityKind.Failure, Title = "Low Orbit Club", Detail = "Download failed · S03E01 · articles missing" },
        new() { Key = "a6", When = now.AddHours(-3), Source = ServiceKind.Radarr, SourceName = "Radarr", Kind = ActivityKind.Upgrade, Title = "Meridian Dogs", Detail = "Upgraded WEB-1080p to BluRay-1080p" },
        new() { Key = "a7", When = now.AddHours(-5), Source = ServiceKind.Sabnzbd, SourceName = "SABnzbd", Kind = ActivityKind.Complete, Title = "Low.Orbit.Club.S03E01.720p", Detail = "Completed" },
        new() { Key = "a8", When = now.AddHours(-8), Source = ServiceKind.Prowlarr, SourceName = "Prowlarr", Kind = ActivityKind.Grab, Title = "Glass.Harbor.S02E04.2160p", Detail = "Example Indexer · qBittorrent" },
        new() { Key = "a9", When = now.AddHours(-14), Source = ServiceKind.Lidarr, SourceName = "Lidarr", Kind = ActivityKind.Import, Title = "Northline", Detail = "Imported album · Paper Weather" },
        new() { Key = "a10", When = now.AddHours(-20), Source = ServiceKind.Bazarr, SourceName = "Bazarr", Kind = ActivityKind.Upgrade, Title = "Paper Satellites", Detail = "Subtitle upgraded · S01E06 · English" },
        new() { Key = "a11", When = now.AddHours(-26), Source = ServiceKind.Sonarr, SourceName = "Sonarr", Kind = ActivityKind.Grab, Title = "Paper Satellites", Detail = "Grabbed S01E08 from Example Indexer" },
        new() { Key = "a12", When = now.AddHours(-30), Source = ServiceKind.Sabnzbd, SourceName = "SABnzbd", Kind = ActivityKind.Failure, Title = "Salt.Road.2025.2160p", Detail = "Not enough repair blocks" }
    ];

    private static IReadOnlyList<CalendarItem> Calendar(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        CalendarItem Item(string key, ServiceKind kind, string source, Guid id, string title, string? episode, string? detail, int dayOffset, int hour, CalendarState state, string poster, int? searchId) =>
            new()
            {
                Key = key,
                Source = kind,
                SourceName = source,
                ServiceId = id,
                Title = title,
                EpisodeTitle = episode,
                Detail = detail,
                When = new DateTimeOffset(today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, 0)), TimeZoneInfo.Local.GetUtcOffset(DateTime.Now)).ToUniversalTime(),
                State = state,
                PosterUrl = "/demo/poster/" + poster,
                CanSearch = state == CalendarState.Missing && searchId is not null,
                SearchId = searchId
            };

        return
        [
            Item("c1", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "The Glass Harbor", "High Water", "S02E03", -3, 1, CalendarState.Downloaded, "glass-harbor", 3),
            Item("c2", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "The Glass Harbor", "Salt Line", "S02E04", -1, 1, CalendarState.Missing, "glass-harbor", 4),
            Item("c3", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "Paper Satellites", "Burn List", "S01E08", 0, 2, CalendarState.Upcoming, "paper-satellites", 8),
            Item("c4", ServiceKind.Radarr, "Radarr", DemoIds.Radarr, "Northline", null, "Digital", 0, 18, CalendarState.Missing, "northline", 21),
            Item("c5", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "Low Orbit Club", "Window Seat", "S03E02", 1, 3, CalendarState.Upcoming, "low-orbit", 32),
            Item("c6", ServiceKind.Lidarr, "Lidarr", DemoIds.Lidarr, "Northline", "Paper Weather", "Album", 2, 12, CalendarState.Upcoming, "paper-weather", 40),
            Item("c7", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "The Glass Harbor", "Keel", "S02E05", 6, 1, CalendarState.Upcoming, "glass-harbor", 5),
            Item("c8", ServiceKind.Radarr, "Radarr", DemoIds.Radarr, "Salt Road", null, "In cinemas", 4, 19, CalendarState.Upcoming, "salt-road", 22),
            Item("c9", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "Paper Satellites", "Quiet Room", "S01E09", 7, 2, CalendarState.Upcoming, "paper-satellites", 9),
            Item("c10", ServiceKind.Radarr, "Radarr", DemoIds.Radarr, "Meridian Dogs", null, "Physical", -6, 0, CalendarState.Downloaded, "meridian", 23),
            Item("c11", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "Low Orbit Club", "Night Shift", "S03E01", -2, 3, CalendarState.Downloaded, "low-orbit", 31),
            Item("c12", ServiceKind.Lidarr, "Lidarr", DemoIds.Lidarr, "Paper Satellites", "Field Notes", "Album", -8, 12, CalendarState.Missing, "field-notes", 41),
            Item("c13", ServiceKind.Sonarr, "Sonarr", DemoIds.Sonarr, "The Glass Harbor", "Pilot", "S01E01", 12, 1, CalendarState.Unmonitored, "glass-harbor", null),
            Item("c14", ServiceKind.Radarr, "Radarr", DemoIds.Radarr, "Glass Weather", null, "Digital", 16, 16, CalendarState.Upcoming, "glass-weather", 24)
        ];
    }

    private static IReadOnlyList<ServiceHealth> Services() =>
    [
        Health(DemoIds.Sonarr, "Sonarr", ServiceKind.Sonarr, true, "4.0.15.2942", 42, null, [new HealthWarning("warning", "Indexer ExampleNZB has been unavailable for 6 hours.", "IndexerLongTermStatus")]),
        Health(DemoIds.Radarr, "Radarr", ServiceKind.Radarr, true, "5.18.4.9674", 38, null, []),
        Health(DemoIds.Lidarr, "Lidarr", ServiceKind.Lidarr, true, "2.11.2.4629", 51, null, []),
        Health(DemoIds.Prowlarr, "Prowlarr", ServiceKind.Prowlarr, true, "1.31.2.4978", 33, null, [new HealthWarning("notice", "An update is available.", "Update")]),
        Health(DemoIds.Bazarr, "Bazarr", ServiceKind.Bazarr, false, null, 8000, "Timed out after 8s contacting bazarr.example:6767", []),
        Health(DemoIds.Qbit, "qBittorrent", ServiceKind.QBittorrent, true, "v5.0.2", 27, null, [new HealthWarning("error", "One or more torrents are in an error state.", "torrents")]),
        Health(DemoIds.Sab, "SABnzbd", ServiceKind.Sabnzbd, true, "4.4.1", 44, null, [new HealthWarning("error", "Too many connections to news.example:563", "SABnzbd")])
    ];

    private static ServiceHealth Health(Guid id, string name, ServiceKind kind, bool online, string? version, int ms, string? error, IReadOnlyList<HealthWarning> warnings) =>
        new()
        {
            ServiceId = id,
            Name = name,
            Kind = kind,
            Online = online,
            Version = version,
            ResponseTimeMs = online ? ms : ms,
            Error = error,
            Warnings = warnings
        };

    private static IReadOnlyList<AlertItem> Alerts() =>
    [
        new() { Key = "al-baz", ServiceId = DemoIds.Bazarr, ServiceName = "Bazarr", Kind = ServiceKind.Bazarr, Level = "error", Message = "Timed out after 8s contacting bazarr.example:6767", Source = "connection" },
        new() { Key = "al-sab", ServiceId = DemoIds.Sab, ServiceName = "SABnzbd", Kind = ServiceKind.Sabnzbd, Level = "error", Message = "Too many connections to news.example:563", Source = "SABnzbd" },
        new() { Key = "al-qbit", ServiceId = DemoIds.Qbit, ServiceName = "qBittorrent", Kind = ServiceKind.QBittorrent, Level = "error", Message = "One or more torrents are in an error state.", Source = "torrents" },
        new() { Key = "al-sonarr", ServiceId = DemoIds.Sonarr, ServiceName = "Sonarr", Kind = ServiceKind.Sonarr, Level = "warning", Message = "Indexer ExampleNZB has been unavailable for 6 hours.", Source = "IndexerLongTermStatus" },
        new() { Key = "al-prowlarr", ServiceId = DemoIds.Prowlarr, ServiceName = "Prowlarr", Kind = ServiceKind.Prowlarr, Level = "notice", Message = "An update is available.", Source = "Update" }
    ];
}
