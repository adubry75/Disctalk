namespace DisctalkClient.Core.Abstractions;

/// <summary>Plain (non-secret) key/value persistence. MAUI: Preferences. Dev host: a JSON file. Tests: a dictionary.</summary>
public interface ISettingsStore
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
    ValueTask RemoveAsync(string key);
}

/// <summary>Where the bot token lives. MAUI: SecureStorage. Implementations must never log the value.</summary>
public interface ITokenStore
{
    ValueTask<string?> GetTokenAsync();
    ValueTask SaveTokenAsync(string token);
    ValueTask ClearTokenAsync();
}

public sealed class InMemorySettingsStore : ISettingsStore
{
    readonly Dictionary<string, string> _values = new();
    readonly Lock _lock = new();

    public ValueTask<string?> GetAsync(string key)
    {
        lock (_lock) return ValueTask.FromResult(_values.GetValueOrDefault(key));
    }

    public ValueTask SetAsync(string key, string value)
    {
        lock (_lock) _values[key] = value;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key)
    {
        lock (_lock) _values.Remove(key);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Keeps the token for the life of the process only.</summary>
public sealed class InMemoryTokenStore(string? token = null) : ITokenStore
{
    string? _token = token;

    public ValueTask<string?> GetTokenAsync() => ValueTask.FromResult(_token);
    public ValueTask SaveTokenAsync(string token) { _token = token; return ValueTask.CompletedTask; }
    public ValueTask ClearTokenAsync() { _token = null; return ValueTask.CompletedTask; }
}
