using Aggregatarr.Models;

namespace Aggregatarr.Web.Data;

public sealed class UserAccount
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ServiceEntity
{
    public Guid Id { get; set; }
    public ServiceKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? ApiKeyProtected { get; set; }
    public string? Username { get; set; }
    public string? PasswordProtected { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class ActionLogEntity
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Username { get; set; } = "";
    public string Action { get; set; } = "";
    public string? ServiceName { get; set; }
    public string? Target { get; set; }
    public bool Success { get; set; }
    public string? Detail { get; set; }
}
