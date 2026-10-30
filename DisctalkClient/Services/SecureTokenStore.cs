using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Services;

/// <summary>
/// Tokens live in platform secure storage: Android Keystore-backed EncryptedSharedPreferences, Windows
/// DataProtection. One entry per account kind (your user token, the bot token). Entered once, never logged, and never
/// leaves the device except to authenticate with Discord.
/// </summary>
public sealed partial class SecureTokenStore : ITokenStore
{
    /// <summary>The bot key predates user mode; keeping it means an existing catbot token still works.</summary>
    static string Key(AccountKind kind) => kind == AccountKind.Bot ? "disctalkclient.bot-token" : "disctalkclient.user-token";

    public async ValueTask<string?> GetTokenAsync(AccountKind kind)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key(kind));
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return FallbackGet(kind);
        }
        catch (Exception)
        {
            // Android can fail to decrypt after a backup restore or key rotation. Forget it; the user re-enters it.
            SecureStorage.Default.Remove(Key(kind));
            return null;
        }
    }

    public async ValueTask SaveTokenAsync(AccountKind kind, string token)
    {
        try
        {
            await SecureStorage.Default.SetAsync(Key(kind), token);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            FallbackSetCore(kind, token);
        }
    }

    public ValueTask ClearTokenAsync(AccountKind kind)
    {
        try { SecureStorage.Default.Remove(Key(kind)); }
        catch (Exception ex) when (IsUnavailable(ex)) { }
        FallbackClearCore(kind);
        return ValueTask.CompletedTask;
    }

    static bool IsUnavailable(Exception ex) => ex is NotSupportedException or NotImplementedException or PlatformNotSupportedException;

    // Platform-specific fallbacks (Platforms/Windows); no-ops elsewhere.
    partial void FallbackGetCore(AccountKind kind, ref string? token);
    partial void FallbackSetCore(AccountKind kind, string token);
    partial void FallbackClearCore(AccountKind kind);

    string? FallbackGet(AccountKind kind)
    {
        string? token = null;
        FallbackGetCore(kind, ref token);
        return token;
    }
}
