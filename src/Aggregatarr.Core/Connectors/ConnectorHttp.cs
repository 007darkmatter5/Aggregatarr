using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aggregatarr.Connectors;

internal static class ConnectorHttp
{
    public static async Task<JsonElement> SendJsonAsync(
        HttpClient http,
        HttpMethod method,
        string path,
        ILogger logger,
        string serviceName,
        string? apiKey,
        string? password,
        IEnumerable<KeyValuePair<string, string>>? headers,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (headers is not null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (content is not null)
            request.Content = content;

        logger.LogInformation("{Service} {Method} {Path}", serviceName, method.Method, path);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{serviceName} returned {(int)response.StatusCode} for {path}. {SecretSanitizer.Clean(Trim(body), apiKey, password)}");
        }

        if (string.IsNullOrWhiteSpace(body))
            return default;

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    public static async Task<string> SendTextAsync(
        HttpClient http,
        HttpMethod method,
        string path,
        ILogger logger,
        string serviceName,
        string? apiKey,
        string? password,
        IEnumerable<KeyValuePair<string, string>>? headers,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (headers is not null)
        {
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (content is not null)
            request.Content = content;

        logger.LogInformation("{Service} {Method} {Path}", serviceName, method.Method, path);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{serviceName} returned {(int)response.StatusCode} for {path}. {SecretSanitizer.Clean(Trim(body), apiKey, password)}");
        }

        return body.Trim().Trim('"');
    }

    public static IEnumerable<KeyValuePair<string, string>> ApiKeyHeader(string headerName, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            yield break;
        yield return new KeyValuePair<string, string>(headerName, apiKey);
    }

    private static string Trim(string body) =>
        body.Length <= 180 ? body : body[..180];
}
