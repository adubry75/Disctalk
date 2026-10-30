using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Security;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisctalkClient.Core.State;

public enum ChangeKind { Connection, Guilds, Channels, Members, Roles, Emojis, Messages, Typing, Unread }

public sealed record ClientChange(ChangeKind Kind, ulong? GuildId = null, ulong? ChannelId = null);

/// <summary>
/// The client's in-memory view of Discord: guilds, channels, roles, members, emoji, per-channel message caches,
/// typing and unread state. Fed by gateway events; the UI reads snapshots and listens to <see cref="Changed"/>.
/// Gateway events arrive on background threads, so all state is guarded by one lock and <see cref="Changed"/>
/// fires outside it.
/// </summary>
public sealed class ChatClient : IAsyncDisposable
{
    public const int HistoryPageSize = 50;
    public const string UnreadStorageKey = "disctalkclient.lastread";
    static readonly TimeSpan TypingResendInterval = TimeSpan.FromSeconds(8);

    readonly IDiscordConnectionFactory _factory;
    readonly IWebhookExecutor _webhookExecutor;
    readonly SettingsService _settings;
    readonly ISettingsStore _store;
    readonly TimeProvider _time;
    readonly ILogger _log;

    readonly Lock _lock = new();
    readonly Dictionary<ulong, GuildState> _guilds = new();
    readonly Dictionary<ulong, ChannelInfo> _channels = new();
    readonly Dictionary<ulong, MessageCache> _messages = new();
    readonly HashSet<ulong> _historyLoading = new();
    readonly Dictionary<ulong, DateTimeOffset> _typingSentAt = new();
    readonly UnreadTracker _unread = new();
    readonly TypingTracker _typing;

    IDiscordConnection? _connection;
    MessageSender? _sender;
    WebhookDirectory? _webhooks;
    int _unreadSaveQueued;

    public ChatClient(
        IDiscordConnectionFactory factory,
        IWebhookExecutor webhookExecutor,
        SettingsService settings,
        ISettingsStore store,
        TimeProvider time,
        ILogger<ChatClient>? logger = null)
    {
        _factory = factory;
        _webhookExecutor = webhookExecutor;
        _settings = settings;
        _store = store;
        _time = time;
        _log = logger ?? (ILogger)NullLogger.Instance;
        _typing = new TypingTracker(time);
    }

    public event Action<ClientChange>? Changed;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? StatusDetail { get; private set; }
    public AuthorInfo? CurrentUser { get; private set; }
    /// <summary>The kind of account of the current (or last) connection.</summary>
    public AccountKind Account { get; private set; } = AccountKind.User;
    public ulong? ActiveChannelId { get; private set; }

    int _historyGeneration;

    /// <summary>Bumped whenever cached history is thrown away (a session that couldn't be resumed); views reload.</summary>
    public int HistoryGeneration => Volatile.Read(ref _historyGeneration);

    sealed class GuildState(GuildInfo info)
    {
        public GuildInfo Info = info;
        public readonly Dictionary<ulong, RoleInfo> Roles = new();
        public readonly Dictionary<ulong, MemberInfo> Members = new();
        public readonly Dictionary<ulong, GuildEmojiInfo> Emojis = new();
    }

    // ---------------------------------------------------------------- connection

    public async Task ConnectAsync(AccountKind kind, string token, CancellationToken cancellationToken = default)
    {
        if (TokenValidator.Validate(kind, token) is { } problem) throw new ArgumentException(problem, nameof(token));
        await DisconnectAsync().ConfigureAwait(false);

        _unread.Restore(await _store.GetAsync(UnreadStorageKey).ConfigureAwait(false));

        var connection = _factory.Create(kind, TokenValidator.Normalize(kind, token));
        Account = kind;
        _webhooks = new WebhookDirectory(connection.Rest, () => CurrentUser?.Id);
        _sender = new MessageSender(connection.Rest, _webhooks, _webhookExecutor, _settings, () => CurrentUser?.Id);
        connection.EventReceived += OnEventAsync;
        _connection = connection;

        SetState(ConnectionState.Connecting, "Connecting to Discord…");
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning("Gateway start failed: {Error}", SecretRedactor.Redact(ex.Message));
            SetState(ConnectionState.Failed, "Couldn't connect: " + SecretRedactor.Redact(ex.Message));
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var connection = _connection;
        _connection = null;
        _sender = null;
        _webhooks = null;
        if (connection is not null)
        {
            connection.EventReceived -= OnEventAsync;
            try { await connection.StopAsync().ConfigureAwait(false); } catch (Exception ex) { _log.LogDebug("Stop failed: {Error}", SecretRedactor.Redact(ex.Message)); }
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        lock (_lock)
        {
            _guilds.Clear();
            _channels.Clear();
            _messages.Clear();
            _historyLoading.Clear();
            CurrentUser = null;
        }
        SetState(ConnectionState.Disconnected, null);
        Raise(new ClientChange(ChangeKind.Guilds));
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    /// <summary>Hosts call this when the OS reports connectivity again (MAUI: Connectivity.ConnectivityChanged).</summary>
    public void NotifyNetworkAvailable()
    {
        if (State is ConnectionState.Reconnecting or ConnectionState.Connecting) _connection?.NetworkAvailable();
    }

    void SetState(ConnectionState state, string? detail)
    {
        State = state;
        StatusDetail = detail;
        Raise(new ClientChange(ChangeKind.Connection));
    }

    // ---------------------------------------------------------------- queries

    public IReadOnlyList<GuildInfo> Guilds
    {
        // Direct Messages first (user accounts), then servers by name.
        get { lock (_lock) return _guilds.Values.Select(g => g.Info).OrderBy(g => !g.IsDirectMessages).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList(); }
    }

    public GuildInfo? GetGuild(ulong guildId)
    {
        lock (_lock) return _guilds.GetValueOrDefault(guildId)?.Info;
    }

    public IReadOnlyList<ChannelInfo> GetChannels(ulong guildId)
    {
        lock (_lock) return _channels.Values.Where(c => c.GuildId == guildId).ToList();
    }

    public ChannelInfo? GetChannel(ulong channelId)
    {
        lock (_lock) return _channels.GetValueOrDefault(channelId);
    }

    public IReadOnlyDictionary<ulong, RoleInfo> GetRoles(ulong guildId)
    {
        lock (_lock) return _guilds.TryGetValue(guildId, out var g) ? new Dictionary<ulong, RoleInfo>(g.Roles) : new Dictionary<ulong, RoleInfo>();
    }

    public RoleInfo? GetRole(ulong guildId, ulong roleId)
    {
        lock (_lock) return _guilds.GetValueOrDefault(guildId)?.Roles.GetValueOrDefault(roleId);
    }

    public IReadOnlyList<MemberInfo> GetMembers(ulong guildId)
    {
        lock (_lock) return _guilds.TryGetValue(guildId, out var g) ? g.Members.Values.ToList() : [];
    }

    public MemberInfo? GetMember(ulong guildId, ulong userId)
    {
        lock (_lock) return _guilds.GetValueOrDefault(guildId)?.Members.GetValueOrDefault(userId);
    }

    public string? GetNameColor(ulong guildId, ulong userId)
    {
        lock (_lock)
            return _guilds.TryGetValue(guildId, out var g) ? MemberGrouping.NameColor(g.Members.GetValueOrDefault(userId), g.Roles) : null;
    }

    public IReadOnlyList<GuildEmojiInfo> GetEmojis(ulong guildId)
    {
        lock (_lock) return _guilds.TryGetValue(guildId, out var g) ? g.Emojis.Values.OrderBy(e => e.Name).ToList() : [];
    }

    public IReadOnlyList<ChatMessage> GetMessages(ulong channelId)
    {
        lock (_lock) return _messages.TryGetValue(channelId, out var cache) ? cache.Snapshot() : [];
    }

    public ChatMessage? GetMessage(ulong channelId, ulong messageId)
    {
        lock (_lock) return _messages.GetValueOrDefault(channelId)?.Get(messageId);
    }

    public bool HasMoreHistory(ulong channelId)
    {
        lock (_lock) return !_messages.TryGetValue(channelId, out var cache) || cache.HasMoreBefore;
    }

    public bool IsHistoryLoaded(ulong channelId)
    {
        lock (_lock) return _messages.TryGetValue(channelId, out var cache) && cache.IsInitialized;
    }

    public bool IsLoadingHistory(ulong channelId)
    {
        lock (_lock) return _historyLoading.Contains(channelId);
    }

    public bool IsUnread(ulong channelId) => _unread.IsUnread(channelId);

    public IReadOnlyList<string> GetTypingNames(ulong channelId) => _typing.GetTypingNames(channelId, CurrentUser?.Id);

    public bool CanEdit(ChatMessage message) => _sender?.CanEdit(message) ?? false;

    // ---------------------------------------------------------------- navigation & history

    public void SetActiveChannel(ulong? channelId)
    {
        ActiveChannelId = channelId;
        if (channelId is not ulong id) return;
        if (_unread.MarkRead(id)) QueueUnreadSave();
        Raise(new ClientChange(ChangeKind.Unread, GetChannel(id)?.GuildId, id));

        var guildId = GetChannel(id)?.GuildId;
        if (guildId is ulong focusGuild && _connection is { } focused)
            _ = Task.Run(async () =>
            {
                try { await focused.FocusChannelAsync(focusGuild, id).ConfigureAwait(false); }
                catch (Exception ex) { _log.LogDebug("Channel focus failed: {Error}", SecretRedactor.Redact(ex.Message)); }
            });
        if (_settings.Current.LastChannelId != id)
            _ = _settings.UpdateAsync(s => s with { LastChannelId = id, LastGuildId = guildId ?? s.LastGuildId });

        // Resolve our webhook early so the edit/delete buttons know which webhook messages are ours.
        if (_webhooks is { } hooks && hooks.TryGetCached(id) is null)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (await hooks.FindAsync(id).ConfigureAwait(false) is not null) Raise(new ClientChange(ChangeKind.Messages, guildId, id));
                }
                catch (Exception ex) { _log.LogDebug("Webhook lookup failed: {Error}", SecretRedactor.Redact(ex.Message)); }
            });
    }

    /// <summary>
    /// The channel for a thread chip under a message. Threads the gateway didn't list (archived, or never joined) are
    /// added on the fly so they can still be opened and read.
    /// </summary>
    public ChannelInfo OpenThread(ThreadRef thread, ulong parentChannelId)
    {
        ChannelInfo channel;
        lock (_lock)
        {
            if (_channels.TryGetValue(thread.Id, out var known)) return known;
            var parent = _channels.GetValueOrDefault(parentChannelId);
            channel = new ChannelInfo
            {
                Id = thread.Id,
                GuildId = parent?.GuildId ?? 0,
                Name = thread.Name,
                Kind = ChannelKind.Thread,
                ParentId = parentChannelId,
            };
            _channels[thread.Id] = channel;
        }
        Raise(new ClientChange(ChangeKind.Channels, channel.GuildId, channel.Id));
        return channel;
    }

    /// <summary>Most scrollback pages a jump-to-message may fetch. Older hits stay readable in the search results.</summary>
    public const int MaxJumpPages = 5;

    /// <summary>
    /// Makes sure <paramref name="messageId"/> is in the channel's cache by paging back through history, at most
    /// <see cref="MaxJumpPages"/> pages (polite: a jump never turns into a crawl). Stops early once history is older than
    /// the message (it was deleted).
    /// </summary>
    /// <returns>True if the message is now loaded.</returns>
    public async Task<bool> LocateMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await EnsureHistoryAsync(channelId, cancellationToken).ConfigureAwait(false);
        for (int page = 0; ; page++)
        {
            ulong? oldest;
            lock (_lock)
            {
                var cache = GetCache(channelId);
                if (cache.Get(messageId) is not null) return true;
                if (!cache.HasMoreBefore || page >= MaxJumpPages) return false;
                oldest = cache.OldestId;
            }
            if (oldest is ulong o && o < messageId) return false;
            if (await LoadOlderAsync(channelId, cancellationToken).ConfigureAwait(false) == 0) return GetMessage(channelId, messageId) is not null;
        }
    }

    /// <summary>Loads the newest page for a channel the first time it's opened.</summary>
    public async Task EnsureHistoryAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        if (IsHistoryLoaded(channelId)) return;
        await LoadOlderAsync(channelId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetches the next page of scrollback (REST pagination with <c>before</c>).</summary>
    /// <returns>How many messages were added.</returns>
    public async Task<int> LoadOlderAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        var rest = _connection?.Rest ?? throw new InvalidOperationException("Not connected.");
        ulong? before;
        lock (_lock)
        {
            var cache = GetCache(channelId);
            if ((cache.IsInitialized && !cache.HasMoreBefore) || !_historyLoading.Add(channelId)) return 0;
            // The first page starts from the newest message; live messages that arrived meanwhile dedupe on merge.
            before = cache.IsInitialized ? cache.OldestId : null;
        }
        Raise(new ClientChange(ChangeKind.Messages, null, channelId));
        try
        {
            var page = await rest.GetMessagesBeforeAsync(channelId, before, HistoryPageSize, cancellationToken).ConfigureAwait(false);
            var guildId = GetChannel(channelId)?.GuildId;
            lock (_lock) GetCache(channelId).AddHistoryPage(page.Select(m => WithGuild(m, guildId)).ToList(), HistoryPageSize);
            return page.Count;
        }
        finally
        {
            lock (_lock) _historyLoading.Remove(channelId);
            Raise(new ClientChange(ChangeKind.Messages, null, channelId));
        }
    }

    // ---------------------------------------------------------------- actions

    MessageSender Sender => _sender ?? throw new InvalidOperationException("Not connected.");
    IDiscordRestApi Rest => _connection?.Rest ?? throw new InvalidOperationException("Not connected.");

    public async Task SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        message = message with { Content = ExpandEmoji(message.ChannelId, message.Content) };
        var sent = await Sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        lock (_lock) _typingSentAt.Remove(message.ChannelId);
        if (sent is not null) Ingest(sent, isEdit: false);
    }

    public async Task EditAsync(ChatMessage message, string newContent, CancellationToken cancellationToken = default)
    {
        var edited = await Sender.EditAsync(message, ExpandEmoji(message.ChannelId, newContent), cancellationToken).ConfigureAwait(false);
        if (edited is not null) Ingest(edited, isEdit: true);
    }

    public async Task DeleteAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        await Sender.DeleteAsync(message, cancellationToken).ConfigureAwait(false);
        lock (_lock) _messages.GetValueOrDefault(message.ChannelId)?.Remove([message.Id]);
        Raise(new ClientChange(ChangeKind.Messages, message.GuildId, message.ChannelId));
    }

    string ExpandEmoji(ulong channelId, string content) =>
        GetChannel(channelId)?.GuildId is ulong guildId ? EmojiShortcodes.Expand(content, GetEmojis(guildId)) : content;

    /// <summary>Adds our reaction, or removes it if we already reacted with that emoji.</summary>
    public async Task ToggleReactionAsync(ChatMessage message, EmojiRef emoji, CancellationToken cancellationToken = default)
    {
        var current = GetMessage(message.ChannelId, message.Id) ?? message;
        bool mine = current.Reactions.Any(r => r.Emoji.Matches(emoji) && r.Me);
        if (mine) await Rest.RemoveOwnReactionAsync(message.ChannelId, message.Id, emoji, cancellationToken).ConfigureAwait(false);
        else await Rest.AddReactionAsync(message.ChannelId, message.Id, emoji, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Call on keystrokes in the composer; sends the typing indicator at most every 8 seconds (not when posting through a webhook).</summary>
    public async Task NotifyTypingAsync(ulong channelId)
    {
        var s = _settings.Current;
        // DMs always send as the account (no webhooks there), so they show typing whatever the send mode.
        bool direct = s.SendMode == SendMode.Bot || GetChannel(channelId)?.IsPrivate == true;
        if (!s.SendTypingIndicator || !direct || _connection is null) return;
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_typingSentAt.TryGetValue(channelId, out var last) && now - last < TypingResendInterval) return;
            _typingSentAt[channelId] = now;
        }
        try { await Rest.TriggerTypingAsync(channelId).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug("Typing indicator failed: {Error}", SecretRedactor.Redact(ex.Message)); }
    }

    // ---------------------------------------------------------------- gateway events

    internal ValueTask OnEventAsync(GatewayEvent e)
    {
        try
        {
            Apply(e);
        }
        catch (Exception ex)
        {
            _log.LogError("Failed to apply {Event}: {Error}", e.GetType().Name, SecretRedactor.Redact(ex.Message));
        }
        return ValueTask.CompletedTask;
    }

    void Apply(GatewayEvent e)
    {
        switch (e)
        {
            case ConnectionStateChangedEvent s:
                SetState(s.State, s.Detail);
                break;

            case ReadyEvent r:
                // A second READY means the session couldn't be resumed: events in the gap are lost, so cached
                // history may be missing messages. Drop it; open channels reload their newest page.
                bool reidentified = CurrentUser is not null;
                CurrentUser = r.CurrentUser;
                if (reidentified)
                {
                    lock (_lock)
                    {
                        _messages.Clear();
                        _historyLoading.Clear();
                    }
                    Interlocked.Increment(ref _historyGeneration);
                    Raise(new ClientChange(ChangeKind.Messages, null, ActiveChannelId));
                }
                SetState(ConnectionState.Connected, null);
                break;

            case GuildAvailableEvent g:
                lock (_lock)
                {
                    var state = new GuildState(g.Snapshot.Guild);
                    foreach (var role in g.Snapshot.Roles) state.Roles[role.Id] = role;
                    foreach (var m in g.Snapshot.Members) state.Members[m.UserId] = m;
                    foreach (var emoji in g.Snapshot.Emojis) state.Emojis[emoji.Id] = emoji;
                    _guilds[g.Snapshot.Guild.Id] = state;
                    foreach (var stale in _channels.Values.Where(c => c.GuildId == g.Snapshot.Guild.Id).Select(c => c.Id).ToList()) _channels.Remove(stale);
                    foreach (var c in g.Snapshot.Channels)
                    {
                        _channels[c.Id] = c;
                        if (c.IsMessageable) _unread.TrackChannel(c.Id, c.LastMessageId);
                    }
                }
                Raise(new ClientChange(ChangeKind.Guilds, g.Snapshot.Guild.Id));
                break;

            case GuildUpdatedEvent g:
                lock (_lock) if (_guilds.TryGetValue(g.Guild.Id, out var gs)) gs.Info = g.Guild;
                Raise(new ClientChange(ChangeKind.Guilds, g.Guild.Id));
                break;

            case GuildRemovedEvent g:
                lock (_lock)
                {
                    _guilds.Remove(g.GuildId);
                    foreach (var id in _channels.Values.Where(c => c.GuildId == g.GuildId).Select(c => c.Id).ToList()) _channels.Remove(id);
                }
                Raise(new ClientChange(ChangeKind.Guilds, g.GuildId));
                break;

            case ChannelUpsertEvent c:
                lock (_lock)
                {
                    _channels[c.Channel.Id] = c.Channel;
                    if (c.Channel.IsMessageable) _unread.TrackChannel(c.Channel.Id, c.Channel.LastMessageId);
                }
                Raise(new ClientChange(ChangeKind.Channels, c.Channel.GuildId, c.Channel.Id));
                break;

            case ChannelDeleteEvent c:
                lock (_lock)
                {
                    _channels.Remove(c.ChannelId);
                    _messages.Remove(c.ChannelId);
                }
                Raise(new ClientChange(ChangeKind.Channels, c.GuildId, c.ChannelId));
                break;

            case RoleUpsertEvent r:
                lock (_lock) if (_guilds.TryGetValue(r.Role.GuildId, out var gs)) gs.Roles[r.Role.Id] = r.Role;
                Raise(new ClientChange(ChangeKind.Roles, r.Role.GuildId));
                break;

            case RoleDeleteEvent r:
                lock (_lock) _guilds.GetValueOrDefault(r.GuildId)?.Roles.Remove(r.RoleId);
                Raise(new ClientChange(ChangeKind.Roles, r.GuildId));
                break;

            case MembersUpsertEvent m:
                lock (_lock)
                    if (_guilds.TryGetValue(m.GuildId, out var gs))
                        foreach (var member in m.Members) gs.Members[member.UserId] = member;
                Raise(new ClientChange(ChangeKind.Members, m.GuildId));
                break;

            case MemberRemoveEvent m:
                lock (_lock) _guilds.GetValueOrDefault(m.GuildId)?.Members.Remove(m.UserId);
                Raise(new ClientChange(ChangeKind.Members, m.GuildId));
                break;

            case EmojisUpdateEvent em:
                lock (_lock)
                    if (_guilds.TryGetValue(em.GuildId, out var gs))
                    {
                        gs.Emojis.Clear();
                        foreach (var emoji in em.Emojis) gs.Emojis[emoji.Id] = emoji;
                    }
                Raise(new ClientChange(ChangeKind.Emojis, em.GuildId));
                break;

            case MessageCreateEvent m:
                Ingest(m.Message, isEdit: false);
                break;

            case MessageUpdateEvent m:
                Ingest(m.Message, isEdit: true);
                break;

            case MessagePatchEvent p:
                bool patched;
                lock (_lock) patched = _messages.GetValueOrDefault(p.ChannelId)?.Patch(p.MessageId, p.Embeds, p.Content, p.EditedAt) ?? false;
                if (patched) Raise(new ClientChange(ChangeKind.Messages, GetChannel(p.ChannelId)?.GuildId, p.ChannelId));
                break;

            case ReadStateEvent r:
                _unread.SeedLastRead(r.LastReadMessageIds);
                Raise(new ClientChange(ChangeKind.Unread));
                break;

            case MessageDeleteEvent d:
                bool removed;
                lock (_lock) removed = _messages.GetValueOrDefault(d.ChannelId)?.Remove(d.MessageIds) ?? false;
                if (removed) Raise(new ClientChange(ChangeKind.Messages, GetChannel(d.ChannelId)?.GuildId, d.ChannelId));
                break;

            case ReactionAddEvent r:
                ApplyReaction(r.ChannelId, r.MessageId, r.Emoji, +1, r.UserId);
                break;

            case ReactionRemoveEvent r:
                ApplyReaction(r.ChannelId, r.MessageId, r.Emoji, -1, r.UserId);
                break;

            case ReactionClearEvent r:
                bool cleared;
                lock (_lock) cleared = _messages.GetValueOrDefault(r.ChannelId)?.ClearReactions(r.MessageId, r.Emoji) ?? false;
                if (cleared) Raise(new ClientChange(ChangeKind.Messages, GetChannel(r.ChannelId)?.GuildId, r.ChannelId));
                break;

            case TypingStartEvent t:
                if (t.UserId == CurrentUser?.Id) break;
                var guild = GetChannel(t.ChannelId)?.GuildId;
                var typist = t.Member ?? (guild is ulong gid ? GetMember(gid, t.UserId) : null);
                _typing.Start(t.ChannelId, t.UserId, typist?.DisplayName ?? "Someone");
                Raise(new ClientChange(ChangeKind.Typing, guild, t.ChannelId));
                break;
        }
    }

    void Ingest(ChatMessage message, bool isEdit)
    {
        var channel = GetChannel(message.ChannelId);
        message = WithGuild(message, channel?.GuildId);
        bool changed, reordered = false;
        lock (_lock)
        {
            // Keep LastMessageId current: DMs are listed by most recent activity.
            if (!isEdit && channel is not null && message.Id > (channel.LastMessageId ?? 0))
            {
                _channels[channel.Id] = channel with { LastMessageId = message.Id };
                reordered = channel.IsPrivate;
            }
            var cache = GetCache(message.ChannelId);
            if (isEdit) changed = cache.Update(message);
            else
            {
                cache.Upsert(message);
                changed = true;
            }
        }

        if (!isEdit)
        {
            if (_typing.Stop(message.ChannelId, message.Author.Id)) Raise(new ClientChange(ChangeKind.Typing, message.GuildId, message.ChannelId));
            // Our own messages and messages in the open channel count as read.
            bool own = message.Author.Id == CurrentUser?.Id || _webhooks?.IsOwnWebhook(message.WebhookId) == true;
            bool viewing = ActiveChannelId == message.ChannelId;
            bool becameUnread = _unread.OnMessage(message.ChannelId, message.Id, isActiveChannel: own || viewing);
            if (becameUnread || viewing || own)
            {
                QueueUnreadSave();
                Raise(new ClientChange(ChangeKind.Unread, message.GuildId, message.ChannelId));
            }
        }
        if (reordered) Raise(new ClientChange(ChangeKind.Channels, message.GuildId, message.ChannelId));
        if (changed) Raise(new ClientChange(ChangeKind.Messages, message.GuildId, message.ChannelId));
    }

    void ApplyReaction(ulong channelId, ulong messageId, EmojiRef emoji, int delta, ulong userId)
    {
        bool changed;
        lock (_lock) changed = _messages.GetValueOrDefault(channelId)?.ApplyReaction(messageId, emoji, delta, byMe: userId == CurrentUser?.Id) ?? false;
        if (changed) Raise(new ClientChange(ChangeKind.Messages, GetChannel(channelId)?.GuildId, channelId));
    }

    static ChatMessage WithGuild(ChatMessage m, ulong? guildId) =>
        m.GuildId is null && guildId is not null
            ? m with { GuildId = guildId, ReplyTo = m.ReplyTo is { GuildId: null } r ? r with { GuildId = guildId } : m.ReplyTo }
            : m;

    MessageCache GetCache(ulong channelId)
    {
        if (!_messages.TryGetValue(channelId, out var cache)) _messages[channelId] = cache = new MessageCache();
        return cache;
    }

    void QueueUnreadSave()
    {
        if (Interlocked.Exchange(ref _unreadSaveQueued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _time).ConfigureAwait(false);
            Interlocked.Exchange(ref _unreadSaveQueued, 0);
            try { await _store.SetAsync(UnreadStorageKey, _unread.Serialize()).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogDebug("Saving read state failed: {Error}", ex.Message); }
        });
    }

    void Raise(ClientChange change)
    {
        try { Changed?.Invoke(change); }
        catch (Exception ex) { _log.LogError("Change handler threw: {Error}", ex.Message); }
    }
}
