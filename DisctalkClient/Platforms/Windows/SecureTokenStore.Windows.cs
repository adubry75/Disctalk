using System.Security.Cryptography;
using System.Text;
using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Services;

// Unpackaged Windows apps may not get SecureStorage; fall back to a DPAPI (current user) encrypted file per account kind.
public sealed partial class SecureTokenStore
{
    static string FallbackPath(AccountKind kind) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisctalkClient", kind == AccountKind.Bot ? "token.bin" : "user-token.bin");

    partial void FallbackGetCore(AccountKind kind, ref string? token)
    {
        var path = FallbackPath(kind);
        if (!File.Exists(path)) return;
        try
        {
            token = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            File.Delete(path);
        }
    }

    partial void FallbackSetCore(AccountKind kind, string token)
    {
        var path = FallbackPath(kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    partial void FallbackClearCore(AccountKind kind)
    {
        var path = FallbackPath(kind);
        if (File.Exists(path)) File.Delete(path);
    }
}
