namespace DisctalkClient.Core.Models;

public sealed record GuildInfo(ulong Id, string Name, string? IconUrl)
{
    /// <summary>DMs and group DMs are grouped under a pseudo guild with this id (no real guild has id 0).</summary>
    public const ulong DirectMessagesId = 0;

    public static GuildInfo DirectMessages { get; } = new(DirectMessagesId, "Direct Messages", null);

    public bool IsDirectMessages => Id == DirectMessagesId;
}

public enum ChannelKind { Text, Announcement, Category, Voice, Forum, Other, DirectMessage, GroupDirectMessage, Thread }

public sealed record ChannelInfo
{
    public required ulong Id { get; init; }
    public required ulong GuildId { get; init; }
    public required string Name { get; init; }
    public ChannelKind Kind { get; init; } = ChannelKind.Text;
    public ulong? ParentId { get; init; }
    public int Position { get; init; }
    public string? Topic { get; init; }
    public ulong? LastMessageId { get; init; }
    /// <summary>DMs: the other person's avatar (or the group icon).</summary>
    public string? IconUrl { get; init; }
    /// <summary>DMs: the other people in the conversation (not you).</summary>
    public IReadOnlyList<ulong> RecipientIds { get; init; } = [];

    /// <summary>Channels the client shows a message pane for.</summary>
    public bool IsMessageable => Kind is ChannelKind.Text or ChannelKind.Announcement or ChannelKind.DirectMessage
        or ChannelKind.GroupDirectMessage or ChannelKind.Thread;

    public bool IsPrivate => Kind is ChannelKind.DirectMessage or ChannelKind.GroupDirectMessage;
}

public sealed record RoleInfo
{
    public required ulong Id { get; init; }
    public required ulong GuildId { get; init; }
    public required string Name { get; init; }
    /// <summary>RGB, or null for "no color".</summary>
    public int? Color { get; init; }
    public int Position { get; init; }
    public bool Hoist { get; init; }

    public string? CssColor => Color is int c and not 0 ? $"#{c:x6}" : null;
}

public sealed record MemberInfo
{
    public required ulong UserId { get; init; }
    public required ulong GuildId { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
    public string? Nickname { get; init; }
    public required string AvatarUrl { get; init; }
    public bool IsBot { get; init; }
    public IReadOnlyList<ulong> RoleIds { get; init; } = [];

    public string DisplayName => Nickname ?? GlobalName ?? Username;
}

public sealed record GuildEmojiInfo(ulong Id, ulong GuildId, string Name, bool Animated)
{
    public EmojiRef ToRef() => new(Id, Name, Animated);
}

/// <summary>Everything the gateway hands over when a guild becomes available.</summary>
public sealed record GuildSnapshot(
    GuildInfo Guild,
    IReadOnlyList<ChannelInfo> Channels,
    IReadOnlyList<RoleInfo> Roles,
    IReadOnlyList<MemberInfo> Members,
    IReadOnlyList<GuildEmojiInfo> Emojis);
