using System.Globalization;
using System.Text.Json;

namespace Aggregatarr.Connectors;

internal static class JsonValues
{
    public static string? Str(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static int? Int(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public static long? Long(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public static double? Dbl(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    public static bool? Bool(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static DateTimeOffset? Date(this JsonElement element, string name)
    {
        var text = element.Str(name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed;
        return null;
    }

    public static JsonElement? Child(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return value;
    }

    public static IEnumerable<JsonElement> Items(this JsonElement element, string name)
    {
        if (!Try(element, name, out var value) || value.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in value.EnumerateArray())
            yield return item;
    }

    public static IEnumerable<JsonElement> AsArray(this JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in element.EnumerateArray())
            yield return item;
    }

    private static bool Try(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }
}
