using Bunit;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;
using DisctalkClient.Core.Settings;
using DisctalkClient.UI.Components;

namespace DisctalkClient.Tests.Rendering;

/// <summary>What changes when you're signed in as yourself: DMs, threads, forwards, labels, and the sign-in choice.</summary>
public class UserModeRenderingTests : UiTestBase
{
    const ulong DmId = 950000000000000001, GroupId = 950000000000000002, ThreadId = 950000000000000003;

    async Task LoginAsUserAsync()
    {
        await Client.ConnectAsync(AccountKind.User, TestData.Token);
        await Conn.LoginAsync();
        await Conn.RaiseAsync(new GuildAvailableEvent(new GuildSnapshot(GuildInfo.DirectMessages,
        [
            new() { Id = DmId, GuildId = 0, Name = "Ada", Kind = ChannelKind.DirectMessage, IconUrl = "ada.png", LastMessageId = 2, RecipientIds = [TestData.AdaId] },
            new() { Id = GroupId, GuildId = 0, Name = "Ada, bob", Kind = ChannelKind.GroupDirectMessage, LastMessageId = 1, RecipientIds = [TestData.AdaId, TestData.BobId] },
        ],
        [], [TestData.Member(TestData.AdaId, "ada") with { GuildId = 0 }, TestData.Member(TestData.BobId, "bob") with { GuildId = 0 }], [])));
    }

    [Fact]
    public async Task Sign_in_defaults_to_my_account_and_can_switch_to_the_bot()
    {
        await Tokens.SaveTokenAsync(AccountKind.Bot, TestData.Token);
        var cut = Render<DisctalkApp>();

        Assert.Contains("on", cut.Find(".dt-setup-kind button:first-child").ClassList);
        Assert.Equal("User token", cut.Find("label[for=dt-token]").TextContent);

        // The bot already has a saved token, so choosing it connects straight away.
        await cut.Find(".dt-setup-kind button:last-child").ClickAsync(new());

        Assert.Equal(AccountKind.Bot, Factory.LastKind);
        Assert.Equal(AccountKind.Bot, Settings.Current.Account);
    }

    [Fact]
    public async Task A_new_user_token_is_saved_under_the_user_kind()
    {
        var cut = Render<DisctalkApp>();
        cut.Find("#dt-token").Input($"\"{TestData.Token}\"");
        await cut.Find(".dt-setup .dt-btn.primary").ClickAsync(new());

        Assert.Equal((AccountKind.User, TestData.Token), (Factory.LastKind!.Value, Factory.LastToken!));
        Assert.Equal(TestData.Token, await Tokens.GetTokenAsync(AccountKind.User));
        Assert.Null(await Tokens.GetTokenAsync(AccountKind.Bot));
    }

    [Fact]
    public async Task Direct_messages_list_first_with_avatars_and_only_their_members()
    {
        await LoginAsUserAsync();
        Ui.SelectChannel(GuildInfo.DirectMessagesId, DmId);

        var list = Render<ChannelList>();
        Assert.Equal("✉ Direct Messages", list.Find(".dt-guild-select option").TextContent);
        Assert.Equal(["Ada", "Ada, bob"], list.FindAll(".dt-channel-name").Select(e => e.TextContent));
        Assert.Equal("ada.png", list.Find(".dt-channel.dm img").GetAttribute("src"));
        Assert.Equal("Online", list.Find(".dt-self-name small").TextContent);

        var members = Render<MemberList>();
        Assert.Equal(["ada"], members.FindAll(".dt-member-name").Select(e => e.TextContent)); // not bob: he's only in the group
    }

    [Fact]
    public async Task Dm_composer_has_no_webhook_toggle_and_always_sends_as_me()
    {
        await LoginAsUserAsync();
        await Settings.UpdateAsync(s => s with { SendMode = SendMode.Webhook });
        Ui.SelectChannel(GuildInfo.DirectMessagesId, DmId);
        Conn.Rest.SendMessageAsync(default!).ReturnsForAnyArgs(c => TestData.Message("hi", TestData.Bot, channelId: DmId));

        var cut = Render<Composer>(p => p.Add(c => c.ChannelId, DmId).Add(c => c.GuildId, GuildInfo.DirectMessagesId));
        Assert.Empty(cut.FindAll(".dt-send-mode"));
        Assert.Equal("Message @Ada", cut.Find("textarea").GetAttribute("placeholder"));

        cut.Find("textarea").Input("hi");
        await cut.InvokeAsync(() => cut.Instance.SendAsync());

        await Conn.Rest.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r => r.ChannelId == DmId && r.Content == "hi"), Arg.Any<CancellationToken>());
        await Executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Guild_composer_labels_the_direct_mode_as_me()
    {
        await LoginAsUserAsync();
        var cut = Render<Composer>(p => p.Add(c => c.ChannelId, TestData.GeneralId).Add(c => c.GuildId, TestData.GuildId));
        Assert.Equal("Me", cut.Find(".dt-send-mode span").TextContent);
    }

    [Fact]
    public async Task Thread_chip_opens_the_thread_even_if_it_was_not_listed()
    {
        await LoginAsUserAsync();
        var message = TestData.Message("let's discuss") with { Thread = new ThreadRef(ThreadId, "cat names", 3) };

        var cut = Render<MessageView>(p => p.Add(c => c.Message, message));
        Assert.Contains("cat names", cut.Find(".dt-thread-chip").TextContent);
        Assert.Contains("3 messages", cut.Find(".dt-thread-chip small").TextContent);

        await cut.Find(".dt-thread-chip").ClickAsync(new());

        Assert.Equal(ThreadId, Ui.ChannelId);
        var thread = Client.GetChannel(ThreadId)!;
        Assert.Equal((ChannelKind.Thread, TestData.GeneralId, TestData.GuildId), (thread.Kind, thread.ParentId!.Value, thread.GuildId));
    }

    [Fact]
    public async Task Threads_show_under_their_parent_channel()
    {
        await LoginAsUserAsync();
        await Conn.RaiseAsync(new ChannelUpsertEvent(new ChannelInfo
        {
            Id = ThreadId, GuildId = TestData.GuildId, Name = "cat names", Kind = ChannelKind.Thread, ParentId = TestData.GeneralId,
        }));
        Ui.SelectGuild(TestData.GuildId);

        var list = Render<ChannelList>();

        Assert.Equal(["general", "cat names", "random"], list.FindAll(".dt-channel-name").Select(e => e.TextContent));
        Assert.Contains("thread", list.FindAll(".dt-channel")[1].ClassList);
    }

    [Fact]
    public async Task Forwarded_messages_are_labeled()
    {
        await LoginAsUserAsync();
        var cut = Render<MessageView>(p => p.Add(c => c.Message, TestData.Message("fwd text") with { IsForwarded = true }));
        Assert.Contains("Forwarded", cut.Find(".dt-forwarded").TextContent);
        Assert.Equal("fwd text", cut.Find(".dt-content").TextContent);
    }
}
