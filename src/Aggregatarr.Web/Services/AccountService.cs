using Aggregatarr.Web.Data;
using Aggregatarr.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Aggregatarr.Web.Services;

public sealed class AccountService(AppDbContext db, PasswordService passwords)
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        db.Users.AnyAsync(cancellationToken);

    public async Task<(bool Ok, string? Error, UserAccount? User)> CreateFirstAsync(string username, string password, string confirm, CancellationToken cancellationToken = default)
    {
        if (await AnyAsync(cancellationToken))
            return (false, "An account already exists.", null);

        var nameError = ValidateUsername(username);
        if (nameError is not null)
            return (false, nameError, null);
        if (password.Length < 8)
            return (false, "Use at least 8 characters.", null);
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
            return (false, "The passwords don't match.", null);

        var user = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = passwords.Hash(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null, user);
    }

    public async Task<UserAccount?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var name = username.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name, cancellationToken);
        if (user is null || !passwords.Verify(user, password))
            return null;
        return user;
    }

    public async Task<(bool Ok, string? Error)> ChangePasswordAsync(string username, string current, string next, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (user is null || !passwords.Verify(user, current))
            return (false, "The current password doesn't match.");
        if (next.Length < 8)
            return (false, "Use at least 8 characters.");
        user.PasswordHash = passwords.Hash(user, next);
        await db.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    public static string? ValidateUsername(string username)
    {
        var name = username.Trim();
        if (name.Length is < 3 or > 32)
            return "Username must be 3–32 characters.";
        if (!name.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-'))
            return "Username can use letters, numbers, dots, underscores, and hyphens.";
        return null;
    }
}
