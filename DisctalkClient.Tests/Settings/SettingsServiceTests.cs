using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Settings;

namespace DisctalkClient.Tests.Settings;

public class SettingsServiceTests
{
    readonly InMemorySettingsStore _store = new();

    [Fact]
    public void Defaults_show_seconds_dark_theme_ginger_cat_bot_mode()
    {
        var s = new ClientSettings();
        Assert.Equal("h:mm:ss tt", s.TimestampFormat);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.Equal(CatAccent.Ginger, s.Accent);
        Assert.Equal(SendMode.Bot, s.SendMode);
        Assert.False(s.CompactMode);
        Assert.True(s.PingOnReply);
        Assert.False(s.AllowEveryoneMentions);
    }

    [Fact]
    public async Task Load_with_nothing_stored_gives_defaults()
    {
        var service = new SettingsService(_store);
        await service.LoadAsync();
        Assert.Equal(new ClientSettings(), service.Current);
    }

    [Fact]
    public async Task Updates_persist_and_reload()
    {
        var service = new SettingsService(_store);
        var result = await service.UpdateAsync(s => s with
        {
            TimestampFormat = "HH:mm:ss",
            CompactMode = true,
            Theme = ThemeMode.Midnight,
            Accent = CatAccent.Tuxedo,
            SendMode = SendMode.Webhook,
            WebhookDisplayName = "  Andre ",
            WebhookAvatarUrl = " https://example.com/me.png ",
        });
        Assert.True(result.Success);

        var reloaded = new SettingsService(_store);
        await reloaded.LoadAsync();
        Assert.Equal("HH:mm:ss", reloaded.Current.TimestampFormat);
        Assert.True(reloaded.Current.CompactMode);
        Assert.Equal(ThemeMode.Midnight, reloaded.Current.Theme);
        Assert.Equal(CatAccent.Tuxedo, reloaded.Current.Accent);
        Assert.Equal(SendMode.Webhook, reloaded.Current.SendMode);
        Assert.Equal("Andre", reloaded.Current.WebhookDisplayName);
        Assert.Equal("https://example.com/me.png", reloaded.Current.WebhookAvatarUrl);
    }

    [Fact]
    public async Task Enums_are_stored_by_name()
    {
        var service = new SettingsService(_store);
        await service.UpdateAsync(s => s with { Theme = ThemeMode.Light });
        Assert.Contains("\"theme\":\"Light\"", await _store.GetAsync(SettingsService.StorageKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%")]
    [InlineData("h:mm:ss tt hh:mm:ss tt hh:mm:ss tt hh:mm:ss tt hh:mm:ss tt hh:mm:ss tt")]
    public async Task Invalid_timestamp_formats_are_rejected_and_not_saved(string format)
    {
        var service = new SettingsService(_store);
        var result = await service.UpdateAsync(s => s with { TimestampFormat = format });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(ClientSettings.DefaultTimestampFormat, service.Current.TimestampFormat);
        Assert.Null(await _store.GetAsync(SettingsService.StorageKey));
    }

    [Theory]
    [InlineData("Discord Fan")]
    [InlineData("clyde")]
    public async Task Webhook_names_discord_rejects_are_caught_early(string name)
    {
        var service = new SettingsService(_store);
        var result = await service.UpdateAsync(s => s with { WebhookDisplayName = name });
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("webhook names"));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/a.png")]
    [InlineData("javascript:alert(1)")]
    public async Task Avatar_must_be_an_http_url(string url)
    {
        var service = new SettingsService(_store);
        Assert.False((await service.UpdateAsync(s => s with { WebhookAvatarUrl = url })).Success);
    }

    [Fact]
    public async Task Changed_fires_only_on_real_changes()
    {
        var service = new SettingsService(_store);
        var seen = new List<ClientSettings>();
        service.Changed += seen.Add;

        await service.UpdateAsync(s => s with { CompactMode = true });
        await service.UpdateAsync(s => s with { CompactMode = true });
        await service.UpdateAsync(s => s with { TimestampFormat = "" });

        Assert.Single(seen);
        Assert.True(seen[0].CompactMode);
    }

    [Fact]
    public async Task Corrupt_storage_falls_back_to_defaults()
    {
        await _store.SetAsync(SettingsService.StorageKey, "{ not json");
        var service = new SettingsService(_store);
        await service.LoadAsync();
        Assert.Equal(new ClientSettings(), service.Current);
    }

    [Fact]
    public async Task Hand_edited_invalid_fields_are_sanitized_individually()
    {
        await _store.SetAsync(SettingsService.StorageKey,
            """{"timestampFormat":"%","compactMode":true,"theme":"Midnight","webhookDisplayName":"discord","webhookAvatarUrl":"nope"}""");
        var service = new SettingsService(_store);
        await service.LoadAsync();

        Assert.Equal(ClientSettings.DefaultTimestampFormat, service.Current.TimestampFormat);
        Assert.True(service.Current.CompactMode);
        Assert.Equal(ThemeMode.Midnight, service.Current.Theme);
        Assert.Equal("", service.Current.WebhookDisplayName);
        Assert.Equal("", service.Current.WebhookAvatarUrl);
    }

    [Fact]
    public async Task Reset_restores_defaults_but_remembers_last_channel()
    {
        var service = new SettingsService(_store);
        await service.UpdateAsync(s => s with { CompactMode = true, LastChannelId = TestData.GeneralId, LastGuildId = TestData.GuildId });
        await service.ResetAsync();
        Assert.False(service.Current.CompactMode);
        Assert.Equal(TestData.GeneralId, service.Current.LastChannelId);
    }

    [Fact]
    public void Every_accent_has_a_color() =>
        Assert.All(Enum.GetValues<CatAccent>(), a => Assert.Matches("^#[0-9a-f]{6}$", ClientSettings.AccentHex(a)));
}
