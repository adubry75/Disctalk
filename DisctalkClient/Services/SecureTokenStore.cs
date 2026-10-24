using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Services;

/// <summary>
/// The bot token lives in platform secure storage: Android Keystore-backed EncryptedSharedPreferences, Windows
/// DataProtection. It is entered once, never logged, and never leaves the device except to authenticate with Discord.
/// </summary>
public sealed partial class SecureTokenStore : ITokenStore
{
    const string Key = "disctalkclient.bot-token";

    public async ValueTask<string?> GetTokenAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return FallbackGet();
        }
        catch (Exception)
        {
            // Android can fail to decrypt after a backup restore or key rotation. Forget it; the user re-enters it.
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public async ValueTask SaveTokenAsync(string token)
    {
        try
        {
            await SecureStorage.Default.SetAsync(Key, token);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            FallbackSet(token);
        }
    }

    public ValueTask ClearTokenAsync()
    {
        try { SecureStorage.Default.Remove(Key); }
        catch (Exception ex) when (IsUnavailable(ex)) { }
        FallbackClear();
        return ValueTask.CompletedTask;
    }

    static bool IsUnavailable(Exception ex) => ex is NotSupportedException or NotImplementedException or PlatformNotSupportedException;

    // Platform-specific fallbacks (Platforms/Windows); no-ops elsewhere.
    partial void FallbackGetCore(ref string? token);
    partial void FallbackSetCore(string token);
    partial void FallbackClearCore();

    string? FallbackGet()
    {
        string? token = null;
        FallbackGetCore(ref token);
        return token;
    }

    void FallbackSet(string token) => FallbackSetCore(token);
    void FallbackClear() => FallbackClearCore();
}
