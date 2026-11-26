using System.Globalization;
using System.Text.Json;
using Aggregatarr.Models;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

public sealed class ProwlarrConnector : IMediaConnector
{
    private readonly ILogger _logger;

    public ProwlarrConnector(ILoggerFactory loggers)
    {
        _logger = loggers.CreateLogger("Aggregatarr.Connectors.Prowlarr");
    }

    public ServiceKind Kind => ServiceKind.Prowlarr;

    public async Task<ConnectionTestResult> TestAsync(ConnectorContext context, CancellationToken cancellationToken)
    {
        try
        {
            var status = await GetAsync(context, "api/v1/system/status", cancellationToken);
            return new ConnectionTestResult(true, status.Str("version"), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(false, null, SecretSanitizer.Clean(ex.Message, context));
        }
    }

    public async Task<ConnectorSnapshot> PollAsync(ConnectorContext context, PollDepth depth, CancellationToken cancellationToken)
    {
        var status = await GetAsync(context, "api/v1/system/status", cancellationToken);
        var warnings = new List<HealthWarning>();
        try
        {
            var health = await GetAsync(context, "api/v1/health", cancellationToken);
            warnings.AddRange(ServarrConnector.ParseHealth(health));
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
            try
            {
                var history = await GetAsync(context, "api/v1/history?page=1&pageSize=40&sortKey=date&sortDirection=descending", cancellationToken);
                activity.AddRange(ParseHistory(history));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add(new HealthWarning("warning", SecretSanitizer.Clean(ex.Message, context), "history"));
            }
        }

        return new ConnectorSnapshot
        {
            Version = status.Str("version"),
            Warnings = warnings,
            Activity = activity
        };
    }

    public Task ExecuteAsync(ConnectorContext context, MediaAction action, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Prowlarr has no queue controls in Aggregatarr.");

    private Task<JsonElement> GetAsync(ConnectorContext context, string path, CancellationToken cancellationToken) =>
        ConnectorHttp.SendJsonAsync(
            context.Http,
            HttpMethod.Get,
            path,
            _logger,
            "Prowlarr",
            context.ApiKey,
            context.Password,
            ConnectorHttp.ApiKeyHeader("X-Api-Key", context.ApiKey),
            null,
            cancellationToken);

    private static IEnumerable<RemoteActivityItem> ParseHistory(JsonElement element)
    {
        var records = element.ValueKind == JsonValueKind.Array ? element.AsArray() : element.Items("records");
        foreach (var record in records)
        {
            var when = record.Date("date");
            if (when is null)
                continue;
            var eventType = record.Str("eventType") ?? "";
            var successful = record.Bool("successful") ?? true;
            var kind = !successful || eventType.Contains("fail", StringComparison.OrdinalIgnoreCase)
                ? ActivityKind.Failure
                : eventType.Equals("releaseGrabbed", StringComparison.OrdinalIgnoreCase)
                    ? ActivityKind.Grab
                    : ActivityKind.Other;
            if (kind == ActivityKind.Other && !eventType.Equals("releaseGrabbed", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = record.Int("id")?.ToString(CultureInfo.InvariantCulture) ?? when.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            yield return new RemoteActivityItem
            {
                Key = "prowlarr-" + id,
                When = when.Value,
                Kind = kind,
                Title = record.Str("sourceTitle") ?? record.Str("indexer") ?? "Indexer",
                Detail = string.Join(" · ", new[] { record.Str("indexer"), record.Str("downloadClient") }.Where(s => !string.IsNullOrWhiteSpace(s)))
            };
        }
    }
}
