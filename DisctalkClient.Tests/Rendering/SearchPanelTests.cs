using Bunit;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Archive;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.State;
using DisctalkClient.UI.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DisctalkClient.Tests.Rendering;

public class SearchPanelTests : UiTestBase
{
    readonly IArchiveDatabase _db = Substitute.For<IArchiveDatabase>();

    public SearchPanelTests() => Services.AddSingleton(_db);

    ArchiveSearchService Archive => Services.GetRequiredService<ArchiveSearchService>();

    [Fact]
    public async Task Without_a_database_it_points_to_settings()
    {
        await LoginAsync();
        var cut = Render<SearchPanel>();

        await cut.Find(".dt-search-empty .dt-btn").ClickAsync(new());

        Assert.True(Ui.SettingsOpen);
    }

    [Fact]
    public async Task Results_show_channel_author_and_time_with_seconds_and_jump_on_click()
    {
        await LoginAsync();
        await Archive.SaveConnectionAsync(new ArchiveConnection("db", 3306, "discord", "u", "p"));
        var at = new DateTimeOffset(2026, 10, 24, 15, 3, 5, TimeSpan.Zero);
        var target = TestData.Message("the cat is a loaf", TestData.Ada, at);
        _db.QueryAsync(default!, default!, default).ReturnsForAnyArgs([
            new ArchiveRow(target.Id, TestData.GeneralId, TestData.AdaId, "ada", target.Content, null, "general", TestData.GuildId),
        ]);
        Conn.Rest.GetMessagesBeforeAsync(default, default, default).ReturnsForAnyArgs([target]);
        Ui.SelectGuild(TestData.GuildId);

        var cut = Render<SearchPanel>();
        cut.Find("#dt-search-input").Input("cat");
        await cut.Find("#dt-search-input").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        var hit = cut.Find(".dt-search-hit");
        Assert.Equal("#general", hit.QuerySelector(".dt-search-channel")!.TextContent);
        Assert.Equal("Today at 3:03:05 PM", hit.QuerySelector("time")!.TextContent);
        Assert.Equal("Ada Lovelace", hit.QuerySelector(".dt-author")!.TextContent);
        // "This server only" is on by default.
        await _db.Received(1).QueryAsync(Arg.Any<ArchiveConnection>(),
            Arg.Is<SqlCommandText>(c => c.Parameters.Any(p => Equals(p.Value, TestData.GuildId))), Arg.Any<CancellationToken>());

        await hit.ClickAsync(new());

        Assert.Equal(TestData.GeneralId, Ui.ChannelId);
        Assert.NotNull(Client.GetMessage(TestData.GeneralId, target.Id));
    }
}

public class LocateMessageTests
{
    readonly FakeConnectionFactory _factory = new();
    readonly ChatClient _client;

    public LocateMessageTests()
    {
        var store = new InMemorySettingsStore();
        _client = new ChatClient(_factory, Substitute.For<IWebhookExecutor>(), new Core.Settings.SettingsService(store), store, TimeProvider.System);
    }

    static List<ChatMessage> Page(int startMinutesAgo) => Enumerable.Range(0, ChatClient.HistoryPageSize)
        .Select(i => TestData.Message($"m{startMinutesAgo + i}", at: TestData.Now.AddMinutes(-(startMinutesAgo + i)))).ToList();

    [Fact]
    public async Task Pages_back_until_the_message_is_loaded_but_no_further_than_the_limit()
    {
        await _client.ConnectAsync(AccountKind.User, TestData.Token);
        await _factory.Connection.LoginAsync();
        var pages = Enumerable.Range(0, 10).Select(i => Page(i * ChatClient.HistoryPageSize)).ToList();
        var rest = _factory.Connection.Rest;
        rest.GetMessagesBeforeAsync(TestData.GeneralId, null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pages[0]);
        for (int i = 1; i < pages.Count; i++)
            rest.GetMessagesBeforeAsync(TestData.GeneralId, pages[i - 1][^1].Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pages[i]);

        Assert.True(await _client.LocateMessageAsync(TestData.GeneralId, pages[2][10].Id));
        await rest.ReceivedWithAnyArgs(3).GetMessagesBeforeAsync(default, default, default);

        rest.ClearReceivedCalls();
        Assert.False(await _client.LocateMessageAsync(TestData.GeneralId, pages[9][10].Id));
        await rest.ReceivedWithAnyArgs(ChatClient.MaxJumpPages).GetMessagesBeforeAsync(default, default, default);
    }

    [Fact]
    public async Task Stops_once_history_is_older_than_a_deleted_message()
    {
        await _client.ConnectAsync(AccountKind.User, TestData.Token);
        await _factory.Connection.LoginAsync();
        var page = Page(0);
        _factory.Connection.Rest.GetMessagesBeforeAsync(default, default, default).ReturnsForAnyArgs(page);

        var deletedId = page[5].Id + 1; // between two loaded messages: it existed once, it's gone now
        Assert.False(await _client.LocateMessageAsync(TestData.GeneralId, deletedId));
        await _factory.Connection.Rest.ReceivedWithAnyArgs(1).GetMessagesBeforeAsync(default, default, default);
    }
}
