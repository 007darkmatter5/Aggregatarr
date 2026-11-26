using Aggregatarr.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace Aggregatarr.Web.Security;

public sealed class PasswordService
{
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public string Hash(UserAccount user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(UserAccount user, string password)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
