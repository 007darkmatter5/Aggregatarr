using System.Globalization;
using System.Text.Json;
using Aggregatarr.Models;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

public sealed class BazarrConnector : IMediaConnector
{
    private readonly ILogger _logger;

    public BazarrConnector(ILoggerFactory loggers)
    {
        _logger = loggers.CreateLogger("Aggregatarr.Connectors.Bazarr");
    }

    public ServiceKind Kind => ServiceKind.Bazarr;

    public async Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        try
        {
            var status = await GetAsync(context, "api/system/status", cancellationToken);
            return new ConnectionTestResult(true, ReadVersion(status), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, SecretSanitizer.Clean(ex.Message, context));
        }
    }

    public async Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken)
    {
        var status = await GetAsync(context, "api/system/status", cancellationToken);
        var warnings = new List<HealthWarning>();
        try
        {
            var health = await GetAsync(context, "api/system/health", cancellationToken);
            warnings.AddRange(ParseHealth(health));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), "health"));
        }

        var activity = new List<RemoteActivityItem>();
        if (depth == PollDepth.Full)
        {
            await TryHistory(context, warnings, "api/episodes/history?start=0&length=30", activity, "episode", cancellationToken);
            await TryHistory(context, warnings, "api/movies/history?start=0&length=30", activity, "movie", cancellationToken);
        }

        return new ConnectorSnapshot
        {
            Version = ReadVersion(status),
            Warnings = warnings,
            Activity = activity
        };
    }

    public Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Bazarr actions are not part of v1.");

    private async Task TryHistory(ConnectorContext context, List<HealthWarning> warnings, string path, List<RemoteActivityItem> activity, string scope, CancellationToken cancellationToken)
    {
        try
        {
            var history = await GetAsync(context, path, cancellationToken);
            activity.AddRange(ParseHistory(history, scope));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), scope + " history"));
        }
    }

    private Task<JsonElement> GetAsync(ConnectorContext context, string path, CancellationToken cancellationToken) =>
        ConnectorHttp.SendJsonAsync(
            context.Http,
            HttpMethod.Get,
            path,
            _logger,
            "Bazarr",
            context.ApiKey,
            context.Password,
            ConnectorHttp.ApiKeyHeader("X-API-KEY", context.ApiKey),
            null,
            cancellationToken);

    private static string? ReadVersion(JsonElement status)
    {
        var data = status.Child("data") ?? status;
        return data.Str("bazarr_version") ?? data.Str("version");
    }

    internal static IEnumerable<HealthWarning> ParseHealth(JsonElement element)
    {
        var data = element.Child("data") ?? element;
        var items = data.ValueKind == JsonValueKind.Array ? data.AsArray() : data.Items("data");
        foreach (var item in items)
        {
            var message = item.Str("message") ?? item.Str("text");
            if (string.IsNullOrWhiteSpace(message))
                continue;
            var level = (item.Str("type") ?? "warning").ToLowerInvariant();
            if (level is not ("error" or "warning" or "notice"))
                level = "warning";
            yield return new HealthWarning(level, message, item.Str("source") ?? item.Str("object"));
        }
    }

    internal static IEnumerable<RemoteActivityItem> ParseHistory(JsonElement element, string scope)
    {
        var data = element.Child("data") ?? element;
        var items = data.ValueKind == JsonValueKind.Array ? data.AsArray() : Enumerable.Empty<JsonElement>();
        foreach (var item in items)
        {
            var when = item.Date("timestamp") ?? item.Date("parsed_timestamp") ?? FromUnix(item);
            if (when is null)
                continue;
            var action = item.Int("action") ?? 0;
            var kind = action switch
            {
                3 => ActivityKind.Upgrade,
                5 => ActivityKind.Failure,
                4 => ActivityKind.Other,
                _ => ActivityKind.Subtitle
            };
            var title = item.Str("seriesTitle") ?? item.Str("title") ?? (scope == "movie" ? "Movie" : "Episode");
            var language = Language(item);
            var provider = item.Str("provider");
            var episode = item.Int("episode") ?? item.Int("episode_number");
            var season = item.Int("season");
            var code = season is not null && episode is not null ? $"S{season:00}E{episode:00}" : item.Str("title");
            var id = item.Int("id")?.ToString(CultureInfo.InvariantCulture)
                ?? when.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + scope + title;
            yield return new RemoteActivityItem
            {
                Key = "bazarr-" + scope + "-" + id,
                When = when.Value,
                Kind = kind,
                Title = title,
                Detail = string.Join(" · ", new[] { ActionLabel(action), code, language, provider }.Where(s => !string.IsNullOrWhiteSpace(s)))
            };
        }
    }

    private static DateTimeOffset? FromUnix(JsonElement item)
    {
        var unix = item.Long("timestamp");
        if (unix is null or < 1_000_000_000)
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(unix.Value);
    }

    private static string? Language(JsonElement item)
    {
        var language = item.Child("language");
        if (language is null)
            return item.Str("language");
        if (language.Value.ValueKind == JsonValueKind.String)
            return language.Value.GetString();
        return language.Value.Str("name") ?? language.Value.Str("code2");
    }

    private static string ActionLabel(int action) => action switch
    {
        1 => "Subtitle downloaded",
        2 => "Subtitle saved manually",
        3 => "Subtitle upgraded",
        4 => "Subtitle deleted",
        5 => "Subtitle failed",
        _ => "Subtitle event"
    };
}
