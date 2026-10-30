using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Services;

/// <summary>
/// Secrets in platform secure storage: Android Keystore-backed EncryptedSharedPreferences, Windows DataProtection.
/// Values are never logged and never leave the device except to authenticate (Discord, the archive database).
/// </summary>
public sealed partial class SecureSecretStore : ISecretStore
{
    public async ValueTask<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            string? value = null;
            FallbackGet(key, ref value);
            return value;
        }
        catch (Exception)
        {
            // Android can fail to decrypt after a backup restore or key rotation. Forget it; the user re-enters it.
            SecureStorage.Default.Remove(key);
            return null;
        }
    }

    public async ValueTask SetAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            FallbackSet(key, value);
        }
    }

    public ValueTask RemoveAsync(string key)
    {
        try { SecureStorage.Default.Remove(key); }
        catch (Exception ex) when (IsUnavailable(ex)) { }
        FallbackRemove(key);
        return ValueTask.CompletedTask;
    }

    static bool IsUnavailable(Exception ex) => ex is NotSupportedException or NotImplementedException or PlatformNotSupportedException;

    // Platform-specific fallbacks (Platforms/Windows); no-ops elsewhere.
    partial void FallbackGet(string key, ref string? value);
    partial void FallbackSet(string key, string value);
    partial void FallbackRemove(string key);
}

/// <summary>One secure entry per account kind (your user token, the bot token).</summary>
public sealed class SecureTokenStore(SecureSecretStore secrets) : ITokenStore
{
    /// <summary>The bot key predates user mode; keeping it means an existing catbot token still works.</summary>
    public static string Key(AccountKind kind) => kind == AccountKind.Bot ? "disctalkclient.bot-token" : "disctalkclient.user-token";

    public ValueTask<string?> GetTokenAsync(AccountKind kind) => secrets.GetAsync(Key(kind));
    public ValueTask SaveTokenAsync(AccountKind kind, string token) => secrets.SetAsync(Key(kind), token);
    public ValueTask ClearTokenAsync(AccountKind kind) => secrets.RemoveAsync(Key(kind));
}
