using Aggregatarr.Models;
using Aggregatarr.Web.Data;
using Aggregatarr.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Aggregatarr.Web.Services;

public static class DemoSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var passwords = services.GetRequiredService<PasswordService>();
        var protector = services.GetRequiredService<ISecretProtector>();

        if (!await db.Users.AnyAsync(cancellationToken))
        {
            var user = new UserAccount
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                Username = "demo",
                CreatedAt = DateTimeOffset.UtcNow
            };
            user.PasswordHash = passwords.Hash(user, "demo");
            db.Users.Add(user);
        }

        if (!await db.Services.AnyAsync(cancellationToken))
        {
            db.Services.AddRange(
                Service(DemoIds.Sonarr, ServiceKind.Sonarr, "Sonarr", "http://sonarr.example:8989/", "demo-sonarr-api-key-not-real", null, null, protector, 1),
                Service(DemoIds.Radarr, ServiceKind.Radarr, "Radarr", "http://radarr.example:7878/", "demo-radarr-api-key-not-real", null, null, protector, 2),
                Service(DemoIds.Lidarr, ServiceKind.Lidarr, "Lidarr", "http://lidarr.example:8686/", "demo-lidarr-api-key-not-real", null, null, protector, 3),
                Service(DemoIds.Prowlarr, ServiceKind.Prowlarr, "Prowlarr", "http://prowlarr.example:9696/", "demo-prowlarr-api-key-not-real", null, null, protector, 4),
                Service(DemoIds.Bazarr, ServiceKind.Bazarr, "Bazarr", "http://bazarr.example:6767/", "demo-bazarr-api-key-not-real", null, null, protector, 5),
                Service(DemoIds.Qbit, ServiceKind.QBittorrent, "qBittorrent", "http://gluetun.example:8080/", null, "demo-user", "demo-password-not-real", protector, 6),
                Service(DemoIds.Sab, ServiceKind.Sabnzbd, "SABnzbd", "http://gluetun.example:8085/", "demo-sabnzbd-api-key-not-real", null, null, protector, 7));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ServiceEntity Service(Guid id, ServiceKind kind, string name, string url, string? apiKey, string? username, string? password, ISecretProtector protector, int sort) =>
        new()
        {
            Id = id,
            Kind = kind,
            Name = name,
            BaseUrl = url,
            ApiKeyProtected = apiKey is null ? null : protector.Protect(apiKey),
            Username = username,
            PasswordProtected = password is null ? null : protector.Protect(password),
            Enabled = true,
            SortOrder = sort
        };
}
