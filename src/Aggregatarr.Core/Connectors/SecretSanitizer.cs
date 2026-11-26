namespace Aggregatarr.Connectors;

public static class SecretSanitizer
{
    public static string Clean(string? message, string? apiKey, string? password)
    {
        if (string.IsNullOrEmpty(message))
            return "";

        var cleaned = message;
        if (!string.IsNullOrEmpty(apiKey))
            cleaned = cleaned.Replace(apiKey, "***", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(password))
            cleaned = cleaned.Replace(password, "***", StringComparison.Ordinal);
        return cleaned;
    }

    public static string Clean(string? message, ConnectorContext context) =>
        Clean(message, context.ApiKey, context.Password);
}
