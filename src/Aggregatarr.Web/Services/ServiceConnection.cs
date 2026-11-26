using Aggregatarr.Models;

namespace Aggregatarr.Web.Services;

/// <summary>
/// Decrypted connection details. Server-side only: never pass this to a component parameter.
/// </summary>
public sealed class ServiceConnection
{
    public required Guid Id { get; init; }
    public required ServiceKind Kind { get; init; }
    public required string Name { get; init; }
    public required Uri BaseUri { get; init; }
    public string? ApiKey { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool Enabled { get; init; }

    public override string ToString() => $"{Kind.DisplayName()} {BaseUri.GetLeftPart(UriPartial.Authority)}";
}

public sealed class ServiceDraft
{
    public Guid? Id { get; set; }
    public ServiceKind Kind { get; set; } = ServiceKind.Sonarr;
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string? Username { get; set; }

    /// <summary>Only the value the operator just typed. Blank means "keep the stored key".</summary>
    public string? ApiKey { get; set; }

    public string? Password { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed record ActionLogView(
    DateTimeOffset At,
    string Username,
    string Action,
    string? ServiceName,
    string? Target,
    bool Success,
    string? Detail);
