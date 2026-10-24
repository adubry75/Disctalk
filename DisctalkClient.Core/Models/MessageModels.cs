namespace DisctalkClient.Core.Models;

public sealed record AuthorInfo
{
    public required ulong Id { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public required string AvatarUrl { get; init; }
    public bool IsBot { get; init; }
    /// <summary>Webhook messages carry a per-message name/avatar and no guild member.</summary>
    public bool IsWebhook { get; init; }

    public string DisplayName => GlobalName ?? Username;
}

public sealed record EmojiRef(ulong? Id, string Name, bool Animated = false)
{
    public bool IsCustom => Id is not null;

    /// <summary>Identity for matching reactions: custom emoji by id, unicode by its text.</summary>
    public string Key => Id?.ToString() ?? Name;

    public string? ImageUrl(int size = 48) => Id is ulong id
        ? $"https://cdn.discordapp.com/emojis/{id}.{(Animated ? "gif" : "webp")}?size={size}"
        : null;

    public bool Matches(EmojiRef other) => Key == other.Key;
}

public sealed record ReactionInfo(EmojiRef Emoji, int Count, bool Me);

public sealed record AttachmentInfo
{
    public required ulong Id { get; init; }
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public string? ProxyUrl { get; init; }
    public string? ContentType { get; init; }
    public long Size { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Description { get; init; }

    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif"];
    static readonly string[] VideoExtensions = [".mp4", ".webm", ".mov"];

    public bool IsImage => ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
        || ImageExtensions.Any(e => FileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    public bool IsVideo => ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
        || VideoExtensions.Any(e => FileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    public bool IsSpoiler => FileName.StartsWith("SPOILER_", StringComparison.Ordinal);
}

public sealed record EmbedFieldInfo(string Name, string Value, bool Inline);

public sealed record EmbedInfo
{
    public string? Type { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public int? Color { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorUrl { get; init; }
    public string? AuthorIconUrl { get; init; }
    public string? FooterText { get; init; }
    public string? FooterIconUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? VideoUrl { get; init; }
    public string? ProviderName { get; init; }
    public IReadOnlyList<EmbedFieldInfo> Fields { get; init; } = [];

    /// <summary>Link previews for a bare image/gif URL show just the media, like Discord does.</summary>
    public bool IsMediaOnly => Type is "image" or "gifv" && Title is null && Description is null;
}

public sealed record StickerInfo(ulong Id, string Name, string ImageUrl);

public enum ChatMessageKind { Default, Reply, System }

public sealed record ChatMessage
{
    public required ulong Id { get; init; }
    public required ulong ChannelId { get; init; }
    public ulong? GuildId { get; init; }
    public required AuthorInfo Author { get; init; }
    public string Content { get; init; } = "";
    public DateTimeOffset Timestamp => Snowflakes.ToTimestamp(Id);
    public DateTimeOffset? EditedAt { get; init; }
    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = [];
    public IReadOnlyList<EmbedInfo> Embeds { get; init; } = [];
    public IReadOnlyList<ReactionInfo> Reactions { get; init; } = [];
    public IReadOnlyList<StickerInfo> Stickers { get; init; } = [];
    public ulong? ReplyToId { get; init; }
    /// <summary>Snapshot of the replied-to message, when Discord included it (null if deleted).</summary>
    public ChatMessage? ReplyTo { get; init; }
    public IReadOnlyList<ulong> MentionedUserIds { get; init; } = [];
    public IReadOnlyList<ulong> MentionedRoleIds { get; init; } = [];
    public bool MentionsEveryone { get; init; }
    public ulong? WebhookId { get; init; }
    public ChatMessageKind Kind { get; init; } = ChatMessageKind.Default;
    /// <summary>Human-readable text for system messages (joins, pins, boosts...).</summary>
    public string? SystemText { get; init; }

    public bool IsEdited => EditedAt is not null;
}
