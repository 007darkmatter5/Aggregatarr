namespace Aggregatarr.Models;

public enum ServiceKind
{
    Sonarr,
    Radarr,
    Lidarr,
    Prowlarr,
    Bazarr,
    QBittorrent,
    Sabnzbd
}

public static class ServiceKindInfo
{
    public static string DisplayName(this ServiceKind kind) => kind switch
    {
        ServiceKind.QBittorrent => "qBittorrent",
        ServiceKind.Sabnzbd => "SABnzbd",
        _ => kind.ToString()
    };

    public static bool UsesApiKey(this ServiceKind kind) => kind != ServiceKind.QBittorrent;

    public static bool UsesPassword(this ServiceKind kind) => kind == ServiceKind.QBittorrent;

    public static int DefaultPort(this ServiceKind kind) => kind switch
    {
        ServiceKind.Sonarr => 8989,
        ServiceKind.Radarr => 7878,
        ServiceKind.Lidarr => 8686,
        ServiceKind.Prowlarr => 9696,
        ServiceKind.Bazarr => 6767,
        ServiceKind.QBittorrent => 8080,
        ServiceKind.Sabnzbd => 8080,
        _ => 80
    };

    public static string CredentialHint(this ServiceKind kind) => kind switch
    {
        ServiceKind.Sonarr => "Sonarr → Settings → General → Security → API Key.",
        ServiceKind.Radarr => "Radarr → Settings → General → Security → API Key.",
        ServiceKind.Lidarr => "Lidarr → Settings → General → Security → API Key.",
        ServiceKind.Prowlarr => "Prowlarr → Settings → General → Security → API Key.",
        ServiceKind.Bazarr => "Bazarr → Settings → General → Security → API Key.",
        ServiceKind.QBittorrent => "qBittorrent → Tools → Options → Web UI → Authentication.",
        ServiceKind.Sabnzbd => "SABnzbd → Settings → General → Security → API Key. Requires SABnzbd 4+ so the key can be sent as a header.",
        _ => ""
    };

    public static bool IsDownloadClient(this ServiceKind kind) =>
        kind is ServiceKind.QBittorrent or ServiceKind.Sabnzbd;

    public static bool HasCalendar(this ServiceKind kind) =>
        kind is ServiceKind.Sonarr or ServiceKind.Radarr or ServiceKind.Lidarr;

    public static bool CanSearchMissing(this ServiceKind kind) =>
        kind is ServiceKind.Sonarr or ServiceKind.Radarr or ServiceKind.Lidarr;
}
