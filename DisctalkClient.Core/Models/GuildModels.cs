namespace DisctalkClient.Core.Models;

public sealed record GuildInfo(ulong Id, string Name, string? IconUrl);

public enum ChannelKind { Text, Announcement, Category, Voice, Forum, Other }

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

    /// <summary>Channels the client shows a message pane for.</summary>
    public bool IsMessageable => Kind is ChannelKind.Text or ChannelKind.Announcement;
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
