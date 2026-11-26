using Microsoft.Extensions.Logging;

namespace Aggregatarr.Web.Services;

public sealed class MediaFetcher(IServiceHttp http, ILogger<MediaFetcher> logger)
{
    public async Task<(byte[] Bytes, string ContentType)?> FetchAsync(ServiceConnection connection, string relativePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal)
            || relativePath.Contains("://", StringComparison.Ordinal)
            || relativePath.StartsWith('/'))
            return null;

        if (!relativePath.StartsWith("MediaCover/", StringComparison.OrdinalIgnoreCase))
            return null;

        using var client = http.Create(connection.BaseUri, TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        if (!string.IsNullOrEmpty(connection.ApiKey))
            request.Headers.TryAddWithoutValidation("X-Api-Key", connection.ApiKey);

        logger.LogInformation("Poster {Service} {Path}", connection.Name, relativePath);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var type = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return null;
        return (bytes, type);
    }
}
