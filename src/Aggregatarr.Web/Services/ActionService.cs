using Aggregatarr.Connectors;
using Aggregatarr.Models;
using Aggregatarr.Web.Data;
using Aggregatarr.Web.Hosting;
using Microsoft.AspNetCore.Components.Authorization;

namespace Aggregatarr.Web.Services;

public sealed record ActionOutcome(bool Ok, string Message);

public interface IActionService
{
    Task<ActionOutcome> PauseAsync(QueueItem item);
    Task<ActionOutcome> ResumeAsync(QueueItem item);
    Task<ActionOutcome> PauseAllAsync(Guid serviceId);
    Task<ActionOutcome> ResumeAllAsync(Guid serviceId);
    Task<ActionOutcome> RemoveAsync(QueueItem item);
    Task<ActionOutcome> SearchAsync(CalendarItem item);
    Task<ActionOutcome> SearchAllMissingAsync();
}

public sealed class ActionService(
    AppDbContext db,
    ServiceAdmin admin,
    ConnectorCatalog catalog,
    IServiceHttp http,
    AppOptions options,
    IClock clock,
    AuthenticationStateProvider auth,
    DemoWorld demo,
    IDashboardRefresh refresh) : IActionService
{
    public Task<ActionOutcome> PauseAsync(QueueItem item) =>
        RunClientAsync(item, "pause", item.ClientItemId, id => new PauseItemAction(id));

    public Task<ActionOutcome> ResumeAsync(QueueItem item) =>
        RunClientAsync(item, "resume", item.ClientItemId, id => new ResumeItemAction(id));

    public Task<ActionOutcome> PauseAllAsync(Guid serviceId) =>
        RunServiceAsync(serviceId, "pause-all", null, new PauseAllAction());

    public Task<ActionOutcome> ResumeAllAsync(Guid serviceId) =>
        RunServiceAsync(serviceId, "resume-all", null, new ResumeAllAction());

    public Task<ActionOutcome> RemoveAsync(QueueItem item)
    {
        if (!string.IsNullOrEmpty(item.ClientItemId))
            return RunClientAsync(item, "remove", item.ClientItemId, id => new RemoveItemAction(id));
        if (item.ArrServiceId is Guid arrId && !string.IsNullOrEmpty(item.ArrQueueId))
            return RunServiceAsync(arrId, "remove", item.Title, new RemoveItemAction(item.ArrQueueId));
        return Task.FromResult(new ActionOutcome(false, "This item cannot be removed from here."));
    }

    public async Task<ActionOutcome> SearchAsync(CalendarItem item)
    {
        if (item.SearchId is not int id)
            return new ActionOutcome(false, "This entry has nothing to search.");
        return await RunServiceAsync(item.ServiceId, "search", item.Title, new SearchIdsAction([id]));
    }

    public async Task<ActionOutcome> SearchAllMissingAsync()
    {
        var connections = await admin.GetEnabledConnectionsAsync();
        var targets = connections.Where(c => c.Kind.CanSearchMissing()).ToList();
        if (targets.Count == 0)
            return new ActionOutcome(false, "Add Sonarr, Radarr, or Lidarr before searching.");

        var notes = new List<string>();
        var ok = true;
        foreach (var connection in targets)
        {
            var outcome = await ExecuteAsync(connection, "search-all-missing", connection.Name, new SearchAllMissingAction());
            ok &= outcome.Ok;
            notes.Add($"{connection.Name}: {(outcome.Ok ? "started" : outcome.Message)}");
        }

        refresh.Kick();
        return new ActionOutcome(ok, string.Join(" ", notes));
    }

    private Task<ActionOutcome> RunClientAsync(QueueItem item, string action, string? itemId, Func<string, MediaAction> build)
    {
        if (!item.CanPause && action is "pause" or "resume")
            return Task.FromResult(new ActionOutcome(false, "Pause and resume run on qBittorrent or SABnzbd."));
        if (string.IsNullOrEmpty(itemId))
            return Task.FromResult(new ActionOutcome(false, "This row is not tied to a download client item."));
        return RunServiceAsync(item.ClientServiceId, action, item.Title, build(itemId));
    }

    private async Task<ActionOutcome> RunServiceAsync(Guid serviceId, string action, string? target, MediaAction mediaAction)
    {
        var connection = await admin.GetConnectionAsync(serviceId);
        if (connection is null)
            return await LogAsync(action, null, target, false, "That service is no longer configured.");
        var outcome = await ExecuteAsync(connection, action, target, mediaAction);
        refresh.Kick();
        return outcome;
    }

    private async Task<ActionOutcome> ExecuteAsync(ServiceConnection connection, string action, string? target, MediaAction mediaAction)
    {
        try
        {
            if (options.DemoMode)
            {
                demo.Apply(connection.Id, mediaAction, target);
                return await LogAsync(action, connection.Name, target, true, "Demo mode recorded the action.");
            }

            using var client = http.Create(connection.BaseUri, TimeSpan.FromSeconds(20));
            var context = new ConnectorContext
            {
                Http = client,
                BaseUri = connection.BaseUri,
                ApiKey = connection.ApiKey,
                Username = connection.Username,
                Password = connection.Password,
                CalendarStart = clock.UtcNow,
                CalendarEnd = clock.UtcNow
            };
            await catalog.Get(connection.Kind).ExecuteAsync(context, mediaAction, CancellationToken.None);
            return await LogAsync(action, connection.Name, target, true, null);
        }
        catch (Exception ex)
        {
            var message = SecretSanitizer.Clean(ex.Message, connection.ApiKey, connection.Password);
            return await LogAsync(action, connection.Name, target, false, message);
        }
    }

    private async Task<ActionOutcome> LogAsync(string action, string? service, string? target, bool success, string? detail)
    {
        var state = await auth.GetAuthenticationStateAsync();
        var username = state.User.Identity?.Name ?? "unknown";
        var safeDetail = detail is null ? null : SecretSanitizer.Clean(detail, null, null);
        if (safeDetail is { Length: > 500 })
            safeDetail = safeDetail[..500];

        db.ActionLogs.Add(new ActionLogEntity
        {
            At = clock.UtcNow,
            Username = username,
            Action = action,
            ServiceName = service,
            Target = target,
            Success = success,
            Detail = safeDetail
        });
        await db.SaveChangesAsync();
        var message = success
            ? Describe(action, service, target)
            : safeDetail ?? "The action failed.";
        return new ActionOutcome(success, message);
    }

    private static string Describe(string action, string? service, string? target) => action switch
    {
        "pause" => $"Paused {target}.",
        "resume" => $"Resumed {target}.",
        "pause-all" => $"Paused everything on {service}.",
        "resume-all" => $"Resumed everything on {service}.",
        "remove" => $"Removed {target}.",
        "search" => $"Search started for {target}.",
        "search-all-missing" => $"Missing search started on {service}.",
        _ => "Done."
    };
}
