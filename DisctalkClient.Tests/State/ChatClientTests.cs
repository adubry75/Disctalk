using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;
using DisctalkClient.Core.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace DisctalkClient.Tests.State;

public class ChatClientTests
{
    readonly FakeConnectionFactory _factory = new();
    readonly InMemorySettingsStore _store = new();
    readonly SettingsService _settings;
    readonly IWebhookExecutor _executor = Substitute.For<IWebhookExecutor>();
    readonly FakeTimeProvider _time = new(TestData.Now);
    readonly CapturingLogger _log = new();
    readonly ChatClient _client;
    readonly List<ClientChange> _changes = new();

    FakeConnection Conn => _factory.Connection;

    public ChatClientTests()
    {
        _settings = new SettingsService(_store);
        _client = new ChatClient(_factory, _executor, _settings, _store, _time, _log);
        _client.Changed += c => { lock (_changes) _changes.Add(c); };
    }

    async Task LoginAsync()
    {
        await _client.ConnectAsync(AccountKind.Bot, TestData.Token);
        await Conn.LoginAsync();
    }

    [Fact]
    public async Task Connect_validates_and_normalizes_the_token()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ConnectAsync(AccountKind.Bot, "not a token"));
        Assert.Null(_factory.LastToken);

        await _client.ConnectAsync(AccountKind.Bot, "Bot " + TestData.Token);
        Assert.Equal(TestData.Token, _factory.LastToken);
        Assert.True(Conn.Started);
        Assert.Equal(ConnectionState.Connecting, _client.State);
    }

    [Fact]
    public async Task Ready_and_guild_create_populate_state()
    {
        await LoginAsync();

        Assert.Equal(ConnectionState.Connected, _client.State);
        Assert.Equal("Whiskers", _client.CurrentUser!.DisplayName);
        Assert.Equal("Test Café", Assert.Single(_client.Guilds).Name);
        Assert.Equal(3, _client.GetChannels(TestData.GuildId).Count);
        Assert.Equal("Ada Lovelace", _client.GetMember(TestData.GuildId, TestData.AdaId)!.DisplayName);
        Assert.Equal("#6cb4ee", _client.GetNameColor(TestData.GuildId, TestData.AdaId));
        Assert.Equal("blobcat", Assert.Single(_client.GetEmojis(TestData.GuildId)).Name);
    }

    [Fact]
    public async Task Scrollback_pages_backwards_with_before_ids()
    {
        await LoginAsync();
        var newest = Enumerable.Range(0, ChatClient.HistoryPageSize).Select(i => TestData.Message($"n{i}", at: TestData.Now.AddMinutes(-i - 1))).ToList();
        var older = new List<ChatMessage> { TestData.Message("oldest", at: TestData.Now.AddDays(-1)) };
        Conn.Rest.GetMessagesBeforeAsync(TestData.GeneralId, null, ChatClient.HistoryPageSize, Arg.Any<CancellationToken>()).Returns(newest);
        Conn.Rest.GetMessagesBeforeAsync(TestData.GeneralId, newest[^1].Id, ChatClient.HistoryPageSize, Arg.Any<CancellationToken>()).Returns(older);

        await _client.EnsureHistoryAsync(TestData.GeneralId);
        await _client.EnsureHistoryAsync(TestData.GeneralId); // no second initial fetch
        Assert.True(_client.HasMoreHistory(TestData.GeneralId));

        Assert.Equal(1, await _client.LoadOlderAsync(TestData.GeneralId));
        Assert.False(_client.HasMoreHistory(TestData.GeneralId));
        Assert.Equal(0, await _client.LoadOlderAsync(TestData.GeneralId)); // reached the start: no more calls

        var messages = _client.GetMessages(TestData.GeneralId);
        Assert.Equal(51, messages.Count);
        Assert.Equal("oldest", messages[0].Content);
        Assert.All(messages, m => Assert.Equal(TestData.GuildId, m.GuildId));
        await Conn.Rest.ReceivedWithAnyArgs(2).GetMessagesBeforeAsync(default, default, default);
    }

    [Fact]
    public async Task Live_messages_edits_deletes_and_reactions_flow_into_the_cache()
    {
        await LoginAsync();
        var m = TestData.Message("hi", TestData.Ada) with { GuildId = null };

        await Conn.RaiseAsync(new MessageCreateEvent(m));
        Assert.Equal(TestData.GuildId, _client.GetMessage(TestData.GeneralId, m.Id)!.GuildId);

        await Conn.RaiseAsync(new MessageUpdateEvent(m with { Content = "hi (edited)", EditedAt = TestData.Now }));
        Assert.Equal("hi (edited)", _client.GetMessage(TestData.GeneralId, m.Id)!.Content);

        var paw = new EmojiRef(null, "🐾");
        await Conn.RaiseAsync(new ReactionAddEvent(TestData.GeneralId, m.Id, TestData.BotId, paw), new ReactionAddEvent(TestData.GeneralId, m.Id, TestData.BobId, paw));
        var reaction = Assert.Single(_client.GetMessage(TestData.GeneralId, m.Id)!.Reactions);
        Assert.Equal((2, true), (reaction.Count, reaction.Me));

        await Conn.RaiseAsync(new ReactionRemoveEvent(TestData.GeneralId, m.Id, TestData.BotId, paw));
        Assert.False(_client.GetMessage(TestData.GeneralId, m.Id)!.Reactions[0].Me);

        await Conn.RaiseAsync(new MessageDeleteEvent(TestData.GeneralId, [m.Id]));
        Assert.Empty(_client.GetMessages(TestData.GeneralId));
        Assert.Contains(_changes, c => c.Kind == ChangeKind.Messages && c.ChannelId == TestData.GeneralId);
    }

    [Fact]
    public async Task Unread_dots_follow_the_active_channel()
    {
        await LoginAsync();
        _client.SetActiveChannel(TestData.GeneralId);

        await Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("in general", channelId: TestData.GeneralId)));
        await Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("in random", channelId: TestData.RandomId)));

        Assert.False(_client.IsUnread(TestData.GeneralId));
        Assert.True(_client.IsUnread(TestData.RandomId));

        _client.SetActiveChannel(TestData.RandomId);
        Assert.False(_client.IsUnread(TestData.RandomId));
    }

    [Fact]
    public async Task Own_messages_never_mark_a_channel_unread()
    {
        await LoginAsync();
        _client.SetActiveChannel(TestData.GeneralId);
        await Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("me", TestData.Bot, channelId: TestData.RandomId)));
        Assert.False(_client.IsUnread(TestData.RandomId));
    }

    [Fact]
    public async Task Typing_shows_member_names_and_clears_when_they_send()
    {
        await LoginAsync();
        await Conn.RaiseAsync(new TypingStartEvent(TestData.GeneralId, TestData.AdaId, null, TestData.Now));
        await Conn.RaiseAsync(new TypingStartEvent(TestData.GeneralId, TestData.BotId, null, TestData.Now)); // ourselves: ignored
        Assert.Equal(["Ada Lovelace"], _client.GetTypingNames(TestData.GeneralId));

        await Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("done", TestData.Ada)));
        Assert.Empty(_client.GetTypingNames(TestData.GeneralId));
    }

    [Fact]
    public async Task Outgoing_typing_is_throttled_and_off_in_webhook_mode()
    {
        await LoginAsync();
        await _client.NotifyTypingAsync(TestData.GeneralId);
        await _client.NotifyTypingAsync(TestData.GeneralId);
        _time.Advance(TimeSpan.FromSeconds(9));
        await _client.NotifyTypingAsync(TestData.GeneralId);
        await Conn.Rest.Received(2).TriggerTypingAsync(TestData.GeneralId, Arg.Any<CancellationToken>());

        await _settings.UpdateAsync(s => s with { SendMode = SendMode.Webhook });
        _time.Advance(TimeSpan.FromSeconds(9));
        await _client.NotifyTypingAsync(TestData.GeneralId);
        await Conn.Rest.Received(2).TriggerTypingAsync(TestData.GeneralId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_expands_server_emoji_and_shows_the_message_immediately()
    {
        await LoginAsync();
        Conn.Rest.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => TestData.Message(c.Arg<SendMessageRequest>().Content, TestData.Bot));

        await _client.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "hi :blobcat:" });

        await Conn.Rest.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r => r.Content == "hi <:blobcat:900000000000000500>"), Arg.Any<CancellationToken>());
        Assert.Equal("hi <:blobcat:900000000000000500>", Assert.Single(_client.GetMessages(TestData.GeneralId)).Content);
    }

    [Fact]
    public async Task Toggle_reaction_adds_then_removes_own_reaction()
    {
        await LoginAsync();
        var m = TestData.Message();
        await Conn.RaiseAsync(new MessageCreateEvent(m));
        var paw = new EmojiRef(null, "🐾");

        await _client.ToggleReactionAsync(m, paw);
        await Conn.RaiseAsync(new ReactionAddEvent(TestData.GeneralId, m.Id, TestData.BotId, paw));
        await _client.ToggleReactionAsync(m, paw);

        await Conn.Rest.Received(1).AddReactionAsync(TestData.GeneralId, m.Id, paw, Arg.Any<CancellationToken>());
        await Conn.Rest.Received(1).RemoveOwnReactionAsync(TestData.GeneralId, m.Id, paw, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gateway_failures_are_reported_without_the_token()
    {
        Conn.StartFailure = new InvalidOperationException($"boom while using {TestData.Token}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _client.ConnectAsync(AccountKind.Bot, TestData.Token));

        Assert.Equal(ConnectionState.Failed, _client.State);
        Assert.DoesNotContain(TestData.Token, _client.StatusDetail);
        Assert.NotEmpty(_log.Lines);
        Assert.All(_log.Lines, line => Assert.DoesNotContain(TestData.Token, line));
    }

    [Fact]
    public async Task Disconnect_clears_state()
    {
        await LoginAsync();
        await _client.DisconnectAsync();
        Assert.Empty(_client.Guilds);
        Assert.Null(_client.CurrentUser);
        Assert.Equal(ConnectionState.Disconnected, _client.State);
    }

    [Fact]
    public async Task Connect_passes_the_account_kind_to_the_factory()
    {
        await _client.ConnectAsync(AccountKind.User, TestData.Token);
        Assert.Equal((AccountKind.User, TestData.Token), (_factory.LastKind!.Value, _factory.LastToken!));
        Assert.Equal(AccountKind.User, _client.Account);
    }

    [Fact]
    public async Task Discord_read_state_marks_channels_unread_until_opened()
    {
        await _client.ConnectAsync(AccountKind.User, TestData.Token);
        var guild = TestData.Guild();
        var channels = guild.Channels.Select(c => c.Id == TestData.GeneralId ? c with { LastMessageId = 500 } : c).ToList();
        await Conn.RaiseAsync(new ReadyEvent(TestData.Bot),
            new ReadStateEvent(new Dictionary<ulong, ulong> { [TestData.GeneralId] = 400 }),
            new GuildAvailableEvent(guild with { Channels = channels }));

        Assert.True(_client.IsUnread(TestData.GeneralId));
        Assert.False(_client.IsUnread(TestData.RandomId)); // not in Discord's read state: starts read

        _client.SetActiveChannel(TestData.GeneralId);
        Assert.False(_client.IsUnread(TestData.GeneralId));
    }

    [Fact]
    public async Task Opening_a_channel_focuses_it_on_the_connection()
    {
        await LoginAsync();
        _client.SetActiveChannel(TestData.GeneralId);
        await WaitUntil(() => { lock (Conn.Focused) return Conn.Focused.Count > 0; });
        lock (Conn.Focused) Assert.Equal((TestData.GuildId, TestData.GeneralId), Conn.Focused[0]);
    }

    [Fact]
    public async Task Partial_updates_patch_embeds_into_the_cached_message()
    {
        await LoginAsync();
        var m = TestData.Message("https://example.com");
        await Conn.RaiseAsync(new MessageCreateEvent(m));

        await Conn.RaiseAsync(new MessagePatchEvent(m.ChannelId, m.Id, [new EmbedInfo { Title = "Example" }]));

        var patched = _client.GetMessage(m.ChannelId, m.Id)!;
        Assert.Equal("Example", Assert.Single(patched.Embeds).Title);
        Assert.Equal("https://example.com", patched.Content);
    }

    [Fact]
    public async Task Direct_messages_come_first_and_sort_by_latest_activity()
    {
        await LoginAsync();
        ChannelInfo Dm(ulong id, string name, ulong last) => new() { Id = id, GuildId = GuildInfo.DirectMessagesId, Name = name, Kind = ChannelKind.DirectMessage, LastMessageId = last };
        await Conn.RaiseAsync(new GuildAvailableEvent(new GuildSnapshot(GuildInfo.DirectMessages,
            [Dm(50, "ada", 100), Dm(51, "bob", 200)], [], [], [])));

        Assert.True(_client.Guilds[0].IsDirectMessages);
        Assert.Equal(["bob", "ada"], ChannelTree.Build(_client.GetChannels(GuildInfo.DirectMessagesId)).Single().Channels.Select(c => c.Name));

        await Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("psst", TestData.Ada, channelId: 50) with { GuildId = null }));

        Assert.Equal(["ada", "bob"], ChannelTree.Build(_client.GetChannels(GuildInfo.DirectMessagesId)).Single().Channels.Select(c => c.Name));
        Assert.Equal(GuildInfo.DirectMessagesId, _client.GetMessages(50).Single().GuildId);
        Assert.True(_client.IsUnread(50));
    }

    static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    sealed class CapturingLogger : ILogger<ChatClient>
    {
        public List<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Lines) Lines.Add(formatter(state, exception) + exception);
        }
    }
}
