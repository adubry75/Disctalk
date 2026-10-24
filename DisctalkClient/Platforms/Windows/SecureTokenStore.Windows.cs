using System.Security.Cryptography;
using System.Text;

namespace DisctalkClient.Services;

// Unpackaged Windows apps may not get SecureStorage; fall back to a DPAPI (current user) encrypted file.
public sealed partial class SecureTokenStore
{
    static string FallbackPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisctalkClient", "token.bin");

    partial void FallbackGetCore(ref string? token)
    {
        if (!File.Exists(FallbackPath)) return;
        try
        {
            token = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FallbackPath), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            File.Delete(FallbackPath);
        }
    }

    partial void FallbackSetCore(string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
        File.WriteAllBytes(FallbackPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    partial void FallbackClearCore()
    {
        if (File.Exists(FallbackPath)) File.Delete(FallbackPath);
    }
}
