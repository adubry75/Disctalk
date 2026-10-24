using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Abstractions;

public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting, Failed }

/// <summary>
/// Library-neutral gateway events. The NetCord adapter translates into these; tests and the demo host raise them directly.
/// </summary>
public abstract record GatewayEvent;

public sealed record ConnectionStateChangedEvent(ConnectionState State, string? Detail = null) : GatewayEvent;
public sealed record ReadyEvent(AuthorInfo CurrentUser) : GatewayEvent;
public sealed record GuildAvailableEvent(GuildSnapshot Snapshot) : GatewayEvent;
public sealed record GuildUpdatedEvent(GuildInfo Guild) : GatewayEvent;
public sealed record GuildRemovedEvent(ulong GuildId) : GatewayEvent;
public sealed record ChannelUpsertEvent(ChannelInfo Channel) : GatewayEvent;
public sealed record ChannelDeleteEvent(ulong GuildId, ulong ChannelId) : GatewayEvent;
public sealed record RoleUpsertEvent(RoleInfo Role) : GatewayEvent;
public sealed record RoleDeleteEvent(ulong GuildId, ulong RoleId) : GatewayEvent;
public sealed record MembersUpsertEvent(ulong GuildId, IReadOnlyList<MemberInfo> Members) : GatewayEvent;
public sealed record MemberRemoveEvent(ulong GuildId, ulong UserId) : GatewayEvent;
public sealed record EmojisUpdateEvent(ulong GuildId, IReadOnlyList<GuildEmojiInfo> Emojis) : GatewayEvent;
public sealed record MessageCreateEvent(ChatMessage Message) : GatewayEvent;
public sealed record MessageUpdateEvent(ChatMessage Message) : GatewayEvent;
public sealed record MessageDeleteEvent(ulong ChannelId, IReadOnlyList<ulong> MessageIds) : GatewayEvent;
public sealed record ReactionAddEvent(ulong ChannelId, ulong MessageId, ulong UserId, EmojiRef Emoji) : GatewayEvent;
public sealed record ReactionRemoveEvent(ulong ChannelId, ulong MessageId, ulong UserId, EmojiRef Emoji) : GatewayEvent;
/// <summary>All reactions (Emoji null) or every reaction of one emoji were removed.</summary>
public sealed record ReactionClearEvent(ulong ChannelId, ulong MessageId, EmojiRef? Emoji) : GatewayEvent;
public sealed record TypingStartEvent(ulong ChannelId, ulong UserId, MemberInfo? Member, DateTimeOffset Timestamp) : GatewayEvent;
