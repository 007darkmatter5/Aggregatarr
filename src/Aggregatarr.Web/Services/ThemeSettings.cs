using Aggregatarr.Web.Data;
using Aggregatarr.Web.Theme;
using Microsoft.EntityFrameworkCore;

namespace Aggregatarr.Web.Services;

/// <summary>
/// The install-wide appearance. SQLite is the source of truth so a choice made
/// on one browser is what the next device gets.
/// </summary>
public sealed class ThemePreference
{
    private readonly object _gate = new();
    private string _current = ThemeChoice.Dark;

    public event Action? Changed;

    public string Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public void Replace(string value, bool notify = true)
    {
        var next = ThemeChoice.Normalize(value);
        lock (_gate)
        {
            if (_current == next)
                return;
            _current = next;
        }

        if (notify)
            Changed?.Invoke();
    }
}

public sealed class ThemeSettings(AppDbContext db, ThemePreference preference)
{
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == ThemeChoice.Key, cancellationToken);
        var value = ThemeChoice.Normalize(row?.Value);
        preference.Replace(value);
        return value;
    }

    public async Task SetAsync(string theme, CancellationToken cancellationToken = default)
    {
        if (!ThemeChoice.IsKnown(theme))
            return;

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == ThemeChoice.Key, cancellationToken);
        if (row is null)
            db.Settings.Add(new AppSetting { Key = ThemeChoice.Key, Value = theme });
        else
            row.Value = theme;

        await db.SaveChangesAsync(cancellationToken);
        preference.Replace(theme);
    }
}

public static class SchemaBootstrap
{
    public static Task EnsureSettingsAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Settings" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_Settings" PRIMARY KEY,
                "Value" TEXT NOT NULL
            );
            """);
}
