using Aggregatarr.Models;

namespace Aggregatarr.Connectors;

public interface IMediaConnector
{
    ServiceKind Kind { get; }

    Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken);

    Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken);

    Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken);
}
