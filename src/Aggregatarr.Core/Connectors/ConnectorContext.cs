namespace Aggregatarr.Connectors;

/// <summary>
/// Credentials live only on the server. <see cref="ToString"/> deliberately omits them
/// so an accidental log of the context cannot print a key or password.
/// </summary>
public sealed class ConnectorContext
{
    public required HttpClient Http { get; init; }
    public required Uri BaseUri { get; init; }
    public string? ApiKey { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public DateTimeOffset CalendarStart { get; init; }
    public DateTimeOffset CalendarEnd { get; init; }

    public override string ToString() => BaseUri.GetLeftPart(UriPartial.Authority);
}
