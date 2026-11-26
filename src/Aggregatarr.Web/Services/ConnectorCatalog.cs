using Aggregatarr.Connectors;
using Aggregatarr.Models;

namespace Aggregatarr.Web.Services;

public sealed class ConnectorCatalog
{
    private readonly Dictionary<ServiceKind, IMediaConnector> _map;

    public ConnectorCatalog(IEnumerable<IMediaConnector> connectors)
    {
        _map = connectors.ToDictionary(c => c.Kind);
    }

    public IMediaConnector Get(ServiceKind kind) =>
        _map.TryGetValue(kind, out var connector)
            ? connector
            : throw new InvalidOperationException($"No connector is registered for {kind}.");
}
