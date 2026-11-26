using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Aggregatarr.Models;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

public enum ArrMedia
{
    Series,
    Movie,
    Album
}

public sealed record ServarrFlavor(ServiceKind Kind, string ApiRoot, ArrMedia Media)
{
    public static readonly ServarrFlavor Sonarr = new(ServiceKind.Sonarr, "api/v3", ArrMedia.Series);
    public static readonly ServarrFlavor Radarr = new(ServiceKind.Radarr, "api/v3", ArrMedia.Movie);
    public static readonly ServarrFlavor Lidarr = new(ServiceKind.Lidarr, "api/v1", ArrMedia.Album);
}

/// <summary>
/// Sonarr v3, Radarr v3, and Lidarr v1 share the Servarr shape. The API root and the
/// calendar/queue payload differ by app.
/// </summary>
public sealed class ServarrConnector : IMediaConnector
{
    private readonly ServarrFlavor _flavor;
    private readonly ILogger _logger;
    private readonly IClock _clock;

    public ServarrConnector(ServarrFlavor flavor, ILoggerFactory loggers, IClock clock)
    {
        _flavor = flavor;
        _logger = loggers.CreateLogger($"Aggregatarr.Connectors.{flavor.Kind}");
        _clock = clock;
    }

    public ServiceKind Kind => _flavor.Kind;

    public async Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        try
        {
            var status = await GetAsync(context, $"{Root()}/system/status", cancellationToken);
            return new ConnectionTestResult(true, status.Str("version"), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, SecretSanitizer.Clean(ex.Message, context));
        }
    }

    public async Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken)
    {
        var status = await GetAsync(context, $"{Root()}/system/status", cancellationToken);
        var warnings = new List<HealthWarning>();
        await TryAddAsync(context, warnings, $"{Root()}/health", cancellationToken, ParseHealth);

        var queue = new List<RemoteQueueItem>();
        await TryAddAsync(context, warnings, QueuePath(), cancellationToken, element =>
        {
            queue.AddRange(ParseQueue(element));
            return [];
        });

        var calendar = new List<RemoteCalendarItem>();
        var activity = new List<RemoteActivityItem>();
        if (depth == PollDepth.Full)
        {
            var start = context.CalendarStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = context.CalendarEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await TryAddAsync(
                context,
                warnings,
                $"{Root()}/calendar?start={start}&end={end}&unmonitored=true&includeSeries=true&includeEpisode=true",
                cancellationToken,
                element =>
                {
                    calendar.AddRange(ParseCalendar(element));
                    return [];
                });

            await TryAddAsync(
                context,
                warnings,
                $"{Root()}/history?page=1&pageSize=40&sortKey=date&sortDirection=descending&includeSeries=true&includeEpisode=true",
                cancellationToken,
                element =>
                {
                    activity.AddRange(ParseHistory(element));
                    return [];
                });
        }

        return new ConnectorSnapshot
        {
            Version = status.Str("version"),
            Warnings = warnings,
            Queue = queue,
            Calendar = calendar,
            Activity = activity
        };
    }

    public async Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken)
    {
        switch (action)
        {
            case RemoveItemAction remove:
                await SendAsync(context, HttpMethod.Delete, $"{Root()}/queue/{Uri.EscapeDataString(remove.ItemId)}?removeFromClient=true&blocklist=false", null, cancellationToken);
                return;
            case SearchAllMissingAction:
                await SendAsync(context, HttpMethod.Post, $"{Root()}/command", Command(SearchAllName(), null), cancellationToken);
                return;
            case SearchIdsAction search:
                await SendAsync(context, HttpMethod.Post, $"{Root()}/command", Command(SearchOneName(), search.Ids), cancellationToken);
                return;
            default:
                throw new NotSupportedException($"{Kind.DisplayName()} does not support {action.GetType().Name}. Pause and resume happen on the download client.");
        }
    }

    private string Root() => _flavor.ApiRoot.Trim('/');

    private string QueuePath() =>
        $"{Root()}/queue?page=1&pageSize=100&includeUnknownSeriesItems=true&includeSeries=true&includeEpisode=true&includeMovie=true&includeAlbum=true&includeArtist=true";

    private string SearchAllName() => _flavor.Media switch
    {
        ArrMedia.Series => "MissingEpisodeSearch",
        ArrMedia.Movie => "MissingMoviesSearch",
        ArrMedia.Album => "MissingAlbumSearch",
        _ => throw new InvalidOperationException()
    };

    private string SearchOneName() => _flavor.Media switch
    {
        ArrMedia.Series => "EpisodeSearch",
        ArrMedia.Movie => "MoviesSearch",
        ArrMedia.Album => "AlbumSearch",
        _ => throw new InvalidOperationException()
    };

    private object Command(string name, IReadOnlyList<int>? ids)
    {
        if (ids is null || ids.Count == 0)
            return new { name };

        return _flavor.Media switch
        {
            ArrMedia.Series => new { name, episodeIds = ids },
            ArrMedia.Movie => new { name, movieIds = ids },
            ArrMedia.Album => new { name, albumIds = ids },
            _ => new { name }
        };
    }

    private async Task TryAddAsync(
        ConnectorContext context,
        List<HealthWarning> warnings,
        string path,
        CancellationToken cancellationToken,
        Func<JsonElement, IEnumerable<HealthWarning>> parse)
    {
        try
        {
            var element = await GetAsync(context, path, cancellationToken);
            warnings.AddRange(parse(element));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), path));
        }
    }

    private async Task<JsonElement> GetAsync(ConnectorContext context, string path, CancellationToken cancellationToken) =>
        await SendAsync(context, HttpMethod.Get, path, null, cancellationToken);

    private Task<JsonElement> SendAsync(ConnectorContext context, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        HttpContent? content = body is null ? null : JsonContent.Create(body);
        return ConnectorHttp.SendJsonAsync(
            context.Http,
            method,
            path,
            _logger,
            Kind.DisplayName(),
            context.ApiKey,
            context.Password,
            ConnectorHttp.ApiKeyHeader("X-Api-Key", context.ApiKey),
            content,
            cancellationToken);
    }

    internal static IEnumerable<HealthWarning> ParseHealth(JsonElement element)
    {
        var items = element.ValueKind == JsonValueKind.Array ? element.AsArray() : element.Items("records");
        foreach (var item in items)
        {
            var message = item.Str("message");
            if (string.IsNullOrWhiteSpace(message))
                continue;
            var type = (item.Str("type") ?? "warning").ToLowerInvariant();
            var level = type is "error" or "warning" or "notice" ? type : "warning";
            yield return new HealthWarning(level, message, item.Str("source"));
        }
    }

    private IEnumerable<RemoteQueueItem> ParseQueue(JsonElement element)
    {
        var records = element.ValueKind == JsonValueKind.Array
            ? element.AsArray()
            : element.Items("records");

        foreach (var record in records)
        {
            var size = record.Long("size") ?? 0;
            var left = record.Long("sizeleft") ?? 0;
            var progress = size > 0 ? Math.Clamp((double)(size - left) / size, 0, 1) : 0;
            var tracked = (record.Str("trackedDownloadStatus") ?? "ok").ToLowerInvariant();
            var state = record.Str("trackedDownloadState") ?? record.Str("status") ?? "unknown";
            var messages = ReadStatusMessages(record);
            var severity = tracked switch
            {
                "error" => QueueSeverity.Failed,
                "warning" => QueueSeverity.Stuck,
                _ => QueueSeverity.Normal
            };
            if (state.Contains("failed", StringComparison.OrdinalIgnoreCase))
                severity = QueueSeverity.Failed;

            var (title, subtitle) = Titles(record);
            yield return new RemoteQueueItem
            {
                Title = title,
                Subtitle = subtitle,
                DownloadId = record.Str("downloadId"),
                ItemId = record.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? record.Str("id"),
                Progress = progress,
                SizeBytes = size == 0 ? null : size,
                RemainingBytes = size == 0 ? null : left,
                Eta = ParseTimeLeft(record.Str("timeleft")) ?? EtaFrom(record.Date("estimatedCompletionTime")),
                State = HumanState(state),
                Severity = severity,
                StatusDetail = messages,
                IsDownloadClient = false
            };
        }
    }

    private (string Title, string? Subtitle) Titles(JsonElement record)
    {
        switch (_flavor.Media)
        {
            case ArrMedia.Series:
            {
                var series = record.Child("series")?.Str("title");
                var episode = record.Child("episode");
                var season = episode?.Int("seasonNumber");
                var number = episode?.Int("episodeNumber");
                var episodeTitle = episode?.Str("title");
                var code = season is not null && number is not null
                    ? $"S{season:00}E{number:00}"
                    : null;
                var title = series ?? record.Str("title") ?? "Episode";
                var subtitle = Join(code, episodeTitle);
                return (title, subtitle);
            }
            case ArrMedia.Movie:
            {
                var movie = record.Child("movie");
                var title = movie?.Str("title") ?? record.Str("title") ?? "Movie";
                var year = movie?.Int("year");
                return (title, year?.ToString(CultureInfo.InvariantCulture));
            }
            default:
            {
                var artist = record.Child("artist")?.Str("artistName");
                var album = record.Child("album")?.Str("title");
                var title = artist ?? record.Str("title") ?? "Album";
                return (title, album);
            }
        }
    }

    private IEnumerable<RemoteCalendarItem> ParseCalendar(JsonElement element)
    {
        foreach (var item in element.AsArray())
        {
            switch (_flavor.Media)
            {
                case ArrMedia.Series:
                {
                    var when = item.Date("airDateUtc") ?? item.Date("airDate");
                    if (when is null)
                        continue;
                    var series = item.Child("series");
                    var season = item.Int("seasonNumber");
                    var number = item.Int("episodeNumber");
                    yield return new RemoteCalendarItem
                    {
                        Key = "sonarr-" + (item.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString("N")),
                        Title = series?.Str("title") ?? "Series",
                        EpisodeTitle = item.Str("title"),
                        Detail = season is not null && number is not null ? $"S{season:00}E{number:00}" : null,
                        When = when.Value,
                        HasFile = item.Bool("hasFile") ?? false,
                        Monitored = item.Bool("monitored") ?? true,
                        PosterPath = Poster(series),
                        SearchId = item.Int("id"),
                        CanSearch = item.Int("id") is not null
                    };
                    break;
                }
                case ArrMedia.Movie:
                {
                    var when = PickMovieDate(item.Date("digitalRelease"), item.Date("physicalRelease"), item.Date("inCinemas"));
                    if (when is null)
                        continue;
                    yield return new RemoteCalendarItem
                    {
                        Key = "radarr-" + (item.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString("N")),
                        Title = item.Str("title") ?? "Movie",
                        EpisodeTitle = null,
                        Detail = item.Int("year")?.ToString(CultureInfo.InvariantCulture),
                        When = when.Value,
                        HasFile = item.Bool("hasFile") ?? false,
                        Monitored = item.Bool("monitored") ?? true,
                        PosterPath = Poster(item),
                        SearchId = item.Int("id"),
                        CanSearch = item.Int("id") is not null
                    };
                    break;
                }
                default:
                {
                    var when = item.Date("releaseDate");
                    if (when is null)
                        continue;
                    var stats = item.Child("statistics");
                    var tracks = stats?.Int("trackFileCount") ?? 0;
                    var artist = item.Child("artist");
                    yield return new RemoteCalendarItem
                    {
                        Key = "lidarr-" + (item.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? Guid.NewGuid().ToString("N")),
                        Title = artist?.Str("artistName") ?? "Artist",
                        EpisodeTitle = item.Str("title"),
                        Detail = "Album",
                        When = when.Value,
                        HasFile = tracks > 0,
                        Monitored = item.Bool("monitored") ?? true,
                        PosterPath = Poster(artist) ?? Poster(item),
                        SearchId = item.Int("id"),
                        CanSearch = item.Int("id") is not null
                    };
                    break;
                }
            }
        }
    }

    private DateTimeOffset? PickMovieDate(DateTimeOffset? digital, DateTimeOffset? physical, DateTimeOffset? cinema)
    {
        var dates = new[] { digital, physical, cinema }.Where(d => d.HasValue).Select(d => d!.Value).ToList();
        if (dates.Count == 0)
            return null;
        var now = _clock.UtcNow;
        var future = dates.Where(d => d >= now).OrderBy(d => d).Cast<DateTimeOffset?>().FirstOrDefault();
        return future ?? dates.Max();
    }

    private IEnumerable<RemoteActivityItem> ParseHistory(JsonElement element)
    {
        var records = element.ValueKind == JsonValueKind.Array ? element.AsArray() : element.Items("records");
        foreach (var record in records)
        {
            var when = record.Date("date");
            if (when is null)
                continue;
            var eventType = record.Str("eventType") ?? "unknown";
            var kind = MapEvent(eventType);
            var title = record.Child("series")?.Str("title")
                ?? record.Child("movie")?.Str("title")
                ?? record.Child("artist")?.Str("artistName")
                ?? record.Str("sourceTitle")
                ?? "History";
            var detail = Join(HumanEvent(eventType), record.Str("sourceTitle"));
            var id = record.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? record.Str("downloadId") ?? when.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            yield return new RemoteActivityItem
            {
                Key = $"{Kind}-{id}",
                When = when.Value,
                Kind = kind,
                Title = title,
                Detail = detail
            };
        }
    }

    private static ActivityKind MapEvent(string eventType)
    {
        var value = eventType.ToLowerInvariant();
        if (value.Contains("fail"))
            return ActivityKind.Failure;
        if (value.Contains("upgrade"))
            return ActivityKind.Upgrade;
        if (value.Contains("import"))
            return ActivityKind.Import;
        if (value.Contains("grab"))
            return ActivityKind.Grab;
        return ActivityKind.Other;
    }

    private static string HumanEvent(string eventType) => eventType switch
    {
        "grabbed" => "Grabbed",
        "downloadFolderImported" => "Imported",
        "seriesFolderImported" => "Imported series folder",
        "downloadFailed" => "Download failed",
        "episodeFileDeleted" => "Episode file deleted",
        "movieFileDeleted" => "Movie file deleted",
        "albumFileDeleted" => "Album file deleted",
        "downloadIgnored" => "Ignored",
        _ => eventType
    };

    private static string? Poster(JsonElement? element)
    {
        if (element is null)
            return null;
        foreach (var image in element.Value.Items("images"))
        {
            var cover = image.Str("coverType");
            if (cover is not null && !cover.Equals("poster", StringComparison.OrdinalIgnoreCase) && !cover.Equals("cover", StringComparison.OrdinalIgnoreCase))
                continue;
            var url = image.Str("url");
            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith('/'))
                return url.TrimStart('/');
        }

        return null;
    }

    private static string? ReadStatusMessages(JsonElement record)
    {
        if (record.Child("statusMessages") is not { } messages || messages.ValueKind != JsonValueKind.Array)
            return null;
        var parts = new List<string>();
        foreach (var message in messages.EnumerateArray())
        {
            var title = message.Str("title");
            var lines = message.Items("messages").Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : null).Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join(" ", lines!);
            var text = string.IsNullOrWhiteSpace(title) ? joined : string.IsNullOrWhiteSpace(joined) ? title : $"{title}: {joined}";
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string HumanState(string state) =>
        string.Concat(state.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString())).Replace('_', ' ');

    private static TimeSpan? ParseTimeLeft(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "00:00:00")
            return null;
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private TimeSpan? EtaFrom(DateTimeOffset? when)
    {
        if (when is null)
            return null;
        var eta = when.Value - _clock.UtcNow;
        return eta < TimeSpan.Zero ? null : eta;
    }

    private static string? Join(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
            return string.IsNullOrWhiteSpace(right) ? null : right;
        if (string.IsNullOrWhiteSpace(right))
            return left;
        return $"{left} · {right}";
    }
}
