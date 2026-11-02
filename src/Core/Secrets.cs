using System.Security.Cryptography;
using System.Text;

namespace MishkaHelper.Core;

/// <summary>Secrets are stored encrypted for the current Windows user: a copied settings folder is useless elsewhere.</summary>
public static class Secrets
{
    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string stored) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
}
