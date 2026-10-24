using System.Text.Json;
using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Core.Settings;

public sealed record SettingsUpdateResult(bool Success, IReadOnlyList<string> Errors)
{
    public static SettingsUpdateResult Ok { get; } = new(true, []);
}

/// <summary>Loads, validates, persists and broadcasts <see cref="ClientSettings"/>.</summary>
public sealed class SettingsService(ISettingsStore store)
{
    public const string StorageKey = "disctalkclient.settings";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly SemaphoreSlim _gate = new(1, 1);

    public ClientSettings Current { get; private set; } = new();

    public event Action<ClientSettings>? Changed;

    public async Task LoadAsync()
    {
        var raw = await store.GetAsync(StorageKey);
        ClientSettings loaded;
        try
        {
            loaded = string.IsNullOrWhiteSpace(raw) ? new() : JsonSerializer.Deserialize<ClientSettings>(raw, Json) ?? new();
        }
        catch (JsonException)
        {
            loaded = new();
        }
        Current = Sanitize(loaded);
        Changed?.Invoke(Current);
    }

    /// <summary>Applies <paramref name="change"/> if the result validates; otherwise keeps the current settings.</summary>
    public async Task<SettingsUpdateResult> UpdateAsync(Func<ClientSettings, ClientSettings> change)
    {
        await _gate.WaitAsync();
        ClientSettings updated;
        try
        {
            updated = change(Current);
            var errors = SettingsValidator.Validate(updated);
            if (errors.Count > 0) return new(false, errors);

            updated = updated with
            {
                WebhookDisplayName = updated.WebhookDisplayName.Trim(),
                WebhookAvatarUrl = updated.WebhookAvatarUrl.Trim(),
            };
            if (updated == Current) return SettingsUpdateResult.Ok;

            await store.SetAsync(StorageKey, JsonSerializer.Serialize(updated, Json));
            Current = updated;
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke(updated);
        return SettingsUpdateResult.Ok;
    }

    public Task<SettingsUpdateResult> ResetAsync() => UpdateAsync(current => new ClientSettings
    {
        LastGuildId = current.LastGuildId,
        LastChannelId = current.LastChannelId,
    });

    /// <summary>A hand-edited or older settings blob must not brick the UI: invalid fields fall back to defaults.</summary>
    static ClientSettings Sanitize(ClientSettings s)
    {
        var defaults = new ClientSettings();
        return s with
        {
            TimestampFormat = SettingsValidator.ValidateTimestampFormat(s.TimestampFormat) is null ? s.TimestampFormat : defaults.TimestampFormat,
            WebhookDisplayName = SettingsValidator.ValidateWebhookName(s.WebhookDisplayName) is null ? s.WebhookDisplayName ?? "" : "",
            WebhookAvatarUrl = SettingsValidator.ValidateAvatarUrl(s.WebhookAvatarUrl) is null ? s.WebhookAvatarUrl ?? "" : "",
            Theme = Enum.IsDefined(s.Theme) ? s.Theme : defaults.Theme,
            Accent = Enum.IsDefined(s.Accent) ? s.Accent : defaults.Accent,
            SendMode = Enum.IsDefined(s.SendMode) ? s.SendMode : defaults.SendMode,
        };
    }
}
