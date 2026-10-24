namespace DisctalkClient.UI.Markdown;

/// <summary>Resolves the ids inside Discord markdown (mentions, channels, roles, timestamps) to display text.</summary>
public interface IMarkdownContext
{
    /// <summary>Display name for a user id, or null when unknown.</summary>
    string? UserName(ulong userId);

    /// <summary>Name and CSS color (may be null) of a role, or null when unknown.</summary>
    (string Name, string? Color)? Role(ulong roleId);

    string? ChannelName(ulong channelId);

    /// <summary>The bot's own user id, so mentions of it can be highlighted.</summary>
    ulong? SelfUserId { get; }

    string FormatTimestamp(long unixSeconds, char style);

    string FormatTimestampTooltip(long unixSeconds);
}

/// <summary>A context that knows nothing; ids render as placeholders. Useful for previews and tests.</summary>
public sealed class EmptyMarkdownContext : IMarkdownContext
{
    public static EmptyMarkdownContext Instance { get; } = new();

    public string? UserName(ulong userId) => null;
    public (string Name, string? Color)? Role(ulong roleId) => null;
    public string? ChannelName(ulong channelId) => null;
    public ulong? SelfUserId => null;
    public string FormatTimestamp(long unixSeconds, char style) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToString("u", System.Globalization.CultureInfo.InvariantCulture);
    public string FormatTimestampTooltip(long unixSeconds) => FormatTimestamp(unixSeconds, 'F');
}

public sealed record MarkdownOptions
{
    /// <summary>Headings, lists and block quotes. Off for places Discord renders inline-only (embed titles, field names).</summary>
    public bool Blocks { get; init; } = true;

    /// <summary>Masked links [text](url). Discord allows these in embeds and (since 2023) in messages.</summary>
    public bool MaskedLinks { get; init; } = true;

    /// <summary>Messages made only of emoji (up to 30) render large.</summary>
    public bool AllowJumboEmoji { get; init; } = true;

    public static MarkdownOptions Message { get; } = new();
    public static MarkdownOptions InlineOnly { get; } = new() { Blocks = false, AllowJumboEmoji = false };
}
