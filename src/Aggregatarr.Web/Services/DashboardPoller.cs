using System.Threading.Channels;
using Aggregatarr.Connectors;
using Aggregatarr.Merging;
using Aggregatarr.Models;
using Aggregatarr.Web.Hosting;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Web.Services;

public sealed class DashboardPoller : BackgroundService, IDashboardRefresh
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConnectorCatalog _catalog;
    private readonly IServiceHttp _http;
    private readonly DashboardStore _store;
    private readonly IClock _clock;
    private readonly AppOptions _options;
    private readonly ILogger<DashboardPoller> _logger;
    private readonly Channel<bool> _kicks = Channel.CreateBounded<bool>(1);
    private readonly Dictionary<Guid, ConnectorSnapshot> _memory = new();

    public DashboardPoller(
        IServiceScopeFactory scopes,
        ConnectorCatalog catalog,
        IServiceHttp http,
        DashboardStore store,
        IClock clock,
        AppOptions options,
        ILogger<DashboardPoller> logger)
    {
        _scopes = scopes;
        _catalog = catalog;
        _http = http;
        _store = store;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public void Kick() => _kicks.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cycle = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var full = cycle % Math.Max(1, _options.FullPollEvery) == 0;
            try
            {
                await PollAsync(full ? PollDepth.Full : PollDepth.Fast, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard poll failed");
            }

            cycle++;
            await WaitAsync(stoppingToken);
        }
    }

    private async Task WaitAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.PollIntervalSeconds, 2, 120)));
        try
        {
            await _kicks.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PollAsync(PollDepth depth, CancellationToken stoppingToken)
    {
        using var scope = _scopes.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<ServiceAdmin>();
        var connections = await admin.GetEnabledConnectionsAsync(stoppingToken);
        var (start, end) = _store.CalendarWindow;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.ServiceTimeoutSeconds, 2, 60));

        var polls = await Task.WhenAll(connections.Select(connection =>
            PollOneAsync(connection, depth, start, end, timeout, stoppingToken)));

        var known = connections.Select(c => c.Id).ToHashSet();
        foreach (var stale in _memory.Keys.Where(id => !known.Contains(id)).ToList())
            _memory.Remove(stale);

        var sources = new List<QueueSource>();
        var calendar = new List<CalendarItem>();
        var activity = new List<ActivityItem>();
        var health = new List<ServiceHealth>();
        var alerts = new List<AlertItem>();

        foreach (var poll in polls)
        {
            health.Add(poll.Health);
            if (!poll.Health.Online || poll.Snapshot is null)
            {
                alerts.Add(new AlertItem
                {
                    Key = "offline-" + poll.Health.ServiceId,
                    ServiceId = poll.Health.ServiceId,
                    ServiceName = poll.Health.Name,
                    Kind = poll.Health.Kind,
                    Level = "error",
                    Message = poll.Health.Error ?? "Unreachable",
                    Source = "connection"
                });
                continue;
            }

            _memory[poll.Health.ServiceId] = poll.Snapshot;
            var snapshot = poll.Snapshot;

            foreach (var item in snapshot.Queue)
            {
                sources.Add(new QueueSource
                {
                    ServiceId = poll.Health.ServiceId,
                    ServiceName = poll.Health.Name,
                    Kind = poll.Health.Kind,
                    Item = item
                });
            }

            foreach (var item in snapshot.Calendar)
            {
                var poster = string.IsNullOrWhiteSpace(item.PosterPath)
                    ? null
                    : $"/media/{poll.Health.ServiceId:D}/{item.PosterPath}";
                calendar.Add(new CalendarItem
                {
                    Key = poll.Health.ServiceId.ToString("N") + "-" + item.Key,
                    Source = poll.Health.Kind,
                    SourceName = poll.Health.Name,
                    ServiceId = poll.Health.ServiceId,
                    Title = item.Title,
                    EpisodeTitle = item.EpisodeTitle,
                    Detail = item.Detail,
                    When = item.When,
                    State = CalendarClassifier.Classify(item.Monitored, item.HasFile, item.When, _clock.UtcNow),
                    PosterUrl = poster,
                    CanSearch = item.CanSearch && poll.Health.Kind.CanSearchMissing(),
                    SearchId = item.SearchId
                });
            }

            foreach (var item in snapshot.Activity)
            {
                activity.Add(new ActivityItem
                {
                    Key = poll.Health.ServiceId.ToString("N") + "-" + item.Key,
                    When = item.When,
                    Source = poll.Health.Kind,
                    SourceName = poll.Health.Name,
                    Kind = item.Kind,
                    Title = item.Title,
                    Detail = item.Detail
                });
            }

            foreach (var warning in snapshot.Warnings)
            {
                alerts.Add(new AlertItem
                {
                    Key = poll.Health.ServiceId.ToString("N") + "-" + warning.Level + "-" + warning.Message,
                    ServiceId = poll.Health.ServiceId,
                    ServiceName = poll.Health.Name,
                    Kind = poll.Health.Kind,
                    Level = warning.Level,
                    Message = warning.Message,
                    Source = warning.Source
                });
            }
        }

        alerts = alerts
            .GroupBy(a => a.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(a => a.Level == "error")
            .ThenBy(a => a.ServiceName)
            .ToList();

        _store.Publish(new DashboardSnapshot
        {
            UpdatedAt = _clock.UtcNow,
            DemoMode = false,
            Queue = QueueMerger.Merge(sources),
            Calendar = calendar.OrderBy(c => c.When).ToList(),
            Activity = ActivityFeed.Merge(activity),
            Services = health.OrderBy(h => h.Name).ToList(),
            Alerts = alerts
        });
    }

    private async Task<OnePoll> PollOneAsync(
        ServiceConnection connection,
        PollDepth depth,
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan timeout,
        CancellationToken stoppingToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(timeout);
        try
        {
            using var client = _http.Create(connection.BaseUri, timeout);
            var context = new ConnectorContext
            {
                Http = client,
                BaseUri = connection.BaseUri,
                ApiKey = connection.ApiKey,
                Username = connection.Username,
                Password = connection.Password,
                CalendarStart = start,
                CalendarEnd = end
            };

            var fresh = await _catalog.Get(connection.Kind).PollAsync(context, depth, cts.Token);
            var merged = MergeMemory(connection.Id, fresh, depth);
            return new OnePoll(new ServiceHealth
            {
                ServiceId = connection.Id,
                Name = connection.Name,
                Kind = connection.Kind,
                Online = true,
                Version = merged.Version,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                ClientPaused = merged.ClientPaused,
                Warnings = merged.Warnings
            }, merged);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            var message = ex is OperationCanceledException
                ? $"Timed out after {timeout.TotalSeconds:0}s"
                : SecretSanitizer.Clean(ex.Message, connection.ApiKey, connection.Password);
            _logger.LogWarning("Service {Name} is unreachable: {Reason}", connection.Name, message);
            return new OnePoll(new ServiceHealth
            {
                ServiceId = connection.Id,
                Name = connection.Name,
                Kind = connection.Kind,
                Online = false,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds,
                Error = message
            }, null);
        }
    }

    private ConnectorSnapshot MergeMemory(Guid id, ConnectorSnapshot fresh, PollDepth depth)
    {
        if (depth == PollDepth.Full || !_memory.TryGetValue(id, out var previous))
            return fresh;

        return new ConnectorSnapshot
        {
            Version = fresh.Version ?? previous.Version,
            Warnings = fresh.Warnings,
            Queue = fresh.Queue,
            Calendar = previous.Calendar,
            Activity = previous.Activity,
            ClientPaused = fresh.ClientPaused
        };
    }

    private sealed record OnePoll(ServiceHealth Health, ConnectorSnapshot? Snapshot);
}
