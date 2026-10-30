using Bunit;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;
using DisctalkClient.UI.Components;

namespace DisctalkClient.Tests.Rendering;

public class AppRenderingTests : UiTestBase
{
    [Fact]
    public void Without_a_token_the_setup_screen_asks_for_one()
    {
        var cut = Render<DisctalkApp>();

        Assert.NotNull(cut.Find(".dt-setup"));
        Assert.Null(Factory.LastToken);
    }

    [Fact]
    public async Task Entering_a_token_saves_it_and_connects()
    {
        var cut = Render<DisctalkApp>();
        cut.Find("#dt-token").Input("Bot " + TestData.Token);
        await cut.Find(".dt-setup .dt-btn").ClickAsync(new());

        Assert.Equal(TestData.Token, Factory.LastToken);
        Assert.Equal(TestData.Token, await Tokens.GetTokenAsync(AccountKind.User));
        Assert.Empty(cut.FindAll(".dt-setup"));
    }

    [Fact]
    public void Setup_rejects_junk_without_connecting()
    {
        var cut = Render<DisctalkApp>();
        cut.Find("#dt-token").Input("my client secret");
        cut.Find(".dt-setup .dt-btn").Click();

        Assert.Contains("doesn't look like a user token", cut.Find(".dt-field-error").TextContent);
        Assert.Null(Factory.LastToken);
    }

    [Fact]
    public async Task Stored_token_connects_and_opens_the_first_text_channel()
    {
        await Tokens.SaveTokenAsync(AccountKind.User, TestData.Token);
        Conn.Rest.GetMessagesBeforeAsync(default, default, default).ReturnsForAnyArgs([TestData.Message("welcome!", TestData.Ada)]);

        var cut = Render<DisctalkApp>();
        await cut.InvokeAsync(() => Conn.LoginAsync());

        cut.WaitForAssertion(() => Assert.Equal(TestData.GeneralId, Ui.ChannelId));
        cut.WaitForAssertion(() => Assert.Equal("general", cut.Find(".dt-chat-title").TextContent));
        cut.WaitForAssertion(() => Assert.Equal("welcome!", cut.Find(".dt-content").TextContent));
        Assert.Equal("Test Café", cut.Find(".dt-guild-name").TextContent);
        Assert.Equal(["general", "random"], cut.FindAll(".dt-channel-name").Select(e => e.TextContent));
        Assert.Contains("theme-dark", cut.Find(".dt-app").ClassList);
        Assert.Contains("--accent:#f2a65a", cut.Find(".dt-app").GetAttribute("style"));
    }

    [Fact]
    public async Task Unread_dot_appears_for_other_channels_and_clears_on_open()
    {
        await LoginAsync();
        Ui.SelectChannel(TestData.GuildId, TestData.GeneralId);
        Client.SetActiveChannel(TestData.GeneralId);
        Ui.SelectGuild(TestData.GuildId);
        var cut = Render<ChannelList>();
        Ui.SelectChannel(TestData.GuildId, TestData.GeneralId);

        await cut.InvokeAsync(() => Conn.RaiseAsync(new MessageCreateEvent(TestData.Message("psst", channelId: TestData.RandomId))));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".dt-unread-dot")));
        var random = cut.FindAll(".dt-channel").Single(b => b.TextContent.Contains("random"));
        Assert.Contains("unread", random.ClassList);

        random.Click();
        Assert.Equal(TestData.RandomId, Ui.ChannelId);
        Client.SetActiveChannel(TestData.RandomId);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".dt-unread-dot")));
    }

    [Fact]
    public async Task Categories_collapse_but_keep_the_selected_channel_visible()
    {
        await LoginAsync();
        Ui.SelectChannel(TestData.GuildId, TestData.GeneralId);
        var cut = Render<ChannelList>();

        cut.Find(".dt-category").Click();

        Assert.Equal(["general"], cut.FindAll(".dt-channel-name").Select(e => e.TextContent));
    }

    [Fact]
    public async Task Member_list_groups_hoisted_roles()
    {
        await LoginAsync();
        Ui.SelectGuild(TestData.GuildId);
        var cut = Render<MemberList>();

        Assert.Equal(["Moderators — 1", "Members — 2"], cut.FindAll(".dt-member-group").Select(e => e.TextContent));
        Assert.Equal("color:#6cb4ee", cut.Find(".dt-member-name").GetAttribute("style"));
    }

    [Fact]
    public async Task Typing_indicator_names_who_is_typing()
    {
        await LoginAsync();
        var cut = Render<TypingIndicator>(p => p.Add(c => c.ChannelId, TestData.GeneralId));
        await cut.InvokeAsync(() => Conn.RaiseAsync(new TypingStartEvent(TestData.GeneralId, TestData.AdaId, null, TestData.Now)));

        cut.WaitForAssertion(() => Assert.Contains("Ada Lovelace is typing…", cut.Markup));
    }

    [Fact]
    public async Task Composer_sends_through_the_webhook_with_persona_when_toggled()
    {
        await LoginAsync();
        var hook = new WebhookInfo(900000000000000700, TestData.GeneralId, WebhookDirectory.WebhookName, "t0k3n", TestData.BotId);
        Conn.Rest.GetChannelWebhooksAsync(TestData.GeneralId, Arg.Any<CancellationToken>()).Returns([hook]);
        await Settings.UpdateAsync(s => s with { WebhookDisplayName = "Andre", WebhookAvatarUrl = "https://example.com/a.png" });
        var cut = Render<Composer>(p => p.Add(c => c.ChannelId, TestData.GeneralId).Add(c => c.GuildId, TestData.GuildId));

        Assert.Equal("Bot", cut.Find(".dt-send-mode span").TextContent);
        await cut.Find(".dt-send-mode").ClickAsync(new());
        Assert.Equal(SendMode.Webhook, Settings.Current.SendMode);
        Assert.Equal("Andre", cut.Find(".dt-send-mode span").TextContent);

        cut.Find("textarea").Input("meow :blobcat:");
        await cut.InvokeAsync(() => cut.Instance.SendAsync());

        await Executor.Received(1).ExecuteAsync(hook,
            Arg.Is<WebhookPayload>(p => p.Content == "meow <:blobcat:900000000000000500>" && p.Username == "Andre" && p.AvatarUrl == "https://example.com/a.png"),
            Arg.Any<IReadOnlyList<OutgoingFile>>(), Arg.Any<CancellationToken>());
        await Conn.Rest.DidNotReceiveWithAnyArgs().SendMessageAsync(default!);
    }

    [Fact]
    public async Task Composer_reply_banner_and_bot_reply()
    {
        await LoginAsync();
        Conn.Rest.SendMessageAsync(default!).ReturnsForAnyArgs(c => TestData.Message("ok", TestData.Bot));
        var parent = TestData.Message("question?", TestData.Ada);
        var cut = Render<Composer>(p => p.Add(c => c.ChannelId, TestData.GeneralId).Add(c => c.GuildId, TestData.GuildId));
        await cut.InvokeAsync(() => Ui.StartReply(parent));

        cut.WaitForAssertion(() => Assert.Contains("Ada Lovelace", cut.Find(".dt-composer-banner").TextContent));
        cut.Find("textarea").Input("answer");
        await cut.InvokeAsync(() => cut.Instance.SendAsync());

        await Conn.Rest.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r => r.ReplyToMessageId == parent.Id && r.Content == "answer"), Arg.Any<CancellationToken>());
        Assert.Null(Ui.ReplyingTo);
    }

    [Fact]
    public async Task Composer_shows_send_errors_as_a_toast()
    {
        await LoginAsync();
        var cut = Render<Composer>(p => p.Add(c => c.ChannelId, TestData.GeneralId));
        cut.Find("textarea").Input(new string('x', 2100));
        await cut.InvokeAsync(() => cut.Instance.SendAsync());

        Assert.Contains("2000", Ui.Toast);
        Assert.Contains("over", cut.Find(".dt-char-count").ClassList);
    }

    [Fact]
    public async Task Settings_page_applies_presets_and_reports_invalid_input()
    {
        await LoginAsync();
        var cut = Render<SettingsPage>();

        Assert.Contains("on", cut.FindAll(".dt-chip").First(c => c.TextContent == "3:04:05 PM").ClassList);
        await cut.FindAll(".dt-chip").First(c => c.TextContent == "15:04:05").ClickAsync(new());
        Assert.Equal("HH:mm:ss", Settings.Current.TimestampFormat);

        cut.Find("#dt-ts-format").Input("%");
        await cut.Find("#dt-ts-format").ChangeAsync(new() { Value = "%" });
        Assert.Equal("HH:mm:ss", Settings.Current.TimestampFormat);
        Assert.Contains("isn't a valid", cut.Find(".dt-settings-errors").TextContent);

        await cut.Find("#dt-wh-name").ChangeAsync(new() { Value = "Discord Mod" });
        Assert.Contains("webhook names", cut.Find(".dt-settings-errors").TextContent);

        await cut.FindAll("button[role=radio]").First(b => b.TextContent == "Midnight").ClickAsync(new());
        await cut.Find("input[type=checkbox]").ChangeAsync(new() { Value = true });
        Assert.Equal(ThemeMode.Midnight, Settings.Current.Theme);
        Assert.True(Settings.Current.CompactMode);
    }

    [Fact]
    public async Task Forgetting_the_token_disconnects_and_returns_to_setup()
    {
        await Tokens.SaveTokenAsync(AccountKind.User, TestData.Token);
        var cut = Render<DisctalkApp>();
        await cut.InvokeAsync(() => Conn.LoginAsync());
        await cut.InvokeAsync(Ui.OpenSettings);

        await cut.Find(".dt-settings .dt-btn.danger").ClickAsync(new());

        Assert.Null(await Tokens.GetTokenAsync(AccountKind.User));
        Assert.NotNull(cut.Find(".dt-setup"));
        Assert.Equal(ConnectionState.Disconnected, Client.State);
    }

    [Fact]
    public async Task Components_unsubscribe_from_client_events_when_disposed()
    {
        await Tokens.SaveTokenAsync(AccountKind.User, TestData.Token);
        var cut = Render<DisctalkApp>();
        await cut.InvokeAsync(() => Conn.LoginAsync());
        Assert.True(ChangedHandlerCount() > 0);

        await DisposeComponentsAsync();

        // A component that subscribed twice would leave a handler (and itself) behind here.
        Assert.Equal(0, ChangedHandlerCount());
    }

    [Fact]
    public async Task Tapping_a_message_toggles_its_action_bar()
    {
        await LoginAsync();
        Conn.Rest.GetMessagesBeforeAsync(default, default, default).ReturnsForAnyArgs([TestData.Message("tap me", TestData.Ada)]);
        Ui.SelectChannel(TestData.GuildId, TestData.GeneralId);
        var cut = Render<ChatPane>();
        // Scrollback that completes synchronously must still show up (regression).
        cut.WaitForAssertion(() => cut.Find(".dt-message"));

        cut.Find(".dt-message").Click();
        Assert.Contains("selected", cut.Find(".dt-message").ClassList);
        cut.Find(".dt-message").Click();
        Assert.DoesNotContain("selected", cut.Find(".dt-message").ClassList);
    }

    int ChangedHandlerCount()
    {
        var field = typeof(Core.State.ChatClient).GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (field.GetValue(Client) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
