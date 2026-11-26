namespace Aggregatarr.Models;

public enum PollDepth
{
    Fast,
    Full
}

public enum QueueSeverity
{
    Normal = 0,
    Stuck = 1,
    Failed = 2
}

public enum CalendarState
{
    Upcoming,
    Missing,
    Downloaded,
    Unmonitored
}

public enum ActivityKind
{
    Grab,
    Import,
    Failure,
    Upgrade,
    Complete,
    Subtitle,
    Other
}

public sealed record HealthWarning(string Level, string Message, string? Source);

public sealed record RemoteQueueItem
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? DownloadId { get; init; }
    public string? ItemId { get; init; }
    public double Progress { get; init; }
    public long? SizeBytes { get; init; }
    public long? RemainingBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public required string State { get; init; }
    public QueueSeverity Severity { get; init; }
    public string? StatusDetail { get; init; }
    public bool IsPaused { get; init; }
    public bool IsDownloadClient { get; init; }
}

public sealed record RemoteCalendarItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? Detail { get; init; }
    public required DateTimeOffset When { get; init; }
    public bool HasFile { get; init; }
    public bool Monitored { get; init; }
    public string? PosterPath { get; init; }
    public int? SearchId { get; init; }
    public bool CanSearch { get; init; }
}

public sealed record RemoteActivityItem
{
    public required string Key { get; init; }
    public required DateTimeOffset When { get; init; }
    public required ActivityKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
}

public sealed class ConnectorSnapshot
{
    public string? Version { get; init; }
    public IReadOnlyList<HealthWarning> Warnings { get; init; } = [];
    public IReadOnlyList<RemoteQueueItem> Queue { get; init; } = [];
    public IReadOnlyList<RemoteCalendarItem> Calendar { get; init; } = [];
    public IReadOnlyList<RemoteActivityItem> Activity { get; init; } = [];
    public bool? ClientPaused { get; init; }
}

public sealed record ConnectionTestResult(bool Ok, string? Version, string? Error);

public sealed record QueueItem
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? MatchedTo { get; init; }
    public required string ClientName { get; init; }
    public required ServiceKind ClientKind { get; init; }
    public required Guid ClientServiceId { get; init; }
    public string? ClientItemId { get; init; }
    public Guid? ArrServiceId { get; init; }
    public string? ArrQueueId { get; init; }
    public double Progress { get; init; }
    public long? SizeBytes { get; init; }
    public long? RemainingBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public TimeSpan? Eta { get; init; }
    public required string State { get; init; }
    public QueueSeverity Severity { get; init; }
    public string? StatusDetail { get; init; }
    public bool CanPause { get; init; }
    public bool IsPaused { get; init; }
    public bool CanRemove { get; init; }
}

public sealed record CalendarItem
{
    public required string Key { get; init; }
    public required ServiceKind Source { get; init; }
    public required string SourceName { get; init; }
    public required Guid ServiceId { get; init; }
    public required string Title { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? Detail { get; init; }
    public required DateTimeOffset When { get; init; }
    public CalendarState State { get; init; }
    public string? PosterUrl { get; init; }
    public bool CanSearch { get; init; }
    public int? SearchId { get; init; }
}

public sealed record ActivityItem
{
    public required string Key { get; init; }
    public required DateTimeOffset When { get; init; }
    public required ServiceKind Source { get; init; }
    public required string SourceName { get; init; }
    public required ActivityKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
}

public sealed record ServiceHealth
{
    public required Guid ServiceId { get; init; }
    public required string Name { get; init; }
    public required ServiceKind Kind { get; init; }
    public bool Online { get; init; }
    public string? Version { get; init; }
    public int? ResponseTimeMs { get; init; }
    public string? Error { get; init; }
    public bool? ClientPaused { get; init; }
    public IReadOnlyList<HealthWarning> Warnings { get; init; } = [];
}

public sealed record AlertItem
{
    public required string Key { get; init; }
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required ServiceKind Kind { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Source { get; init; }
}

public sealed class DashboardSnapshot
{
    public static DashboardSnapshot Empty { get; } = new();

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public bool DemoMode { get; init; }
    public IReadOnlyList<QueueItem> Queue { get; init; } = [];
    public IReadOnlyList<CalendarItem> Calendar { get; init; } = [];
    public IReadOnlyList<ActivityItem> Activity { get; init; } = [];
    public IReadOnlyList<ServiceHealth> Services { get; init; } = [];
    public IReadOnlyList<AlertItem> Alerts { get; init; } = [];

    public int AlertCount => Alerts.Count;

    public double TotalDownloadBytesPerSecond =>
        Queue.Where(q => q.SpeedBytesPerSecond is > 0).Sum(q => q.SpeedBytesPerSecond ?? 0);
}

/// <summary>
/// A configured service as the UI is allowed to see it. Secrets are not on this type.
/// </summary>
public sealed record ServiceListItem
{
    public required Guid Id { get; init; }
    public required ServiceKind Kind { get; init; }
    public required string Name { get; init; }
    public required string BaseUrl { get; init; }
    public bool Enabled { get; init; }
    public bool HasApiKey { get; init; }
    public bool HasPassword { get; init; }
    public string? Username { get; init; }
}

public abstract record MediaAction;

public sealed record PauseItemAction(string ItemId) : MediaAction;

public sealed record ResumeItemAction(string ItemId) : MediaAction;

public sealed record PauseAllAction : MediaAction;

public sealed record ResumeAllAction : MediaAction;

public sealed record RemoveItemAction(string ItemId) : MediaAction;

public sealed record SearchIdsAction(IReadOnlyList<int> Ids) : MediaAction;

public sealed record SearchAllMissingAction : MediaAction;
