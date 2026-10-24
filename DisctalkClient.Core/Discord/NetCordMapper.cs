using DisctalkClient.Core.Models;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace DisctalkClient.Core.Discord;

/// <summary>NetCord entities → the client's library-neutral models.</summary>
internal static class NetCordMapper
{
    public static AuthorInfo ToAuthor(User user, bool isWebhook = false) => new()
    {
        Id = user.Id,
        Username = user.Username,
        GlobalName = string.IsNullOrEmpty(user.GlobalName) ? null : user.GlobalName,
        AvatarUrl = AvatarUrl(user),
        IsBot = user.IsBot,
        IsWebhook = isWebhook,
    };

    public static string AvatarUrl(User user) =>
        user.HasAvatar ? user.GetAvatarUrl()!.ToString(128) : user.DefaultAvatarUrl.ToString();

    public static MemberInfo ToMember(GuildUser user) => new()
    {
        UserId = user.Id,
        GuildId = user.GuildId,
        Username = user.Username,
        GlobalName = string.IsNullOrEmpty(user.GlobalName) ? null : user.GlobalName,
        Nickname = string.IsNullOrEmpty(user.Nickname) ? null : user.Nickname,
        AvatarUrl = user.HasGuildAvatar ? user.GetGuildAvatarUrl()!.ToString(128) : AvatarUrl(user),
        IsBot = user.IsBot,
        RoleIds = user.RoleIds.ToList(),
    };

    public static RoleInfo ToRole(Role role) => new()
    {
        Id = role.Id,
        GuildId = role.GuildId,
        Name = role.Name,
        Color = role.Colors.PrimaryColor.RawValue == 0 ? null : role.Colors.PrimaryColor.RawValue,
        Position = role.RawPosition,
        Hoist = role.Hoist,
    };

    public static GuildInfo ToGuild(RestGuild guild) =>
        new(guild.Id, guild.Name, guild.HasIcon ? guild.GetIconUrl()!.ToString(128) : null);

    public static GuildEmojiInfo ToEmoji(GuildEmoji emoji) => new(emoji.Id, emoji.GuildId, emoji.Name, emoji.Animated);

    /// <summary>Null for channels the client doesn't list (threads, DMs).</summary>
    public static ChannelInfo? ToChannel(IGuildChannel channel)
    {
        if (channel is GuildThread) return null;
        var (kind, parent, topic, last) = channel switch
        {
            CategoryGuildChannel => (ChannelKind.Category, (ulong?)null, (string?)null, (ulong?)null),
            StageGuildChannel s => (ChannelKind.Voice, s.ParentId, null, null),
            VoiceGuildChannel v => (ChannelKind.Voice, v.ParentId, null, v.LastMessageId),
            AnnouncementGuildChannel a => (ChannelKind.Announcement, a.ParentId, a.Topic, a.LastMessageId),
            TextGuildChannel t => (ChannelKind.Text, t.ParentId, t.Topic, t.LastMessageId),
            ForumGuildChannel f => (ChannelKind.Forum, f.ParentId, f.Topic, null),
            _ => (ChannelKind.Other, null, null, null),
        };
        return new ChannelInfo
        {
            Id = channel.Id,
            GuildId = channel.GuildId,
            Name = channel.Name,
            Kind = kind,
            ParentId = parent,
            Position = channel.Position ?? 0,
            Topic = string.IsNullOrEmpty(topic) ? null : topic,
            LastMessageId = last,
        };
    }

    public static GuildSnapshot ToSnapshot(Guild guild) => new(
        ToGuild(guild),
        guild.Channels.Values.Select(ToChannel).OfType<ChannelInfo>().ToList(),
        guild.Roles.Values.Select(ToRole).ToList(),
        guild.Users.Values.Select(ToMember).ToList(),
        guild.Emojis.Values.Select(ToEmoji).ToList());

    public static EmojiRef ToEmojiRef(MessageReactionEmoji emoji) => new(emoji.Id, emoji.Name ?? "", emoji.Animated);

    public static ChatMessage ToMessage(RestMessage m, ulong? guildId = null, bool includeReply = true)
    {
        guildId ??= (m as Message)?.GuildId;
        var (kind, systemText) = Classify(m);
        return new ChatMessage
        {
            Id = m.Id,
            ChannelId = m.ChannelId,
            GuildId = guildId,
            Author = ToAuthor(m.Author, isWebhook: m.WebhookId is not null),
            Content = m.Content ?? "",
            EditedAt = m.EditedAt,
            Attachments = m.Attachments.Select(ToAttachment).ToList(),
            Embeds = m.Embeds.Select(ToEmbed).ToList(),
            Reactions = m.Reactions.Select(r => new ReactionInfo(ToEmojiRef(r.Emoji), r.Count, r.Me)).ToList(),
            Stickers = m.Stickers.Select(s => new StickerInfo(s.Id, s.Name,
                $"https://media.discordapp.net/stickers/{s.Id}.{(s.Format == StickerFormat.Gif ? "gif" : "png")}?size=160")).ToList(),
            ReplyToId = m.Type == MessageType.Reply ? m.MessageReference?.MessageId : null,
            ReplyTo = includeReply && m.Type == MessageType.Reply && m.ReferencedMessage is { } referenced
                ? ToMessage(referenced, guildId, includeReply: false)
                : null,
            MentionedUserIds = m.MentionedUsers.Select(u => u.Id).ToList(),
            MentionedRoleIds = m.MentionedRoleIds.ToList(),
            MentionsEveryone = m.MentionEveryone,
            WebhookId = m.WebhookId,
            Kind = kind,
            SystemText = systemText,
        };
    }

    static (ChatMessageKind, string?) Classify(RestMessage m)
    {
        var who = string.IsNullOrEmpty(m.Author.GlobalName) ? m.Author.Username : m.Author.GlobalName;
        return m.Type switch
        {
            MessageType.Default or MessageType.ApplicationCommand or MessageType.ContextMenuCommand => (ChatMessageKind.Default, null),
            MessageType.Reply => (ChatMessageKind.Reply, null),
            MessageType.GuildMembedJoined => (ChatMessageKind.System, $"{who} joined the server."),
            MessageType.MessagePinned => (ChatMessageKind.System, $"{who} pinned a message to this channel."),
            MessageType.PremiumGuildSubscription or MessageType.PremiumGuildSubscriptionTier1
                or MessageType.PremiumGuildSubscriptionTier2 or MessageType.PremiumGuildSubscriptionTier3
                => (ChatMessageKind.System, $"{who} just boosted the server!"),
            MessageType.ThreadCreated => (ChatMessageKind.System, $"{who} started a thread: {m.Content}"),
            MessageType.ChannelFollowAdd => (ChatMessageKind.System, $"{who} added {m.Content} to this channel."),
            MessageType.PollResult => (ChatMessageKind.System, "A poll has closed."),
            _ => (ChatMessageKind.System, $"{who}: {m.Type}"),
        };
    }

    static AttachmentInfo ToAttachment(Attachment a) => new()
    {
        Id = a.Id,
        FileName = a.FileName,
        Url = a.Url,
        ProxyUrl = a.ProxyUrl,
        ContentType = a.ContentType,
        Size = a.Size,
        Width = (a as ImageAttachment)?.Width,
        Height = (a as ImageAttachment)?.Height,
        Description = a.Description,
    };

    static EmbedInfo ToEmbed(Embed e) => new()
    {
        Type = e.Type?.ToString().ToLowerInvariant(),
        Title = e.Title,
        Description = e.Description,
        Url = e.Url,
        Color = e.Color?.RawValue,
        Timestamp = e.Timestamp,
        AuthorName = e.Author?.Name,
        AuthorUrl = e.Author?.Url,
        AuthorIconUrl = e.Author?.ProxyIconUrl ?? e.Author?.IconUrl,
        FooterText = e.Footer?.Text,
        FooterIconUrl = e.Footer?.ProxyIconUrl ?? e.Footer?.IconUrl,
        ImageUrl = e.Image?.ProxyUrl ?? e.Image?.Url,
        ThumbnailUrl = e.Thumbnail?.ProxyUrl ?? e.Thumbnail?.Url,
        VideoUrl = e.Video?.ProxyUrl ?? e.Video?.Url,
        ProviderName = e.Provider?.Name,
        Fields = e.Fields.Select(f => new EmbedFieldInfo(f.Name, f.Value, f.Inline)).ToList(),
    };
}
