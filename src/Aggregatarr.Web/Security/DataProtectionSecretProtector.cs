using Microsoft.AspNetCore.DataProtection;

namespace Aggregatarr.Web.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string payload);
}

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Aggregatarr.Credentials.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string payload) => _protector.Unprotect(payload);
}
