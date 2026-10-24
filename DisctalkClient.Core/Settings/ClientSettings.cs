using System.Text.Json.Serialization;

namespace DisctalkClient.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode { Dark, Midnight, Light }

/// <summary>Accent colors, named after cats.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatAccent>))]
public enum CatAccent { Ginger, Siamese, Calico, Tabby, Tuxedo }

[JsonConverter(typeof(JsonStringEnumConverter<SendMode>))]
public enum SendMode
{
    /// <summary>Post as the bot user.</summary>
    Bot,
    /// <summary>Post through a channel webhook with the configured display name and avatar.</summary>
    Webhook,
}

public sealed record ClientSettings
{
    /// <summary>First customization: timestamps include seconds.</summary>
    public const string DefaultTimestampFormat = "h:mm:ss tt";

    public string TimestampFormat { get; init; } = DefaultTimestampFormat;
    public bool CompactMode { get; init; }
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;
    public CatAccent Accent { get; init; } = CatAccent.Ginger;

    public SendMode SendMode { get; init; } = SendMode.Bot;
    /// <summary>Username shown on webhook messages. Empty = the webhook's own name.</summary>
    public string WebhookDisplayName { get; init; } = "";
    /// <summary>Avatar image URL for webhook messages. Empty = the webhook's own avatar.</summary>
    public string WebhookAvatarUrl { get; init; } = "";

    public bool PingOnReply { get; init; } = true;
    public bool AllowEveryoneMentions { get; init; }
    public bool SendTypingIndicator { get; init; } = true;

    public ulong? LastGuildId { get; init; }
    public ulong? LastChannelId { get; init; }

    public static string AccentHex(CatAccent accent) => accent switch
    {
        CatAccent.Ginger => "#f2a65a",
        CatAccent.Siamese => "#6cb4ee",
        CatAccent.Calico => "#e07a5f",
        CatAccent.Tabby => "#c9a227",
        CatAccent.Tuxedo => "#b8c0cc",
        _ => "#f2a65a",
    };
}
