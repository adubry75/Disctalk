using System.Security.Cryptography;
using System.Text;

namespace DisctalkClient.Services;

// Unpackaged Windows apps may not get SecureStorage; fall back to DPAPI (current user) encrypted files.
public sealed partial class SecureSecretStore
{
    static string FallbackPath(string key)
    {
        // Same file the bot token always used, so an existing catbot token keeps working.
        var name = key == SecureTokenStore.Key(Core.Abstractions.AccountKind.Bot) ? "token" : key.Replace("disctalkclient.", "");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisctalkClient", name + ".bin");
    }

    partial void FallbackGet(string key, ref string? value)
    {
        var path = FallbackPath(key);
        if (!File.Exists(path)) return;
        try
        {
            value = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            File.Delete(path);
        }
    }

    partial void FallbackSet(string key, string value)
    {
        var path = FallbackPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    partial void FallbackRemove(string key)
    {
        var path = FallbackPath(key);
        if (File.Exists(path)) File.Delete(path);
    }
}
