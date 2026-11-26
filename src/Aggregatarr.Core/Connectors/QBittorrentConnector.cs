using System.Globalization;
using System.Net;
using System.Text.Json;
using Aggregatarr.Models;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

/// <summary>
/// qBittorrent WebUI API v2. qBittorrent 5 (WebAPI 2.11+) renamed pause/resume to stop/start.
/// Older 4.x servers still expose pause/resume, so a 404 falls back.
/// </summary>
public sealed class QBittorrentConnector : IMediaConnector
{
    private readonly ILogger _logger;
    private readonly IClock _clock;

    public QBittorrentConnector(ILoggerFactory loggers, IClock clock)
    {
        _logger = loggers.CreateLogger("Aggregatarr.Connectors.QBittorrent");
        _clock = clock;
    }

    public ServiceKind Kind => ServiceKind.QBittorrent;

    public async Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        try
        {
            await LoginAsync(context, cancellationToken);
            var version = await TextAsync(context, "api/v2/app/version", cancellationToken);
            return new ConnectionTestResult(true, version, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, SecretSanitizer.Clean(ex.Message, context));
        }
    }

    public async Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken)
    {
        await LoginAsync(context, cancellationToken);
        var version = await TextAsync(context, "api/v2/app/version", cancellationToken);
        var torrents = await ConnectorHttp.SendJsonAsync(
            context.Http,
            HttpMethod.Get,
            "api/v2/torrents/info",
            _logger,
            "qBittorrent",
            context.ApiKey,
            context.Password,
            Referer(context),
            null,
            cancellationToken);

        var warnings = new List<HealthWarning>();
        var queue = new List<RemoteQueueItem>();
        var activity = new List<RemoteActivityItem>();
        var cutoff = _clock.UtcNow.AddDays(-3).ToUnixTimeSeconds();

        foreach (var torrent in torrents.AsArray())
        {
            var state = torrent.Str("state") ?? "unknown";
            var hash = torrent.Str("hash") ?? "";
            var name = torrent.Str("name") ?? hash;
            var progress = torrent.Dbl("progress") ?? 0;
            var size = torrent.Long("size");
            var speed = torrent.Dbl("dlspeed");
            var etaSeconds = torrent.Long("eta") ?? torrent.Int("eta");
            TimeSpan? eta = etaSeconds is > 0 and < 8_640_000 ? TimeSpan.FromSeconds(etaSeconds.Value) : null;

            if (IsDownload(state, progress))
            {
                var severity = state switch
                {
                    "error" or "missingFiles" => QueueSeverity.Failed,
                    "stalledDL" => QueueSeverity.Stuck,
                    _ => QueueSeverity.Normal
                };
                queue.Add(new RemoteQueueItem
                {
                    Title = name,
                    DownloadId = hash,
                    ItemId = hash,
                    Progress = progress,
                    SizeBytes = size,
                    RemainingBytes = size is null ? null : (long)(size.Value * (1 - Math.Clamp(progress, 0, 1))),
                    SpeedBytesPerSecond = speed,
                    Eta = eta,
                    State = HumanState(state),
                    Severity = severity,
                    StatusDetail = severity == QueueSeverity.Failed ? "qBittorrent reported an error for this torrent." : severity == QueueSeverity.Stuck ? "No peers are sending data." : null,
                    IsPaused = state is "pausedDL" or "stoppedDL" or "pausedUP",
                    IsDownloadClient = true
                });
            }

            if (depth == PollDepth.Full)
            {
                var completed = torrent.Long("completion_on") ?? 0;
                if (completed >= cutoff && progress >= 0.999)
                {
                    activity.Add(new RemoteActivityItem
                    {
                        Key = "qbit-" + hash,
                        When = DateTimeOffset.FromUnixTimeSeconds(completed),
                        Kind = ActivityKind.Complete,
                        Title = name,
                        Detail = "Completed in qBittorrent"
                    });
                }
            }
        }

        if (queue.Any(q => q.Severity == QueueSeverity.Failed))
            warnings.Add(new HealthWarning("error", "One or more torrents are in an error state.", "torrents"));

        return new ConnectorSnapshot
        {
            Version = version,
            Warnings = warnings,
            Queue = queue,
            Activity = activity
        };
    }

    public async Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken)
    {
        await LoginAsync(context, cancellationToken);
        switch (action)
        {
            case PauseItemAction pause:
                await PostHashesAsync(context, "api/v2/torrents/stop", "api/v2/torrents/pause", pause.ItemId, cancellationToken);
                return;
            case ResumeItemAction resume:
                await PostHashesAsync(context, "api/v2/torrents/start", "api/v2/torrents/resume", resume.ItemId, cancellationToken);
                return;
            case PauseAllAction:
                await PostHashesAsync(context, "api/v2/torrents/stop", "api/v2/torrents/pause", "all", cancellationToken);
                return;
            case ResumeAllAction:
                await PostHashesAsync(context, "api/v2/torrents/start", "api/v2/torrents/resume", "all", cancellationToken);
                return;
            case RemoveItemAction remove:
                await PostFormAsync(context, "api/v2/torrents/delete", new Dictionary<string, string>
                {
                    ["hashes"] = remove.ItemId,
                    ["deleteFiles"] = "false"
                }, cancellationToken);
                return;
            default:
                throw new NotSupportedException("qBittorrent cannot search the *arr apps.");
        }
    }

    private async Task LoginAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = context.Username ?? "",
            ["password"] = context.Password ?? ""
        });
        var body = await ConnectorHttp.SendTextAsync(
            context.Http,
            HttpMethod.Post,
            "api/v2/auth/login",
            _logger,
            "qBittorrent",
            context.ApiKey,
            context.Password,
            Referer(context),
            form,
            cancellationToken);

        if (!body.StartsWith("Ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("qBittorrent rejected the WebUI username or password.");
    }

    private Task<string> TextAsync(ConnectorContext context, string path, CancellationToken cancellationToken) =>
        ConnectorHttp.SendTextAsync(
            context.Http,
            HttpMethod.Get,
            path,
            _logger,
            "qBittorrent",
            context.ApiKey,
            context.Password,
            Referer(context),
            null,
            cancellationToken);

    private async Task PostHashesAsync(ConnectorContext context, string modernPath, string legacyPath, string hashes, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string> { ["hashes"] = hashes };
        var code = await TryPostAsync(context, modernPath, fields, cancellationToken);
        if (code == HttpStatusCode.NotFound)
        {
            await PostFormAsync(context, legacyPath, fields, cancellationToken);
            return;
        }

        if (code is not HttpStatusCode.OK and not HttpStatusCode.NoContent)
            throw new InvalidOperationException($"qBittorrent returned {(int)code} for {modernPath}.");
    }

    private async Task PostFormAsync(ConnectorContext context, string path, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var code = await TryPostAsync(context, path, fields, cancellationToken);
        if (code is not HttpStatusCode.OK and not HttpStatusCode.NoContent)
            throw new InvalidOperationException($"qBittorrent returned {(int)code} for {path}.");
    }

    private async Task<HttpStatusCode> TryPostAsync(ConnectorContext context, string path, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        foreach (var header in Referer(context))
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        _logger.LogInformation("qBittorrent POST {Path}", path);
        using var response = await context.Http.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    private static IEnumerable<KeyValuePair<string, string>> Referer(ConnectorContext context)
    {
        yield return new KeyValuePair<string, string>("Referer", context.BaseUri.ToString());
    }

    internal static bool IsDownload(string state, double progress)
    {
        if (state is "error" or "missingFiles")
            return true;
        if (state is "uploading" or "pausedUP" or "queuedUP" or "stalledUP" or "checkingUP" or "forcedUP")
            return progress < 0.999;
        return state is "downloading" or "metaDL" or "pausedDL" or "queuedDL" or "stalledDL"
            or "checkingDL" or "forcedDL" or "allocating" or "checkingResumeData" or "moving" or "stoppedDL" or "unknown";
    }

    private static string HumanState(string state) => state switch
    {
        "downloading" or "forcedDL" => "Downloading",
        "metaDL" => "Fetching metadata",
        "pausedDL" or "stoppedDL" => "Paused",
        "queuedDL" => "Queued",
        "stalledDL" => "Stalled",
        "checkingDL" or "checkingResumeData" => "Checking",
        "allocating" => "Allocating",
        "moving" => "Moving",
        "error" => "Error",
        "missingFiles" => "Missing files",
        _ => state
    };
}
