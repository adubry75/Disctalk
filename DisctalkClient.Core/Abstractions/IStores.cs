namespace DisctalkClient.Core.Abstractions;

/// <summary>Plain (non-secret) key/value persistence. MAUI: Preferences. Dev host: a JSON file. Tests: a dictionary.</summary>
public interface ISettingsStore
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
    ValueTask RemoveAsync(string key);
}

/// <summary>
/// Where the tokens live, one per <see cref="AccountKind"/>. MAUI: SecureStorage. Implementations must never log the value.
/// </summary>
public interface ITokenStore
{
    ValueTask<string?> GetTokenAsync(AccountKind kind);
    ValueTask SaveTokenAsync(AccountKind kind, string token);
    ValueTask ClearTokenAsync(AccountKind kind);
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

/// <summary>Other secrets (the archive database connection, which includes a password). MAUI: SecureStorage.</summary>
public interface ISecretStore
{
    ValueTask<string?> GetAsync(string key);
    ValueTask SetAsync(string key, string value);
    ValueTask RemoveAsync(string key);
}

/// <summary>Keeps secrets for the life of the process only.</summary>
public sealed class InMemorySecretStore : ISecretStore
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

/// <summary>Keeps the tokens for the life of the process only.</summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    readonly Dictionary<AccountKind, string> _tokens = new();
    readonly Lock _lock = new();

    public InMemoryTokenStore() { }

    public InMemoryTokenStore(AccountKind kind, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token)) _tokens[kind] = token;
    }

    public ValueTask<string?> GetTokenAsync(AccountKind kind)
    {
        lock (_lock) return ValueTask.FromResult(_tokens.GetValueOrDefault(kind));
    }

    public ValueTask SaveTokenAsync(AccountKind kind, string token)
    {
        lock (_lock) _tokens[kind] = token;
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearTokenAsync(AccountKind kind)
    {
        lock (_lock) _tokens.Remove(kind);
        return ValueTask.CompletedTask;
    }
}
