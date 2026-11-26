using System.Globalization;

namespace Aggregatarr.Formatting;

public static class SizeFormat
{
    public static string Bytes(long? bytes) => bytes is null ? "—" : Format(bytes.Value);

    private static string Format(double bytes)
    {
        var sign = bytes < 0 ? "-" : "";
        var value = Math.Abs(bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var digits = value >= 100 || unit == 0 ? "0" : "0.0";
        return sign + value.ToString(digits, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Speed(double? bytesPerSecond)
    {
        if (bytesPerSecond is null or <= 0)
            return "—";
        return Format(bytesPerSecond.Value) + "/s";
    }

    public static string Eta(TimeSpan? eta)
    {
        if (eta is null || eta.Value < TimeSpan.Zero || eta.Value.TotalDays > 60)
            return "—";
        var value = eta.Value;
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        if (value.TotalMinutes >= 1)
            return $"{value.Minutes}m {value.Seconds}s";
        return $"{Math.Max(0, value.Seconds)}s";
    }

    public static string Percent(double progress) =>
        Math.Clamp(progress, 0, 1).ToString("0%", CultureInfo.InvariantCulture);
}
