using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;

namespace DisctalkClient.Tests.Sending;

public class MessageSenderTests
{
    const ulong HookId = 900000000000000700;
    static readonly WebhookInfo OwnHook = new(HookId, TestData.GeneralId, WebhookDirectory.WebhookName, "hook-secret-token", TestData.BotId);

    readonly IBotRestApi _rest = Substitute.For<IBotRestApi>();
    readonly IWebhookExecutor _executor = Substitute.For<IWebhookExecutor>();
    readonly SettingsService _settings = new(new InMemorySettingsStore());
    readonly WebhookDirectory _webhooks;
    readonly MessageSender _sender;

    public MessageSenderTests()
    {
        _webhooks = new WebhookDirectory(_rest, () => TestData.BotId);
        _sender = new MessageSender(_rest, _webhooks, _executor, _settings, () => TestData.BotId);
        _rest.SendMessageAsync(Arg.Any<BotMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(c => TestData.Message(c.Arg<BotMessageRequest>().Content, TestData.Bot));
        _executor.ExecuteAsync(default!, default!, default!, default).ReturnsForAnyArgs(1UL);
    }

    Task UseWebhook(string name = "Andre", string avatar = "https://example.com/andre.png") =>
        _settings.UpdateAsync(s => s with { SendMode = SendMode.Webhook, WebhookDisplayName = name, WebhookAvatarUrl = avatar });

    void ChannelHasHooks(params WebhookInfo[] hooks) =>
        _rest.GetChannelWebhooksAsync(TestData.GeneralId, Arg.Any<CancellationToken>()).Returns(hooks);

    // ------------------------------------------------------------------ bot mode

    [Fact]
    public async Task Bot_mode_sends_through_the_bot_and_never_touches_webhooks()
    {
        var sent = await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "hi  " });

        Assert.NotNull(sent);
        await _rest.Received(1).SendMessageAsync(Arg.Is<BotMessageRequest>(r => r.ChannelId == TestData.GeneralId && r.Content == "hi"), Arg.Any<CancellationToken>());
        await _rest.DidNotReceiveWithAnyArgs().GetChannelWebhooksAsync(default);
        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Bot_reply_uses_a_real_message_reference(bool ping)
    {
        await _settings.UpdateAsync(s => s with { PingOnReply = ping });
        var parent = TestData.Message("original", TestData.Ada);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "reply", ReplyTo = parent });

        await _rest.Received(1).SendMessageAsync(Arg.Is<BotMessageRequest>(r =>
            r.ReplyToMessageId == parent.Id && r.Content == "reply" && r.AllowedMentions.RepliedUser == ping), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Per_message_mode_overrides_the_setting()
    {
        await UseWebhook();
        ChannelHasHooks(OwnHook);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "as bot", Mode = SendMode.Bot });

        await _rest.ReceivedWithAnyArgs(1).SendMessageAsync(default!);
        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public async Task Empty_messages_are_rejected(string content) =>
        await Assert.ThrowsAsync<SendException>(() => _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = content }));

    [Fact]
    public async Task Attachment_only_message_is_allowed()
    {
        var file = new OutgoingFile("cat.png", new byte[] { 1, 2, 3 }, "image/png");
        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Files = [file] });
        await _rest.Received(1).SendMessageAsync(Arg.Is<BotMessageRequest>(r => r.Files.Count == 1 && r.Files[0].FileName == "cat.png"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Over_2000_characters_is_rejected_before_calling_discord()
    {
        var ex = await Assert.ThrowsAsync<SendException>(() =>
            _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = new string('a', 2001) }));
        Assert.Contains("2001", ex.Message);
        await _rest.DidNotReceiveWithAnyArgs().SendMessageAsync(default!);
    }

    // ------------------------------------------------------------------ webhook mode

    [Fact]
    public async Task Webhook_mode_posts_with_configured_name_and_avatar()
    {
        await UseWebhook("  Andre  ", "https://example.com/andre.png");
        ChannelHasHooks(OwnHook);

        var result = await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "hello cats" });

        Assert.Null(result); // the gateway delivers webhook messages
        await _executor.Received(1).ExecuteAsync(OwnHook,
            Arg.Is<WebhookPayload>(p => p.Content == "hello cats" && p.Username == "Andre" && p.AvatarUrl == "https://example.com/andre.png"),
            Arg.Is<IReadOnlyList<OutgoingFile>>(f => f.Count == 0), Arg.Any<CancellationToken>());
        await _rest.DidNotReceiveWithAnyArgs().SendMessageAsync(default!);
    }

    [Fact]
    public async Task Blank_persona_falls_back_to_the_webhook_defaults()
    {
        await _settings.UpdateAsync(s => s with { SendMode = SendMode.Webhook });
        ChannelHasHooks(OwnHook);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "x" });

        await _executor.Received(1).ExecuteAsync(OwnHook, Arg.Is<WebhookPayload>(p => p.Username == null && p.AvatarUrl == null),
            Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_is_created_once_when_the_channel_has_none()
    {
        await UseWebhook();
        ChannelHasHooks(new WebhookInfo(1, TestData.GeneralId, "Someone else's", Token: null, ApplicationId: 42));
        _rest.CreateWebhookAsync(TestData.GeneralId, WebhookDirectory.WebhookName, Arg.Any<CancellationToken>()).Returns(OwnHook);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "one" });
        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "two" });

        await _rest.Received(1).CreateWebhookAsync(TestData.GeneralId, WebhookDirectory.WebhookName, Arg.Any<CancellationToken>());
        await _rest.Received(1).GetChannelWebhooksAsync(TestData.GeneralId, Arg.Any<CancellationToken>());
        await _executor.ReceivedWithAnyArgs(2).ExecuteAsync(default!, default!, default!);
        Assert.True(_webhooks.IsOwnWebhook(HookId));
    }

    [Fact]
    public async Task Existing_webhook_from_this_app_is_reused()
    {
        await UseWebhook();
        var foreign = new WebhookInfo(2, TestData.GeneralId, "Other bot", "other-token", 777);
        var ours = OwnHook with { Name = "renamed by an admin" };
        ChannelHasHooks(foreign, ours);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "x" });

        await _executor.Received(1).ExecuteAsync(ours, Arg.Any<WebhookPayload>(), Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
        await _rest.DidNotReceiveWithAnyArgs().CreateWebhookAsync(default, default!);
    }

    [Fact]
    public async Task Missing_manage_webhooks_permission_gives_a_helpful_error()
    {
        await UseWebhook();
        _rest.GetChannelWebhooksAsync(TestData.GeneralId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<WebhookInfo>>(_ => throw new InvalidOperationException("403 Missing Permissions"));

        var ex = await Assert.ThrowsAsync<SendException>(() => _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "x" }));
        Assert.Contains("Manage Webhooks", ex.Message);
    }

    [Fact]
    public async Task Webhook_reply_is_emulated_with_a_jump_link_and_pings_the_author()
    {
        await UseWebhook();
        ChannelHasHooks(OwnHook);
        var parent = TestData.Message("original", TestData.Ada);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "agreed", ReplyTo = parent });

        await _executor.Received(1).ExecuteAsync(OwnHook, Arg.Is<WebhookPayload>(p =>
                p.Content == $"-# ↪ Replying to <@{TestData.AdaId}> · [jump](https://discord.com/channels/{TestData.GuildId}/{TestData.GeneralId}/{parent.Id})\nagreed"
                && p.AllowedMentions.Users.SequenceEqual(new[] { TestData.AdaId })),
            Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Webhook_reply_without_ping_keeps_the_mention_text_but_not_the_ping()
    {
        await UseWebhook();
        await _settings.UpdateAsync(s => s with { PingOnReply = false });
        ChannelHasHooks(OwnHook);
        var parent = TestData.Message("original", TestData.Ada);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = $"cc <@{TestData.BobId}>", ReplyTo = parent });

        await _executor.Received(1).ExecuteAsync(OwnHook, Arg.Is<WebhookPayload>(p =>
                p.Content.Contains($"<@{TestData.AdaId}>") && p.AllowedMentions.Users.SequenceEqual(new[] { TestData.BobId })),
            Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replying_to_a_webhook_message_names_it_instead_of_mentioning()
    {
        var parent = TestData.Message("from a webhook", TestData.Webhook(HookId, "Some_Persona"));
        var text = WebhookReplyFormatter.Format("ok", parent);
        Assert.StartsWith(@"-# ↪ Replying to **Some\_Persona** ·", text);
        Assert.DoesNotContain("<@", text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Reply_header_counts_toward_the_length_limit()
    {
        await UseWebhook();
        ChannelHasHooks(OwnHook);
        var parent = TestData.Message("original", TestData.Ada);

        var ex = await Assert.ThrowsAsync<SendException>(() =>
            _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = new string('a', 1990), ReplyTo = parent }));
        Assert.Contains("reply header", ex.Message);
        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Everyone_pings_only_when_allowed()
    {
        await UseWebhook();
        ChannelHasHooks(OwnHook);

        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "@everyone dinner" });
        await _settings.UpdateAsync(s => s with { AllowEveryoneMentions = true });
        await _sender.SendAsync(new OutgoingMessage { ChannelId = TestData.GeneralId, Content = "@everyone dinner" });

        Received.InOrder(() =>
        {
            _executor.ExecuteAsync(OwnHook, Arg.Is<WebhookPayload>(p => !p.AllowedMentions.Everyone), Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
            _executor.ExecuteAsync(OwnHook, Arg.Is<WebhookPayload>(p => p.AllowedMentions.Everyone), Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
        });
    }

    // ------------------------------------------------------------------ edit / delete routing

    [Fact]
    public async Task Edit_own_webhook_message_goes_through_the_webhook()
    {
        ChannelHasHooks(OwnHook);
        var mine = TestData.Message("old", TestData.Webhook(HookId)) with { WebhookId = HookId };

        await _sender.EditAsync(mine, "new");

        await _executor.Received(1).EditAsync(OwnHook, mine.Id, "new", Arg.Any<AllowedMentions>(), Arg.Any<CancellationToken>());
        await _rest.DidNotReceiveWithAnyArgs().EditMessageAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task Edit_bot_message_goes_through_the_bot()
    {
        var mine = TestData.Message("old", TestData.Bot);
        _rest.EditMessageAsync(default, default, default!, default!).ReturnsForAnyArgs(mine with { Content = "new" });

        var edited = await _sender.EditAsync(mine, "new");

        Assert.Equal("new", edited!.Content);
        await _rest.Received(1).EditMessageAsync(TestData.GeneralId, mine.Id, "new", Arg.Any<AllowedMentions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Other_peoples_messages_cannot_be_edited()
    {
        var theirs = TestData.Message("not yours", TestData.Ada);
        Assert.False(_sender.CanEdit(theirs));
        await Assert.ThrowsAsync<SendException>(() => _sender.EditAsync(theirs, "hacked"));
        await _rest.DidNotReceiveWithAnyArgs().EditMessageAsync(default, default, default!, default!);
    }

    [Fact]
    public async Task Someone_elses_webhook_message_cannot_be_edited()
    {
        ChannelHasHooks(OwnHook);
        var other = TestData.Message("x", TestData.Webhook(55)) with { WebhookId = 55 };
        await _webhooks.FindAsync(TestData.GeneralId);

        Assert.False(_sender.CanEdit(other));
        await Assert.ThrowsAsync<SendException>(() => _sender.EditAsync(other, "y"));
    }

    [Fact]
    public async Task CanEdit_knows_own_webhook_once_resolved()
    {
        ChannelHasHooks(OwnHook);
        var mine = TestData.Message("x", TestData.Webhook(HookId)) with { WebhookId = HookId };
        Assert.False(_sender.CanEdit(mine));
        await _webhooks.FindAsync(TestData.GeneralId);
        Assert.True(_sender.CanEdit(mine));
        Assert.True(_sender.CanEdit(TestData.Message("x", TestData.Bot)));
    }

    [Fact]
    public async Task Delete_routes_by_owner()
    {
        ChannelHasHooks(OwnHook);
        var viaHook = TestData.Message("a", TestData.Webhook(HookId)) with { WebhookId = HookId };
        var viaBot = TestData.Message("b", TestData.Bot);
        var someoneElse = TestData.Message("c", TestData.Ada);

        await _sender.DeleteAsync(viaHook);
        await _sender.DeleteAsync(viaBot);
        await _sender.DeleteAsync(someoneElse);

        await _executor.Received(1).DeleteAsync(OwnHook, viaHook.Id, Arg.Any<CancellationToken>());
        await _rest.Received(1).DeleteMessageAsync(TestData.GeneralId, viaBot.Id, Arg.Any<CancellationToken>());
        await _rest.Received(1).DeleteMessageAsync(TestData.GeneralId, someoneElse.Id, Arg.Any<CancellationToken>());
        await _rest.DidNotReceive().DeleteMessageAsync(TestData.GeneralId, viaHook.Id, Arg.Any<CancellationToken>());
    }
}
