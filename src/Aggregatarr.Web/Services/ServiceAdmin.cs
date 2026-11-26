using Aggregatarr.Connectors;
using Aggregatarr.Models;
using Aggregatarr.Web.Data;
using Aggregatarr.Web.Hosting;
using Aggregatarr.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Aggregatarr.Web.Services;

public sealed class ServiceAdmin(
    AppDbContext db,
    ISecretProtector protector,
    ConnectorCatalog connectors,
    IServiceHttp http,
    AppOptions options,
    IClock clock)
{
    public async Task<IReadOnlyList<ServiceListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Services.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        return rows.Select(ToListItem).ToList();
    }

    public async Task<IReadOnlyList<ServiceConnection>> GetEnabledConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Services.Where(s => s.Enabled).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        return rows.Select(ToConnection).Where(c => c is not null).Cast<ServiceConnection>().ToList();
    }

    public async Task<ServiceConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        return row is null ? null : ToConnection(row);
    }

    public async Task<(bool Ok, string? Error)> SaveAsync(ServiceDraft draft, CancellationToken cancellationToken = default)
    {
        var name = draft.Name.Trim();
        if (name.Length is < 1 or > 80)
            return (false, "Give the service a name.");

        var uri = NormalizeBaseUri(draft.BaseUrl);
        if (uri is null)
            return (false, "Enter an http or https URL.");

        ServiceEntity entity;
        var creating = draft.Id is null || draft.Id == Guid.Empty;
        if (creating)
        {
            if (draft.Kind.UsesApiKey() && string.IsNullOrWhiteSpace(draft.ApiKey))
                return (false, "An API key is required.");
            if (draft.Kind.UsesPassword() && (string.IsNullOrWhiteSpace(draft.Username) || string.IsNullOrWhiteSpace(draft.Password)))
                return (false, "A WebUI username and password are required.");

            var sort = await db.Services.Select(s => (int?)s.SortOrder).MaxAsync(cancellationToken) ?? 0;
            entity = new ServiceEntity
            {
                Id = Guid.NewGuid(),
                SortOrder = sort + 1
            };
            db.Services.Add(entity);
        }
        else
        {
            entity = await db.Services.FirstAsync(s => s.Id == draft.Id, cancellationToken);
        }

        entity.Kind = draft.Kind;
        entity.Name = name;
        entity.BaseUrl = uri.ToString();
        entity.Enabled = draft.Enabled;
        entity.Username = string.IsNullOrWhiteSpace(draft.Username) ? entity.Username : draft.Username.Trim();
        if (draft.Kind.UsesPassword() && string.IsNullOrWhiteSpace(entity.Username))
            return (false, "A WebUI username is required.");

        if (!string.IsNullOrWhiteSpace(draft.ApiKey))
            entity.ApiKeyProtected = protector.Protect(draft.ApiKey.Trim());
        if (!string.IsNullOrWhiteSpace(draft.Password))
            entity.PasswordProtected = protector.Protect(draft.Password);

        if (creating && draft.Kind.UsesApiKey() && entity.ApiKeyProtected is null)
            return (false, "An API key is required.");
        if (draft.Kind.UsesPassword() && entity.PasswordProtected is null)
            return (false, "A WebUI password is required.");

        await db.SaveChangesAsync(cancellationToken);
        draft.Id = entity.Id;
        draft.ApiKey = null;
        draft.Password = null;
        return (true, null);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
            return;
        db.Services.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var entity = await db.Services.FirstAsync(s => s.Id == id, cancellationToken);
        entity.Enabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConnectionTestResult> TestAsync(ServiceDraft draft, CancellationToken cancellationToken = default)
    {
        if (options.DemoMode)
            return new ConnectionTestResult(true, DemoVersion(draft.Kind), null);

        var uri = NormalizeBaseUri(draft.BaseUrl);
        if (uri is null)
            return new ConnectionTestResult(false, null, "Enter an http or https URL.");

        string? apiKey = string.IsNullOrWhiteSpace(draft.ApiKey) ? null : draft.ApiKey.Trim();
        string? password = string.IsNullOrWhiteSpace(draft.Password) ? null : draft.Password;
        string? username = string.IsNullOrWhiteSpace(draft.Username) ? null : draft.Username.Trim();

        if (draft.Id is Guid id && id != Guid.Empty)
        {
            var existing = await db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (existing is not null)
            {
                apiKey ??= Unprotect(existing.ApiKeyProtected);
                password ??= Unprotect(existing.PasswordProtected);
                username ??= existing.Username;
            }
        }

        return await ProbeAsync(draft.Kind, uri, apiKey, username, password, cancellationToken);
    }

    public async Task<ConnectionTestResult> TestSavedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.Services.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (entity is null)
            return new ConnectionTestResult(false, null, "That service was removed.");
        if (options.DemoMode)
            return new ConnectionTestResult(true, DemoVersion(entity.Kind), null);

        var connection = ToConnection(entity);
        if (connection is null)
            return new ConnectionTestResult(false, null, "The saved URL is not valid.");
        return await ProbeAsync(connection.Kind, connection.BaseUri, connection.ApiKey, connection.Username, connection.Password, cancellationToken);
    }

    public async Task<IReadOnlyList<ActionLogView>> RecentActionsAsync(int take = 40, CancellationToken cancellationToken = default)
    {
        var rows = await db.ActionLogs.OrderByDescending(a => a.Id).Take(take).ToListAsync(cancellationToken);
        return rows.Select(a => new ActionLogView(a.At, a.Username, a.Action, a.ServiceName, a.Target, a.Success, a.Detail)).ToList();
    }

    private async Task<ConnectionTestResult> ProbeAsync(ServiceKind kind, Uri uri, string? apiKey, string? username, string? password, CancellationToken cancellationToken)
    {
        var connector = connectors.Get(kind);
        using var client = http.Create(uri, TimeSpan.FromSeconds(Math.Clamp(options.ServiceTimeoutSeconds, 2, 30)));
        var context = new ConnectorContext
        {
            Http = client,
            BaseUri = uri,
            ApiKey = apiKey,
            Username = username,
            Password = password,
            CalendarStart = clock.UtcNow,
            CalendarEnd = clock.UtcNow
        };
        return await connector.TestAsync(context, cancellationToken);
    }

    private ServiceListItem ToListItem(ServiceEntity entity) => new()
    {
        Id = entity.Id,
        Kind = entity.Kind,
        Name = entity.Name,
        BaseUrl = entity.BaseUrl,
        Enabled = entity.Enabled,
        HasApiKey = !string.IsNullOrEmpty(entity.ApiKeyProtected),
        HasPassword = !string.IsNullOrEmpty(entity.PasswordProtected),
        Username = entity.Username
    };

    private ServiceConnection? ToConnection(ServiceEntity entity)
    {
        var uri = NormalizeBaseUri(entity.BaseUrl);
        if (uri is null)
            return null;
        return new ServiceConnection
        {
            Id = entity.Id,
            Kind = entity.Kind,
            Name = entity.Name,
            BaseUri = uri,
            ApiKey = Unprotect(entity.ApiKeyProtected),
            Username = entity.Username,
            Password = Unprotect(entity.PasswordProtected),
            Enabled = entity.Enabled
        };
    }

    private string? Unprotect(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return null;
        return protector.Unprotect(payload);
    }

    public static Uri? NormalizeBaseUri(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        var text = input.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme is not ("http" or "https"))
            return null;
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";
        return builder.Uri;
    }

    private static string DemoVersion(ServiceKind kind) => kind switch
    {
        ServiceKind.Sonarr => "4.0.15.2942",
        ServiceKind.Radarr => "5.18.4.9674",
        ServiceKind.Lidarr => "2.11.2.4629",
        ServiceKind.Prowlarr => "1.31.2.4978",
        ServiceKind.Bazarr => "1.5.1",
        ServiceKind.QBittorrent => "v5.0.2",
        ServiceKind.Sabnzbd => "4.4.1",
        _ => "demo"
    };
}
