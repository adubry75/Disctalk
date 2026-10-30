using System.Text.Json;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.UserAccount;

/// <summary>
/// Gateway DISPATCH payloads (as a user account receives them) → library-neutral <see cref="GatewayEvent"/>s.
/// Keeps the little state a user session needs to interpret later events: who we are and the deduplicated users
/// from READY (DM recipients are referenced by id).
/// </summary>
public sealed class UserEventTranslator
{
    readonly Dictionary<ulong, AuthorInfo> _users = new();

    public AuthorInfo? Self { get; private set; }
    public string? SessionId { get; private set; }
    public string? ResumeGatewayUrl { get; private set; }

    public IReadOnlyList<GatewayEvent> Translate(string type, JsonElement d) => type switch
    {
        "READY" => Ready(d),
        "GUILD_CREATE" => d.Bool("unavailable") ? [] : [new GuildAvailableEvent(DiscordJson.ToSnapshot(d, self: Self))],
        "GUILD_UPDATE" => [new GuildUpdatedEvent(DiscordJson.ToGuild(d))],
        // unavailable = outage; the guild comes back with GUILD_CREATE. Otherwise we left or were removed.
        "GUILD_DELETE" => d.Bool("unavailable") || d.Id("id") is not ulong gone ? [] : [new GuildRemovedEvent(gone)],

        "CHANNEL_CREATE" or "CHANNEL_UPDATE" or "THREAD_CREATE" or "THREAD_UPDATE" => ChannelUpsert(d),
        "CHANNEL_DELETE" or "THREAD_DELETE" => d.Id("id") is ulong cid ? [new ChannelDeleteEvent(d.Id("guild_id") ?? GuildInfo.DirectMessagesId, cid)] : [],
        "THREAD_LIST_SYNC" => d.Arr("threads").SelectMany(ChannelUpsert).ToList(),

        "GUILD_ROLE_CREATE" or "GUILD_ROLE_UPDATE" when d.Id("guild_id") is ulong g && d.Obj("role") is { } role
            => [new RoleUpsertEvent(DiscordJson.ToRole(g, role))],
        "GUILD_ROLE_DELETE" when d.Id("guild_id") is ulong g && d.Id("role_id") is ulong r => [new RoleDeleteEvent(g, r)],
        "GUILD_EMOJIS_UPDATE" when d.Id("guild_id") is ulong g
            => [new EmojisUpdateEvent(g, d.Arr("emojis").Select(e => DiscordJson.ToEmoji(g, e)).OfType<GuildEmojiInfo>().ToList())],

        "GUILD_MEMBER_ADD" or "GUILD_MEMBER_UPDATE" when d.Id("guild_id") is ulong g && DiscordJson.ToMember(g, d) is { } member
            => [new MembersUpsertEvent(g, [member])],
        "GUILD_MEMBER_REMOVE" when d.Id("guild_id") is ulong g && d.Obj("user")?.Id("id") is ulong u => [new MemberRemoveEvent(g, u)],
        "GUILD_MEMBERS_CHUNK" when d.Id("guild_id") is ulong g => Members(g, d.Arr("members")),
        "GUILD_MEMBER_LIST_UPDATE" when d.Id("guild_id") is ulong g => Members(g, MemberListItems(d)),

        "MESSAGE_CREATE" => MessageCreate(d),
        "MESSAGE_UPDATE" => MessageUpdate(d),
        "MESSAGE_DELETE" when d.Id("channel_id") is ulong c && d.Id("id") is ulong m => [new MessageDeleteEvent(c, [m])],
        "MESSAGE_DELETE_BULK" when d.Id("channel_id") is ulong c => [new MessageDeleteEvent(c, d.Ids("ids"))],

        "MESSAGE_REACTION_ADD" when Reaction(d) is (var c, var m, var u, var e) => [new ReactionAddEvent(c, m, u, e)],
        "MESSAGE_REACTION_REMOVE" when Reaction(d) is (var c, var m, var u, var e) => [new ReactionRemoveEvent(c, m, u, e)],
        "MESSAGE_REACTION_REMOVE_ALL" when d.Id("channel_id") is ulong c && d.Id("message_id") is ulong m => [new ReactionClearEvent(c, m, null)],
        "MESSAGE_REACTION_REMOVE_EMOJI" when d.Id("channel_id") is ulong c && d.Id("message_id") is ulong m && d.Obj("emoji") is { } e
            => [new ReactionClearEvent(c, m, DiscordJson.ToEmojiRef(e))],

        "TYPING_START" when d.Id("channel_id") is ulong c && d.Id("user_id") is ulong u => [new TypingStartEvent(c, u,
            d.Id("guild_id") is ulong g && d.Obj("member") is { } mem ? DiscordJson.ToMember(g, mem) : null,
            d.Long("timestamp") is long ts ? DateTimeOffset.FromUnixTimeSeconds(ts) : DateTimeOffset.UtcNow)],

        _ => [],
    };

    IReadOnlyList<GatewayEvent> Ready(JsonElement d)
    {
        SessionId = d.Str("session_id");
        ResumeGatewayUrl = d.Str("resume_gateway_url");
        _users.Clear();
        foreach (var u in d.Arr("users")) if (u.Id("id") is ulong id) _users[id] = DiscordJson.ToAuthor(u);

        var events = new List<GatewayEvent>();
        if (d.Obj("user") is { } me)
        {
            Self = DiscordJson.ToAuthor(me);
            events.Add(new ReadyEvent(Self));
        }

        // Read state goes first so unread seeding sees it before channels are tracked.
        // It's an array, or { entries: [...] } with the VERSIONED_READ_STATES capability.
        var readState = d.Obj("read_state") is { } rs ? rs.Arr("entries") : d.Arr("read_state");
        var lastRead = new Dictionary<ulong, ulong>();
        foreach (var entry in readState)
            if (entry.Id("id") is ulong channel && entry.Id("last_message_id") is ulong last) lastRead[channel] = last;
        if (lastRead.Count > 0) events.Add(new ReadStateEvent(lastRead));

        var merged = d.Arr("merged_members").ToList();
        int i = 0;
        foreach (var guild in d.Arr("guilds"))
        {
            var members = i < merged.Count && merged[i].ValueKind == JsonValueKind.Array ? merged[i].EnumerateArray().ToList() : null;
            i++;
            if (guild.Bool("unavailable")) continue; // arrives later as GUILD_CREATE
            events.Add(new GuildAvailableEvent(DiscordJson.ToSnapshot(guild, members, Self)));
        }

        var dms = d.Arr("private_channels").Select(c => DiscordJson.ToChannel(c, users: _users, selfId: Self?.Id)).OfType<ChannelInfo>().ToList();
        var recipients = d.Arr("private_channels").SelectMany(c => DiscordJson.Recipients(c, _users))
            .Where(u => u.Id != Self?.Id).DistinctBy(u => u.Id).Select(DiscordJson.ToDirectMessageMember).ToList();
        events.Add(new GuildAvailableEvent(new GuildSnapshot(GuildInfo.DirectMessages, dms, [], recipients, [])));
        return events;
    }

    IReadOnlyList<GatewayEvent> ChannelUpsert(JsonElement d)
    {
        var channel = DiscordJson.ToChannel(d, users: _users, selfId: Self?.Id);
        if (channel is null)
            // An archived thread: drop it from the list like Discord does.
            return d.Id("id") is ulong id ? [new ChannelDeleteEvent(d.Id("guild_id") ?? 0, id)] : [];
        if (!channel.IsPrivate) return [new ChannelUpsertEvent(channel)];
        var recipients = DiscordJson.Recipients(d, _users).Where(u => u.Id != Self?.Id).Select(DiscordJson.ToDirectMessageMember).ToList();
        return [new MembersUpsertEvent(GuildInfo.DirectMessagesId, recipients), new ChannelUpsertEvent(channel)];
    }

    static IReadOnlyList<GatewayEvent> Members(ulong guildId, IEnumerable<JsonElement> members)
    {
        var list = members.Select(m => DiscordJson.ToMember(guildId, m)).OfType<MemberInfo>().ToList();
        return list.Count == 0 ? [] : [new MembersUpsertEvent(guildId, list)];
    }

    /// <summary>Member entries from a member-list update's SYNC/INSERT/UPDATE ops (group headers are skipped).</summary>
    static IEnumerable<JsonElement> MemberListItems(JsonElement d)
    {
        foreach (var op in d.Arr("ops"))
        {
            var items = op.Arr("items").ToList();
            if (op.Obj("item") is { } single) items.Add(single);
            foreach (var item in items)
                if (item.Obj("member") is { } member) yield return member;
        }
    }

    IReadOnlyList<GatewayEvent> MessageCreate(JsonElement d)
    {
        var message = DiscordJson.ToMessage(d);
        var events = new List<GatewayEvent>(2);
        // Guild messages carry the author's member (nick, roles) without its user; pair them so names and colors resolve.
        if (message.GuildId is ulong g && d.Obj("member") is { } member && d.Obj("author") is { } author
            && DiscordJson.ToMember(g, member, author) is { } info)
            events.Add(new MembersUpsertEvent(g, [info]));
        events.Add(new MessageCreateEvent(message));
        return events;
    }

    static IReadOnlyList<GatewayEvent> MessageUpdate(JsonElement d)
    {
        if (d.Obj("author") is not null) return [new MessageUpdateEvent(DiscordJson.ToMessage(d))];
        if (d.Id("channel_id") is not ulong c || d.Id("id") is not ulong m) return [];
        var embeds = d.TryGetProperty("embeds", out _) ? d.Arr("embeds").Select(DiscordJson.ToEmbed).ToList() : null;
        return [new MessagePatchEvent(c, m, embeds, d.Str("content"), d.Time("edited_timestamp"))];
    }

    static (ulong, ulong, ulong, EmojiRef)? Reaction(JsonElement d) =>
        d.Id("channel_id") is ulong c && d.Id("message_id") is ulong m && d.Id("user_id") is ulong u && d.Obj("emoji") is { } e
            ? (c, m, u, DiscordJson.ToEmojiRef(e))
            : null;
}
