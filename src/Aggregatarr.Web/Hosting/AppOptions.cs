namespace Aggregatarr.Web.Hosting;

public sealed class AppOptions
{
    public string ConfigPath { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 5;
    public int FullPollEvery { get; set; } = 12;
    public int ServiceTimeoutSeconds { get; set; } = 8;
    public bool DemoMode { get; set; }
}

public static class ConfigPath
{
    public static string Resolve(IConfiguration configuration)
    {
        var configured = configuration["Aggregatarr:ConfigPath"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var env = Environment.GetEnvironmentVariable("AGGREGATARR_CONFIG");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);

        if (Directory.Exists("/config"))
            return "/config";

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "config"));
    }
}
