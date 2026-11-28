namespace Aggregatarr.Web.Theme;

public static class ThemeChoice
{
    public const string Key = "theme";
    public const string Dark = "dark";
    public const string Light = "light";
    public const string Lcars = "lcars";

    public static bool IsKnown(string? value) =>
        value is Dark or Light or Lcars;

    public static string Normalize(string? value) =>
        IsKnown(value) ? value! : Dark;
}
