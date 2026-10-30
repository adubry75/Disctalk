using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.DevHost.Demo;

/// <summary>
/// A fake Discord for running the UI without a bot token: one guild ("Cat Café"), seeded scrollback, simulated
/// members typing and chatting, and working send/edit/delete/react/webhook paths. Purely in-memory.
/// </summary>
public sealed class DemoHub : IDiscordConnectionFactory, IWebhookExecutor
{
    /// <summary>Shaped like a real token so validation passes; it is never sent anywhere.</summary>
    public const string FakeToken = "MTEwMDAwMDAwMDAwMDAwMTAw.DEMO00.demo-token-not-real-demo-token-not-real";

    public const ulong GuildId = 1100000000000000001;
    public const ulong BotId = 1100000000000000100;
    const ulong WelcomeId = 1100000000000000201, AnnouncementsId = 1100000000000000202, GeneralId = 1100000000000000203,
        CatPicsId = 1100000000000000204, BotTestingId = 1100000000000000205, LoungeId = 1100000000000000206;
    const ulong InfoCategory = 1100000000000000301, ChatCategory = 1100000000000000302, VoiceCategory = 1100000000000000303;
    const ulong AdminRole = 1100000000000000401, ModRole = 1100000000000000402, CatLoverRole = 1100000000000000403;

    readonly Lock _lock = new();
    readonly Dictionary<ulong, SortedList<ulong, ChatMessage>> _messages = new();
    readonly Dictionary<ulong, WebhookInfo> _webhooks = new();
    readonly Random _random = new(7);
    ulong _sequence;
    DemoConnection? _active;

    public static readonly AuthorInfo Bot = new() { Id = BotId, Username = "whiskers", GlobalName = "Whiskers", AvatarUrl = "demo/bot.svg", IsBot = true };

    static readonly MemberInfo[] Members =
    [
        Member(1100000000000000101, "ada", "Ada", 0, AdminRole, CatLoverRole),
        Member(1100000000000000102, "mochi", "Mochi", 1, ModRole),
        Member(1100000000000000103, "biscuit", "Biscuit", 2, CatLoverRole),
        Member(1100000000000000104, "pixel", "Pixel", 3),
        Member(1100000000000000105, "noodle", "Noodle", 4, CatLoverRole),
        Member(1100000000000000106, "clover", "Clover", 5),
        Member(1100000000000000107, "pepper", "Pepper", 0),
        Member(1100000000000000108, "ziggy", "Ziggy", 1),
        new MemberInfo { UserId = BotId, GuildId = GuildId, Username = "whiskers", GlobalName = "Whiskers", AvatarUrl = "demo/bot.svg", IsBot = true, RoleIds = [ModRole] },
    ];

    static MemberInfo Member(ulong id, string user, string name, int avatar, params ulong[] roles) => new()
    {
        UserId = id, GuildId = GuildId, Username = user, GlobalName = name,
        AvatarUrl = $"https://cdn.discordapp.com/embed/avatars/{avatar}.png", RoleIds = roles,
    };

    static AuthorInfo AuthorOf(MemberInfo m) => new() { Id = m.UserId, Username = m.Username, GlobalName = m.GlobalName, AvatarUrl = m.AvatarUrl, IsBot = m.IsBot };

    public DemoHub()
    {
        Seed();
    }

    public IDiscordConnection Create(AccountKind kind, string token) => _active = new DemoConnection(this);

    internal GuildSnapshot Snapshot() => new(
        new GuildInfo(GuildId, "Cat Café", null),
        [
            new() { Id = InfoCategory, GuildId = GuildId, Name = "Information", Kind = ChannelKind.Category, Position = 0 },
            new() { Id = ChatCategory, GuildId = GuildId, Name = "Chat", Kind = ChannelKind.Category, Position = 1 },
            new() { Id = VoiceCategory, GuildId = GuildId, Name = "Voice", Kind = ChannelKind.Category, Position = 2 },
            new() { Id = WelcomeId, GuildId = GuildId, Name = "welcome", ParentId = InfoCategory, Position = 0, Topic = "Read me first 🐾", LastMessageId = LastId(WelcomeId) },
            new() { Id = AnnouncementsId, GuildId = GuildId, Name = "announcements", Kind = ChannelKind.Announcement, ParentId = InfoCategory, Position = 1, LastMessageId = LastId(AnnouncementsId) },
            new() { Id = GeneralId, GuildId = GuildId, Name = "general", ParentId = ChatCategory, Position = 0, Topic = "Purr-fectly normal conversation. Be nice to the cats.", LastMessageId = LastId(GeneralId) },
            new() { Id = CatPicsId, GuildId = GuildId, Name = "cat-pics", ParentId = ChatCategory, Position = 1, Topic = "Pictures of cats. Only cats. Okay, maybe a dog.", LastMessageId = LastId(CatPicsId) },
            new() { Id = BotTestingId, GuildId = GuildId, Name = "bot-testing", ParentId = ChatCategory, Position = 2, LastMessageId = LastId(BotTestingId) },
            new() { Id = LoungeId, GuildId = GuildId, Name = "Lounge", Kind = ChannelKind.Voice, ParentId = VoiceCategory, Position = 0 },
        ],
        [
            new() { Id = GuildId, GuildId = GuildId, Name = "@everyone", Position = 0 },
            new() { Id = AdminRole, GuildId = GuildId, Name = "Head Cat", Color = 0xF2A65A, Position = 3, Hoist = true },
            new() { Id = ModRole, GuildId = GuildId, Name = "Moderators", Color = 0x6CB4EE, Position = 2, Hoist = true },
            new() { Id = CatLoverRole, GuildId = GuildId, Name = "Cat Lovers", Color = 0xE07A5F, Position = 1 },
        ],
        Members,
        [new GuildEmojiInfo(1100000000000000501, GuildId, "blobcat", false), new GuildEmojiInfo(1100000000000000502, GuildId, "catjam", true)]);

    ulong? LastId(ulong channelId)
    {
        lock (_lock) return _messages.TryGetValue(channelId, out var list) && list.Count > 0 ? list.Keys[^1] : null;
    }

    ulong NextId(DateTimeOffset at)
    {
        lock (_lock) return Snowflakes.FromTimestamp(at) + (++_sequence % 4096);
    }

    ChatMessage Add(ChatMessage message)
    {
        lock (_lock)
        {
            if (!_messages.TryGetValue(message.ChannelId, out var list)) _messages[message.ChannelId] = list = new();
            list[message.Id] = message;
        }
        return message;
    }

    internal ChatMessage? Get(ulong channelId, ulong messageId)
    {
        lock (_lock) return _messages.GetValueOrDefault(channelId)?.GetValueOrDefault(messageId);
    }

    internal IReadOnlyList<ChatMessage> Before(ulong channelId, ulong? before, int limit)
    {
        lock (_lock)
        {
            if (!_messages.TryGetValue(channelId, out var list)) return [];
            return list.Values.Where(m => before is null || m.Id < before).Reverse().Take(limit).ToList();
        }
    }

    internal ChatMessage Post(ulong channelId, AuthorInfo author, string content, ChatMessage? replyTo = null,
        IReadOnlyList<AttachmentInfo>? attachments = null, IReadOnlyList<EmbedInfo>? embeds = null, ulong? webhookId = null, DateTimeOffset? at = null)
        => Add(new ChatMessage
        {
            Id = NextId(at ?? DateTimeOffset.UtcNow),
            ChannelId = channelId,
            GuildId = GuildId,
            Author = author,
            Content = content,
            ReplyToId = replyTo?.Id,
            ReplyTo = replyTo,
            Kind = replyTo is null ? ChatMessageKind.Default : ChatMessageKind.Reply,
            Attachments = attachments ?? [],
            Embeds = embeds ?? [],
            WebhookId = webhookId,
            MentionedUserIds = Core.Sending.MentionPolicy.ExtractUserMentions(content).Concat(replyTo is null ? [] : [replyTo.Author.Id]).ToList(),
            MentionsEveryone = content.Contains("@everyone") || content.Contains("@here"),
        });

    internal void Replace(ChatMessage message) => Add(message);

    internal bool Remove(ulong channelId, ulong messageId)
    {
        lock (_lock) return _messages.GetValueOrDefault(channelId)?.Remove(messageId) ?? false;
    }

    internal WebhookInfo? FindWebhook(ulong channelId)
    {
        lock (_lock) return _webhooks.GetValueOrDefault(channelId);
    }

    internal WebhookInfo CreateWebhook(ulong channelId, string name)
    {
        var hook = new WebhookInfo(NextId(DateTimeOffset.UtcNow), channelId, name, "demo-webhook-token", BotId);
        lock (_lock) _webhooks[channelId] = hook;
        return hook;
    }

    // ------------------------------------------------------------------ IWebhookExecutor

    public async Task<ulong> ExecuteAsync(WebhookInfo webhook, WebhookPayload payload, IReadOnlyList<OutgoingFile> files, CancellationToken cancellationToken = default)
    {
        var author = new AuthorInfo
        {
            Id = webhook.Id,
            Username = payload.Username ?? webhook.Name,
            AvatarUrl = payload.AvatarUrl ?? "https://cdn.discordapp.com/embed/avatars/5.png",
            IsBot = true,
            IsWebhook = true,
        };
        var message = Post(webhook.ChannelId, author, payload.Content, attachments: DemoConnection.ToAttachments(files), webhookId: webhook.Id);
        await Emit(new MessageCreateEvent(message));
        return message.Id;
    }

    public async Task EditAsync(WebhookInfo webhook, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default)
    {
        if (Get(webhook.ChannelId, messageId) is not { } existing) throw new InvalidOperationException("Unknown message");
        var edited = existing with { Content = content, EditedAt = DateTimeOffset.UtcNow };
        Replace(edited);
        await Emit(new MessageUpdateEvent(edited));
    }

    public async Task DeleteAsync(WebhookInfo webhook, ulong messageId, CancellationToken cancellationToken = default)
    {
        if (Remove(webhook.ChannelId, messageId)) await Emit(new MessageDeleteEvent(webhook.ChannelId, [messageId]));
    }

    internal ValueTask Emit(GatewayEvent e) => _active?.Emit(e) ?? ValueTask.CompletedTask;

    // ------------------------------------------------------------------ simulated activity

    static readonly string[] Chatter =
    [
        "my cat just knocked a glass off the table while making eye contact 😐",
        "anyone else's cat scream at 3am for no reason",
        "**hot take:** boxes are better than any cat bed you can buy",
        "Biscuit learned to open the treat drawer. we are doomed",
        "||the cat is sitting on my keyboard right now|| asdfghjkl",
        "`sudo feed cat` — permission denied",
        "> if I fits I sits\nwords to live by",
        "new toy arrived, cat prefers the packaging. as expected 📦",
        "does anyone know a good vet near downtown?",
        "she's doing the loaf again 🍞🐈",
        "just saw <@1100000000000000102>'s cat on the stream, absolute unit",
        "brb, cat needs to be let in. and out. and in again.",
    ];

    internal async Task RunActivityAsync(CancellationToken ct)
    {
        var channels = new[] { GeneralId, GeneralId, GeneralId, CatPicsId, BotTestingId };
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
            while (!ct.IsCancellationRequested)
            {
                var member = Members[_random.Next(Members.Length - 1)];
                var channel = channels[_random.Next(channels.Length)];
                await Emit(new TypingStartEvent(channel, member.UserId, member, DateTimeOffset.UtcNow));
                await Task.Delay(TimeSpan.FromSeconds(3 + _random.Next(3)), ct);

                var roll = _random.Next(10);
                if (roll < 7)
                {
                    await Emit(new MessageCreateEvent(Post(channel, AuthorOf(member), Chatter[_random.Next(Chatter.Length)])));
                }
                else if (Before(channel, null, 5) is { Count: > 0 } recent)
                {
                    var target = recent[_random.Next(recent.Count)];
                    var emoji = new EmojiRef(null, new[] { "😹", "❤️", "🐾", "👀" }[_random.Next(4)]);
                    var updated = WithReaction(target, emoji, member.UserId == BotId);
                    Replace(updated);
                    await Emit(new ReactionAddEvent(channel, target.Id, member.UserId, emoji));
                }
                await Task.Delay(TimeSpan.FromSeconds(6 + _random.Next(8)), ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    internal static ChatMessage WithReaction(ChatMessage m, EmojiRef emoji, bool me, int delta = 1)
    {
        var list = m.Reactions.ToList();
        int i = list.FindIndex(r => r.Emoji.Matches(emoji));
        if (i >= 0)
        {
            var count = list[i].Count + delta;
            if (count <= 0) list.RemoveAt(i);
            else list[i] = list[i] with { Count = count, Me = me ? delta > 0 : list[i].Me };
        }
        else if (delta > 0) list.Add(new ReactionInfo(emoji, delta, me));
        return m with { Reactions = list };
    }

    // ------------------------------------------------------------------ seed data

    void Seed()
    {
        var now = DateTimeOffset.UtcNow;
        var m = Members.ToDictionary(x => x.GlobalName!, AuthorOf);

        Post(WelcomeId, m["Ada"], "# Welcome to the Cat Café ☕🐈\nThis is a tiny server for cat people.\n\n- Be kind\n- Post cats in <#1100000000000000204>\n- Chat in <#1100000000000000203>\n\n-# Rules subject to change at the cat's discretion.", at: now.AddDays(-30));
        Post(AnnouncementsId, m["Ada"], "@everyone the café now has a **bot**! Say hi to <@1100000000000000100> 🐾", at: now.AddDays(-3));

        // Older history in #general spread over two days so day dividers and scrollback show up.
        var start = now.AddDays(-2).AddHours(-3);
        var authors = new[] { "Biscuit", "Pixel", "Noodle", "Clover", "Pepper", "Ziggy", "Mochi", "Ada" };
        for (int i = 0; i < 90; i++)
        {
            var who = m[authors[(i * 7 + i / 3) % authors.Length]];
            Post(GeneralId, who, Chatter[(i * 5) % Chatter.Length] + (i % 9 == 0 ? $" (#{i})" : ""), at: start.AddMinutes(i * 25 + (i % 3)));
        }

        var t = now.AddMinutes(-42);
        Post(GeneralId, m["Mochi"], "Markdown check: **bold**, *italic*, __underline__, ~~strike~~, `inline code`, ||spoiler||, and a [masked link](https://discord.com/developers/docs).", at: t);
        var q = Post(GeneralId, m["Pixel"], "Does the bot support code blocks?\n```cs\nvar cat = new Cat(\"Biscuit\");\ncat.Meow(times: 3);\n```", at: t.AddMinutes(1));
        Post(GeneralId, Bot, "It does! Timestamps too: <t:1767225600:F> was <t:1767225600:R>.", replyTo: q, at: t.AddMinutes(2));
        var pic = Post(CatPicsId, m["Noodle"], "behold, the loaf", attachments:
        [
            new AttachmentInfo { Id = 1100000000000000601, FileName = "loaf.svg", Url = "demo/cat-loaf.svg", ContentType = "image/svg+xml", Width = 480, Height = 320, Size = 2048 },
        ], at: t.AddMinutes(3));
        Replace(WithReaction(WithReaction(pic, new EmojiRef(null, "😻"), false, 4), new EmojiRef(1100000000000000501, "blobcat"), true, 2));
        Post(GeneralId, m["Clover"], "found a cool article https://en.wikipedia.org/wiki/Cat", embeds:
        [
            new EmbedInfo
            {
                Type = "article", ProviderName = "Wikipedia", Title = "Cat - Wikipedia", Url = "https://en.wikipedia.org/wiki/Cat", Color = 0xF2A65A,
                Description = "The **cat** (*Felis catus*), also referred to as the domestic cat, is a small domesticated carnivorous mammal.",
                ThumbnailUrl = "demo/cat-loaf.svg",
                Fields = [new("Scientific name", "*Felis catus*", true), new("Lifespan", "12–18 years", true)],
            },
        ], at: t.AddMinutes(5));
        var edited = Post(GeneralId, m["Biscuit"], "reminder: <@&1100000000000000402> are on snack duty this week 🍪", at: t.AddMinutes(8));
        Replace(edited with { EditedAt = t.AddMinutes(9) });
        Replace(WithReaction(Post(GeneralId, m["Ziggy"], "lol 😹😹", at: t.AddMinutes(10)), new EmojiRef(null, "😹"), false, 3));
        Post(GeneralId, m["Ada"], "<:blobcat:1100000000000000501>", at: t.AddMinutes(11));

        Post(BotTestingId, Bot, "Bot online. Try sending a message, reacting, or switching the send mode to **Webhook** in the composer.", at: now.AddMinutes(-5));
    }
}
