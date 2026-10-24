using System.Text.Json;
using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.DevHost;

/// <summary>Non-secret settings in a JSON file under the user's local app data (never the repo).</summary>
public sealed class JsonFileSettingsStore(string path) : ISettingsStore
{
    readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<string?> GetAsync(string key) => (await LoadAsync()).GetValueOrDefault(key);

    public async ValueTask SetAsync(string key, string value) => await MutateAsync(d => d[key] = value);

    public async ValueTask RemoveAsync(string key) => await MutateAsync(d => d.Remove(key));

    async Task<Dictionary<string, string>> LoadAsync()
    {
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path)) ?? new(); }
        catch (JsonException) { return new(); }
    }

    async Task MutateAsync(Action<Dictionary<string, string>> change)
    {
        await _gate.WaitAsync();
        try
        {
            var data = await LoadAsync();
            change(data);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));
        }
        finally
        {
            _gate.Release();
        }
    }
}
