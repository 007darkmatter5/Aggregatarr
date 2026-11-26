using System.Globalization;
using System.Text.Json;
using Aggregatarr.Models;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

/// <summary>
/// SABnzbd API. The API key is sent as the X-Api-Key header (SABnzbd 4+) and never
/// placed on the query string, so request logs cannot record it.
/// </summary>
public sealed class SabnzbdConnector : IMediaConnector
{
    private readonly ILogger _logger;

    public SabnzbdConnector(ILoggerFactory loggers)
    {
        _logger = loggers.CreateLogger("Aggregatarr.Connectors.SABnzbd");
    }

    public ServiceKind Kind => ServiceKind.Sabnzbd;

    public async Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        try
        {
            var version = await GetAsync(context, "mode=version", cancellationToken);
            return new ConnectionTestResult(true, version.Str("version"), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, SecretSanitizer.Clean(ex.Message, context));
        }
    }

    public async Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken)
    {
        var queueDoc = await GetAsync(context, "mode=queue&start=0&limit=100", cancellationToken);
        var queue = queueDoc.Child("queue") ?? queueDoc;
        var warnings = new List<HealthWarning>();
        var version = queue.Str("version");

        try
        {
            var warningDoc = await GetAsync(context, "mode=warnings", cancellationToken);
            warnings.AddRange(ParseWarnings(warningDoc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), "warnings"));
        }

        var items = new List<RemoteQueueItem>();
        foreach (var slot in queue.Items("slots"))
        {
            var status = slot.Str("status") ?? "Unknown";
            var labels = slot.Items("labels").Select(l => l.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList();
            var mb = slot.Dbl("mb");
            var left = slot.Dbl("mbleft");
            long? size = mb is null ? null : (long)(mb.Value * 1024 * 1024);
            long? remaining = left is null ? null : (long)(left.Value * 1024 * 1024);
            var percentage = slot.Dbl("percentage") ?? 0;
            var severity = Severity(status, labels);
            var kb = queue.Dbl("kbpersec");
            double? speed = status.Equals("Downloading", StringComparison.OrdinalIgnoreCase) && kb is not null
                ? kb.Value * 1024
                : null;

            items.Add(new RemoteQueueItem
            {
                Title = slot.Str("filename") ?? slot.Str("nzo_id") ?? "NZB",
                DownloadId = slot.Str("nzo_id"),
                ItemId = slot.Str("nzo_id"),
                Progress = Math.Clamp(percentage / 100d, 0, 1),
                SizeBytes = size,
                RemainingBytes = remaining,
                SpeedBytesPerSecond = speed,
                Eta = ParseSabTime(slot.Str("timeleft")),
                State = status,
                Severity = severity,
                StatusDetail = labels.Count == 0 ? null : string.Join(", ", labels),
                IsPaused = status.Equals("Paused", StringComparison.OrdinalIgnoreCase),
                IsDownloadClient = true
            });
        }

        var activity = new List<RemoteActivityItem>();
        if (depth == PollDepth.Full)
        {
            try
            {
                var history = await GetAsync(context, "mode=history&start=0&limit=30", cancellationToken);
                activity.AddRange(ParseHistory(history.Child("history") ?? history));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), "history"));
            }
        }

        return new ConnectorSnapshot
        {
            Version = version,
            Warnings = warnings,
            Queue = items,
            Activity = activity,
            ClientPaused = queue.Bool("paused") ?? queue.Bool("paused_all")
        };
    }

    public async Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case PauseItemAction pause:
                await GetAsync(context, "mode=queue&name=pause&value=" + Uri.EscapeDataString(pause.ItemId), cancellationToken);
                return;
            case ResumeItemAction resume:
                await GetAsync(context, "mode=queue&name=resume&value=" + Uri.EscapeDataString(resume.ItemId), cancellationToken);
                return;
            case PauseAllAction:
                await GetAsync(context, "mode=pause", cancellationToken);
                return;
            case ResumeAllAction:
                await GetAsync(context, "mode=resume", cancellationToken);
                return;
            case RemoveItemAction remove:
                await GetAsync(context, "mode=queue&name=delete&value=" + Uri.EscapeDataString(remove.ItemId), cancellationToken);
                return;
            default:
                throw new NotSupportedException("SABnzbd cannot search the *arr apps.");
        }
    }

    private Task<JsonElement> GetAsync(ConnectorContext context, string query, CancellationToken cancellationToken)
    {
        var path = "api?" + query + (query.Contains("output=", StringComparison.Ordinal) ? "" : "&output=json");
        return ConnectorHttp.SendJsonAsync(
            context.Http,
            HttpMethod.Get,
            path,
            _logger,
            "SABnzbd",
            context.ApiKey,
            context.Password,
            ConnectorHttp.ApiKeyHeader("X-Api-Key", context.ApiKey),
            null,
            cancellationToken);
    }

    internal static IEnumerable<HealthWarning> ParseWarnings(JsonElement element)
    {
        foreach (var warning in element.Items("warnings"))
        {
            if (warning.ValueKind == JsonValueKind.String)
            {
                var text = warning.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    yield return new HealthWarning("warning", text, "SABnzbd");
                continue;
            }

            var message = warning.Str("text") ?? warning.Str("message");
            if (string.IsNullOrWhiteSpace(message))
                continue;
            var type = (warning.Str("type") ?? "warning").ToLowerInvariant();
            var level = type.Contains("error") ? "error" : "warning";
            yield return new HealthWarning(level, message, "SABnzbd");
        }
    }

    internal static IEnumerable<RemoteActivityItem> ParseHistory(JsonElement history)
    {
        foreach (var slot in history.Items("slots"))
        {
            var completed = slot.Long("completed");
            if (completed is null or <= 0)
                continue;
            var status = slot.Str("status") ?? "";
            var failed = !string.IsNullOrWhiteSpace(slot.Str("fail_message")) || status.Contains("fail", StringComparison.OrdinalIgnoreCase);
            var id = slot.Str("nzo_id") ?? completed.Value.ToString(CultureInfo.InvariantCulture);
            yield return new RemoteActivityItem
            {
                Key = "sab-" + id,
                When = DateTimeOffset.FromUnixTimeSeconds(completed.Value),
                Kind = failed ? ActivityKind.Failure : ActivityKind.Complete,
                Title = slot.Str("name") ?? slot.Str("nzb_name") ?? "NZB",
                Detail = failed ? slot.Str("fail_message") ?? status : status
            };
        }
    }

    private static QueueSeverity Severity(string status, List<string> labels)
    {
        if (status.Contains("fail", StringComparison.OrdinalIgnoreCase))
            return QueueSeverity.Failed;
        if (labels.Any(l => l.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase)
                            || l.Contains("TOO LARGE", StringComparison.OrdinalIgnoreCase)
                            || l.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase)))
            return QueueSeverity.Stuck;
        return QueueSeverity.Normal;
    }

    private static TimeSpan? ParseSabTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "0:00:00" or "0:00")
            return null;
        var parts = value.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return new TimeSpan(hours, minutes, seconds);
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
