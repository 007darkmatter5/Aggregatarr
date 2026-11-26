using Aggregatarr.Models;

namespace Aggregatarr.Merging;

public sealed record QueueSource
{
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required ServiceKind Kind { get; init; }
    public required RemoteQueueItem Item { get; init; }
}

public static class QueueMerger
{
    public static IReadOnlyList<QueueItem> Merge(IEnumerable<QueueSource> sources)
    {
        var list = sources.ToList();
        var clients = list.Where(s => s.Item.IsDownloadClient).ToList();
        var arr = list.Where(s => !s.Item.IsDownloadClient).ToList();
        var usedArr = new HashSet<QueueSource>();
        var merged = new List<QueueItem>();

        foreach (var client in clients)
        {
            var match = FindMatch(client, arr, usedArr);
            if (match is not null)
                usedArr.Add(match);
            merged.Add(Combine(client, match));
        }

        foreach (var leftover in arr.Where(a => !usedArr.Contains(a)))
            merged.Add(FromArrOnly(leftover));

        return merged
            .OrderByDescending(i => i.Severity)
            .ThenByDescending(i => i.SpeedBytesPerSecond ?? 0)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static QueueSource? FindMatch(QueueSource client, List<QueueSource> arr, HashSet<QueueSource> used)
    {
        var downloadId = Normalize(client.Item.DownloadId) ?? Normalize(client.Item.ItemId);
        if (!string.IsNullOrEmpty(downloadId))
        {
            var byId = arr.FirstOrDefault(a => !used.Contains(a) && SameId(a.Item.DownloadId, downloadId));
            if (byId is not null)
                return byId;
        }

        var clientName = NormalizeTitle(client.Item.Title);
        if (clientName.Length < 6)
            return null;

        return arr.FirstOrDefault(a =>
        {
            if (used.Contains(a))
                return false;
            var title = NormalizeTitle(a.Item.Title);
            return title.Length >= 6 && (title.Contains(clientName, StringComparison.Ordinal) || clientName.Contains(title, StringComparison.Ordinal));
        });
    }

    private static QueueItem Combine(QueueSource client, QueueSource? arr)
    {
        var item = client.Item;
        var severity = item.Severity;
        var detail = item.StatusDetail;
        if (arr is not null && arr.Item.Severity > severity)
        {
            severity = arr.Item.Severity;
            detail = arr.Item.StatusDetail ?? detail;
        }
        else if (arr is not null && string.IsNullOrWhiteSpace(detail))
        {
            detail = arr.Item.StatusDetail;
        }

        var matched = arr is null
            ? null
            : $"{arr.Kind.DisplayName()} · {arr.Item.Title}";

        return new QueueItem
        {
            Key = $"{client.ServiceId:N}:{item.ItemId ?? item.Title}",
            Title = item.Title,
            Subtitle = arr?.Item.Subtitle ?? item.Subtitle,
            MatchedTo = matched,
            ClientName = client.ServiceName,
            ClientKind = client.Kind,
            ClientServiceId = client.ServiceId,
            ClientItemId = item.ItemId,
            ArrServiceId = arr?.ServiceId,
            ArrQueueId = arr?.Item.ItemId,
            Progress = item.Progress,
            SizeBytes = item.SizeBytes ?? arr?.Item.SizeBytes,
            RemainingBytes = item.RemainingBytes ?? arr?.Item.RemainingBytes,
            SpeedBytesPerSecond = item.SpeedBytesPerSecond,
            Eta = item.Eta ?? arr?.Item.Eta,
            State = item.State,
            Severity = severity,
            StatusDetail = detail,
            CanPause = true,
            IsPaused = item.IsPaused,
            CanRemove = true
        };
    }

    private static QueueItem FromArrOnly(QueueSource arr)
    {
        var item = arr.Item;
        return new QueueItem
        {
            Key = $"{arr.ServiceId:N}:arr:{item.ItemId ?? item.Title}",
            Title = item.Title,
            Subtitle = item.Subtitle,
            MatchedTo = $"{arr.Kind.DisplayName()} queue",
            ClientName = arr.ServiceName,
            ClientKind = arr.Kind,
            ClientServiceId = arr.ServiceId,
            ClientItemId = null,
            ArrServiceId = arr.ServiceId,
            ArrQueueId = item.ItemId,
            Progress = item.Progress,
            SizeBytes = item.SizeBytes,
            RemainingBytes = item.RemainingBytes,
            SpeedBytesPerSecond = item.SpeedBytesPerSecond,
            Eta = item.Eta,
            State = item.State,
            Severity = item.Severity,
            StatusDetail = item.StatusDetail,
            CanPause = false,
            IsPaused = false,
            CanRemove = !string.IsNullOrEmpty(item.ItemId)
        };
    }

    private static bool SameId(string? left, string right) =>
        string.Equals(Normalize(left), right, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var chars = value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
        return new string(chars);
    }
}

public static class CalendarClassifier
{
    public static CalendarState Classify(bool monitored, bool hasFile, DateTimeOffset when, DateTimeOffset now)
    {
        if (!monitored)
            return CalendarState.Unmonitored;
        if (hasFile)
            return CalendarState.Downloaded;
        return when <= now ? CalendarState.Missing : CalendarState.Upcoming;
    }
}

public static class ActivityFeed
{
    public static IReadOnlyList<ActivityItem> Merge(IEnumerable<ActivityItem> items) =>
        items
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(i => i.When)
            .Take(200)
            .ToList();
}
