using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Services;

/// <summary>Non-secret settings (theme, timestamp format, read state) in MAUI Preferences.</summary>
public sealed class PreferencesSettingsStore : ISettingsStore
{
    public ValueTask<string?> GetAsync(string key) => ValueTask.FromResult(Preferences.Default.Get<string?>(key, null));

    public ValueTask SetAsync(string key, string value)
    {
        Preferences.Default.Set(key, value);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key)
    {
        Preferences.Default.Remove(key);
        return ValueTask.CompletedTask;
    }
}
