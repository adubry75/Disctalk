using System.Globalization;
using System.Text.Json;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.UserAccount;

/// <summary>
/// Raw Discord JSON (REST responses and gateway dispatches, as a user account sees them) → the client's
/// library-neutral models. Tolerant by design: user-account payloads vary with the gateway capabilities, so every
/// field is optional and anything unparseable is skipped rather than thrown.
/// </summary>
public static class DiscordJson
{
    public const string Cdn = "https://cdn.discordapp.com";

    // ---------------------------------------------------------------- primitive readers

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Snowflakes are strings in JSON, but tolerate numbers.</summary>
    public static ulong? Id(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String when ulong.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) => id,
            JsonValueKind.Number when v.TryGetUInt64(out var n) => n,
            _ => null,
        };
    }

    public static int? Int(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    public static long? Long(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i) ? i : null;

    public static bool Bool(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static JsonElement? Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    public static IReadOnlyList<ulong> Ids(this JsonElement e, string name) =>
        e.Arr(name).Select(x => x.ValueKind == JsonValueKind.String && ulong.TryParse(x.GetString(), CultureInfo.InvariantCulture, out var id) ? id : 0UL)
            .Where(id => id != 0).ToList();

    public static DateTimeOffset? Time(this JsonElement e, string name) =>
        e.Str(name) is { } s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    // ---------------------------------------------------------------- CDN

    public static string AvatarUrl(ulong userId, string? hash, string? discriminator = null, int size = 128) => hash is { Length: > 0 }
        ? $"{Cdn}/avatars/{userId}/{hash}.{(hash.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png")}?size={size}"
        : DefaultAvatarUrl(userId, discriminator);

    /// <summary>Pomelo (unique username) accounts: (id &gt;&gt; 22) % 6. Legacy discriminators: discriminator % 5.</summary>
    public static string DefaultAvatarUrl(ulong userId, string? discriminator)
    {
        int index = discriminator is { Length: > 0 } d && d != "0" && int.TryParse(d, CultureInfo.InvariantCulture, out var n)
            ? n % 5
            : (int)((userId >> 22) % 6);
        return $"{Cdn}/embed/avatars/{index}.png";
    }

    public static string? GuildIconUrl(ulong guildId, string? hash) =>
        hash is { Length: > 0 } ? $"{Cdn}/icons/{guildId}/{hash}.{(hash.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png")}?size=128" : null;

    // ---------------------------------------------------------------- users & members

    public static AuthorInfo ToAuthor(JsonElement user, bool isWebhook = false)
    {
        var id = user.Id("id") ?? 0;
        return new AuthorInfo
        {
            Id = id,
            Username = user.Str("username") ?? "Unknown",
            GlobalName = NonEmpty(user.Str("global_name")),
            AvatarUrl = AvatarUrl(id, user.Str("avatar"), user.Str("discriminator")),
            IsBot = user.Bool("bot"),
            IsWebhook = isWebhook,
        };
    }

    /// <param name="member">A guild member object (with or without its own <c>user</c>).</param>
    /// <param name="user">The user object when the member doesn't embed one (MESSAGE_CREATE puts it in <c>author</c>).</param>
    public static MemberInfo? ToMember(ulong guildId, JsonElement member, JsonElement? user = null)
    {
        var u = member.Obj("user") ?? user;
        if (u is not { } userJson || userJson.Id("id") is not ulong id) return null;
        var author = ToAuthor(userJson);
        var guildAvatar = member.Str("avatar");
        return new MemberInfo
        {
            UserId = id,
            GuildId = guildId,
            Username = author.Username,
            GlobalName = author.GlobalName,
            Nickname = NonEmpty(member.Str("nick")),
            AvatarUrl = guildAvatar is { Length: > 0 }
                ? $"{Cdn}/guilds/{guildId}/users/{id}/avatars/{guildAvatar}.{(guildAvatar.StartsWith("a_", StringComparison.Ordinal) ? "gif" : "png")}?size=128"
                : author.AvatarUrl,
            IsBot = author.IsBot,
            RoleIds = member.Ids("roles"),
        };
    }

    /// <summary>A DM "member": recipients are listed under the Direct Messages pseudo guild so names resolve there.</summary>
    public static MemberInfo ToDirectMessageMember(AuthorInfo user) => new()
    {
        UserId = user.Id,
        GuildId = GuildInfo.DirectMessagesId,
        Username = user.Username,
        GlobalName = user.GlobalName,
        AvatarUrl = user.AvatarUrl,
        IsBot = user.IsBot,
    };

    // ---------------------------------------------------------------- guilds

    /// <summary>Name/icon live at the top level, or under <c>properties</c> with the CLIENT_STATE_V2 capability.</summary>
    public static GuildInfo ToGuild(JsonElement guild)
    {
        var id = guild.Id("id") ?? 0;
        var props = guild.Obj("properties") ?? guild;
        return new GuildInfo(id, props.Str("name") ?? guild.Str("name") ?? "Unknown server", GuildIconUrl(id, props.Str("icon") ?? guild.Str("icon")));
    }

    public static RoleInfo ToRole(ulong guildId, JsonElement role)
    {
        int? color = role.Obj("colors") is { } colors ? colors.Int("primary_color") : role.Int("color");
        return new RoleInfo
        {
            Id = role.Id("id") ?? 0,
            GuildId = guildId,
            Name = role.Str("name") ?? "",
            Color = color is 0 ? null : color,
            Position = role.Int("position") ?? 0,
            Hoist = role.Bool("hoist"),
        };
    }

    public static GuildEmojiInfo? ToEmoji(ulong guildId, JsonElement emoji) =>
        emoji.Id("id") is ulong id ? new GuildEmojiInfo(id, guildId, emoji.Str("name") ?? "emoji", emoji.Bool("animated")) : null;

    /// <param name="mergedMembers">With some capabilities READY lists members separately (<c>merged_members[i]</c> for <c>guilds[i]</c>).</param>
    public static GuildSnapshot ToSnapshot(JsonElement guild, IEnumerable<JsonElement>? mergedMembers = null, AuthorInfo? self = null)
    {
        var info = ToGuild(guild);
        var channels = guild.Arr("channels").Concat(guild.Arr("threads"))
            .Select(c => ToChannel(c, info.Id)).OfType<ChannelInfo>().ToList();
        var roles = guild.Arr("roles").Select(r => ToRole(info.Id, r)).ToList();
        var members = guild.Arr("members").Concat(mergedMembers ?? [])
            .Select(m => ToMember(info.Id, m, m.Obj("user") is null && self is not null && m.Id("user_id") == self.Id ? SelfJson(self) : null))
            .OfType<MemberInfo>().DistinctBy(m => m.UserId).ToList();
        var emojis = guild.Arr("emojis").Select(e => ToEmoji(info.Id, e)).OfType<GuildEmojiInfo>().ToList();
        return new GuildSnapshot(info, channels, roles, members, emojis);
    }

    // merged_members entries carry user_id instead of a user object; for our own entry we know the user.
    static JsonElement SelfJson(AuthorInfo self) => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["id"] = self.Id.ToString(CultureInfo.InvariantCulture),
        ["username"] = self.Username,
        ["global_name"] = self.GlobalName,
    });

    // ---------------------------------------------------------------- channels

    /// <summary>
    /// Null for channels the client doesn't list (archived threads). DMs/group DMs land in the Direct Messages pseudo
    /// guild; <paramref name="users"/> resolves <c>recipient_ids</c> when READY dedupes user objects.
    /// </summary>
    public static ChannelInfo? ToChannel(JsonElement channel, ulong? guildId = null, IReadOnlyDictionary<ulong, AuthorInfo>? users = null, ulong? selfId = null)
    {
        if (channel.Id("id") is not ulong id) return null;
        int type = channel.Int("type") ?? 0;
        var kind = type switch
        {
            0 => ChannelKind.Text,
            1 => ChannelKind.DirectMessage,
            2 or 13 => ChannelKind.Voice,
            3 => ChannelKind.GroupDirectMessage,
            4 => ChannelKind.Category,
            5 => ChannelKind.Announcement,
            10 or 11 or 12 => ChannelKind.Thread,
            15 or 16 => ChannelKind.Forum,
            _ => ChannelKind.Other,
        };
        if (kind == ChannelKind.Thread && channel.Obj("thread_metadata") is { } meta && meta.Bool("archived")) return null;

        if (kind is ChannelKind.DirectMessage or ChannelKind.GroupDirectMessage)
        {
            var recipients = Recipients(channel, users).Where(r => r.Id != selfId).ToList();
            var name = NonEmpty(channel.Str("name")) ?? (recipients.Count == 0 ? "Empty group" : string.Join(", ", recipients.Select(r => r.DisplayName)));
            var icon = kind == ChannelKind.GroupDirectMessage
                ? channel.Str("icon") is { Length: > 0 } h ? $"{Cdn}/channel-icons/{id}/{h}.png?size=64" : null
                : recipients.FirstOrDefault()?.AvatarUrl;
            return new ChannelInfo
            {
                Id = id,
                GuildId = GuildInfo.DirectMessagesId,
                Name = name,
                Kind = kind,
                LastMessageId = channel.Id("last_message_id"),
                IconUrl = icon,
            };
        }

        return new ChannelInfo
        {
            Id = id,
            GuildId = channel.Id("guild_id") ?? guildId ?? 0,
            Name = channel.Str("name") ?? "unknown",
            Kind = kind,
            ParentId = channel.Id("parent_id"),
            Position = channel.Int("position") ?? 0,
            Topic = NonEmpty(channel.Str("topic")),
            LastMessageId = channel.Id("last_message_id"),
        };
    }

    public static IReadOnlyList<AuthorInfo> Recipients(JsonElement channel, IReadOnlyDictionary<ulong, AuthorInfo>? users = null)
    {
        var list = channel.Arr("recipients").Select(r => ToAuthor(r)).ToList();
        if (users is not null)
            foreach (var rid in channel.Ids("recipient_ids"))
                if (users.TryGetValue(rid, out var u) && list.All(x => x.Id != rid)) list.Add(u);
        return list;
    }

    // ---------------------------------------------------------------- messages

    public static ChatMessage ToMessage(JsonElement m, ulong? guildId = null, bool includeReply = true)
    {
        guildId = m.Id("guild_id") ?? guildId;
        int type = m.Int("type") ?? 0;
        var author = m.Obj("author") is { } a ? ToAuthor(a, isWebhook: m.Id("webhook_id") is not null) : new AuthorInfo { Id = 0, Username = "Unknown", AvatarUrl = DefaultAvatarUrl(0, null) };

        // Forwards keep their content in message_snapshots; thread starter messages in referenced_message.
        var body = m;
        bool forwarded = false;
        if (m.Obj("message_reference") is { } fref && fref.Int("type") == 1 && m.Arr("message_snapshots").FirstOrDefault() is { ValueKind: JsonValueKind.Object } snap && snap.Obj("message") is { } snapMsg)
        {
            body = snapMsg;
            forwarded = true;
        }
        else if (type == 21 && m.Obj("referenced_message") is { } starter)
        {
            body = starter;
        }

        var (kind, systemText) = Classify(m, type, author);
        var reference = m.Obj("message_reference");
        return new ChatMessage
        {
            Id = m.Id("id") ?? 0,
            ChannelId = m.Id("channel_id") ?? 0,
            GuildId = guildId,
            Author = type == 21 && body.Obj("author") is { } starterAuthor ? ToAuthor(starterAuthor) : author,
            Content = body.Str("content") ?? "",
            EditedAt = m.Time("edited_timestamp"),
            Attachments = body.Arr("attachments").Select(ToAttachment).ToList(),
            Embeds = body.Arr("embeds").Select(ToEmbed).ToList(),
            Reactions = m.Arr("reactions").Select(ToReaction).OfType<ReactionInfo>().ToList(),
            Stickers = body.Arr("sticker_items").Concat(body.Arr("stickers")).Select(ToSticker).OfType<StickerInfo>().DistinctBy(s => s.Id).ToList(),
            ReplyToId = type == 19 ? reference?.Id("message_id") : null,
            ReplyTo = includeReply && type == 19 && m.Obj("referenced_message") is { } referenced ? ToMessage(referenced, guildId, includeReply: false) : null,
            MentionedUserIds = m.Arr("mentions").Select(u => u.Id("id")).OfType<ulong>().ToList(),
            MentionedRoleIds = m.Ids("mention_roles"),
            MentionsEveryone = m.Bool("mention_everyone"),
            WebhookId = m.Id("webhook_id"),
            Kind = kind,
            SystemText = systemText,
            Thread = m.Obj("thread") is { } t && t.Id("id") is ulong threadId
                ? new ThreadRef(threadId, t.Str("name") ?? "thread", t.Int("message_count") ?? 0)
                : null,
            IsForwarded = forwarded,
        };
    }

    static (ChatMessageKind, string?) Classify(JsonElement m, int type, AuthorInfo author)
    {
        var who = author.DisplayName;
        string? other = m.Arr("mentions").FirstOrDefault() is { ValueKind: JsonValueKind.Object } u ? ToAuthor(u).DisplayName : null;
        string content = m.Str("content") ?? "";
        return type switch
        {
            0 or 20 or 21 or 23 => (ChatMessageKind.Default, null),
            19 => (ChatMessageKind.Reply, null),
            1 => (ChatMessageKind.System, $"{who} added {other ?? "someone"} to the group."),
            2 => (ChatMessageKind.System, other is null || other == who ? $"{who} left the group." : $"{who} removed {other} from the group."),
            3 => (ChatMessageKind.System, $"{who} started a call."),
            4 => (ChatMessageKind.System, $"{who} changed the channel name: {content}"),
            5 => (ChatMessageKind.System, $"{who} changed the channel icon."),
            6 => (ChatMessageKind.System, $"{who} pinned a message to this channel."),
            7 => (ChatMessageKind.System, $"{who} joined the server."),
            8 or 9 or 10 or 11 => (ChatMessageKind.System, $"{who} just boosted the server!"),
            12 => (ChatMessageKind.System, $"{who} added {content} to this channel."),
            18 => (ChatMessageKind.System, $"{who} started a thread: {content}"),
            24 => (ChatMessageKind.System, "AutoMod blocked a message."),
            46 => (ChatMessageKind.System, "A poll has closed."),
            _ => (ChatMessageKind.System, $"{who}: message type {type}"),
        };
    }

    static AttachmentInfo ToAttachment(JsonElement a) => new()
    {
        Id = a.Id("id") ?? 0,
        FileName = a.Str("filename") ?? "file",
        Url = a.Str("url") ?? "",
        ProxyUrl = a.Str("proxy_url"),
        ContentType = a.Str("content_type"),
        Size = a.Long("size") ?? 0,
        Width = a.Int("width"),
        Height = a.Int("height"),
        Description = a.Str("description"),
    };

    static ReactionInfo? ToReaction(JsonElement r) =>
        r.Obj("emoji") is { } e ? new ReactionInfo(ToEmojiRef(e), r.Int("count") ?? 0, r.Bool("me")) : null;

    public static EmojiRef ToEmojiRef(JsonElement emoji) => new(emoji.Id("id"), emoji.Str("name") ?? "", emoji.Bool("animated"));

    static StickerInfo? ToSticker(JsonElement s)
    {
        if (s.Id("id") is not ulong id) return null;
        // format_type: 1 png, 2 apng, 3 lottie (no image; Discord's own CDN png fallback), 4 gif
        var ext = s.Int("format_type") == 4 ? "gif" : "png";
        return new StickerInfo(id, s.Str("name") ?? "sticker", $"https://media.discordapp.net/stickers/{id}.{ext}?size=160");
    }

    public static EmbedInfo ToEmbed(JsonElement e) => new()
    {
        Type = e.Str("type"),
        Title = e.Str("title"),
        Description = e.Str("description"),
        Url = e.Str("url"),
        Color = e.Int("color"),
        Timestamp = e.Time("timestamp"),
        AuthorName = e.Obj("author")?.Str("name"),
        AuthorUrl = e.Obj("author")?.Str("url"),
        AuthorIconUrl = e.Obj("author") is { } au ? au.Str("proxy_icon_url") ?? au.Str("icon_url") : null,
        FooterText = e.Obj("footer")?.Str("text"),
        FooterIconUrl = e.Obj("footer") is { } f ? f.Str("proxy_icon_url") ?? f.Str("icon_url") : null,
        ImageUrl = e.Obj("image") is { } i ? i.Str("proxy_url") ?? i.Str("url") : null,
        ThumbnailUrl = e.Obj("thumbnail") is { } t ? t.Str("proxy_url") ?? t.Str("url") : null,
        VideoUrl = e.Obj("video") is { } v ? v.Str("proxy_url") ?? v.Str("url") : null,
        ProviderName = e.Obj("provider")?.Str("name"),
        Fields = e.Arr("fields").Select(x => new EmbedFieldInfo(x.Str("name") ?? "", x.Str("value") ?? "", x.Bool("inline"))).ToList(),
    };
}
