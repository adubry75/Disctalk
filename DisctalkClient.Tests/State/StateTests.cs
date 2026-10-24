using DisctalkClient.Core.Models;
using DisctalkClient.Core.State;
using Microsoft.Extensions.Time.Testing;

namespace DisctalkClient.Tests.State;

public class MessageCacheTests
{
    static ChatMessage M(ulong id, string content = "x") => TestData.Message(content, id: id);

    [Fact]
    public void Keeps_messages_sorted_and_deduped()
    {
        var cache = new MessageCache();
        cache.Upsert(M(30));
        cache.Upsert(M(10));
        cache.Upsert(M(20));
        cache.Upsert(M(20, "again"));

        Assert.Equal([10UL, 20UL, 30UL], cache.Snapshot().Select(m => m.Id));
        Assert.Equal("again", cache.Get(20)!.Content);
    }

    [Fact]
    public void History_pages_merge_and_detect_the_channel_start()
    {
        var cache = new MessageCache();
        cache.Upsert(M(100)); // a live message arrived before the first page
        Assert.False(cache.IsInitialized);

        cache.AddHistoryPage([M(100), M(99), M(98)], requestedLimit: 3);
        Assert.True(cache.IsInitialized);
        Assert.True(cache.HasMoreBefore);
        Assert.Equal(98UL, cache.OldestId);

        cache.AddHistoryPage([M(97)], requestedLimit: 3);
        Assert.False(cache.HasMoreBefore);
        Assert.Equal([97UL, 98UL, 99UL, 100UL], cache.Snapshot().Select(m => m.Id));
    }

    [Fact]
    public void Edits_keep_reactions_and_reply_tracked_from_other_events()
    {
        var cache = new MessageCache();
        var parent = M(1, "parent");
        cache.Upsert(M(2) with { ReplyTo = parent, ReplyToId = 1 });
        cache.ApplyReaction(2, new EmojiRef(null, "🐾"), +1, byMe: true);

        Assert.True(cache.Update(M(2, "edited") with { EditedAt = TestData.Now }));

        var m = cache.Get(2)!;
        Assert.Equal("edited", m.Content);
        Assert.Equal("parent", m.ReplyTo!.Content);
        Assert.Equal(new ReactionInfo(new EmojiRef(null, "🐾"), 1, true), Assert.Single(m.Reactions));
    }

    [Fact]
    public void Edits_for_unknown_messages_are_ignored() => Assert.False(new MessageCache().Update(M(5)));

    [Fact]
    public void Reactions_count_up_and_down_and_track_me()
    {
        var cache = new MessageCache();
        cache.Upsert(M(1));
        var paw = new EmojiRef(null, "🐾");
        var blob = new EmojiRef(900000000000000500, "blobcat");

        cache.ApplyReaction(1, paw, +1, byMe: false);
        cache.ApplyReaction(1, paw, +1, byMe: true);
        cache.ApplyReaction(1, blob with { Name = "renamed" }, +1, byMe: false); // custom emoji match by id
        cache.ApplyReaction(1, blob, +1, byMe: false);
        Assert.Equal([(2, true), (2, false)], cache.Get(1)!.Reactions.Select(r => (r.Count, r.Me)));

        cache.ApplyReaction(1, paw, -1, byMe: true);
        Assert.False(cache.Get(1)!.Reactions[0].Me);
        cache.ApplyReaction(1, paw, -1, byMe: false);
        Assert.Single(cache.Get(1)!.Reactions);

        cache.ClearReactions(1, null);
        Assert.Empty(cache.Get(1)!.Reactions);
    }

    [Fact]
    public void Live_messages_push_out_the_oldest_beyond_the_cap()
    {
        var cache = new MessageCache(maxMessages: 3);
        cache.AddHistoryPage([M(1), M(2), M(3)], 10);
        Assert.False(cache.HasMoreBefore);
        cache.Upsert(M(4));
        Assert.Equal([2UL, 3UL, 4UL], cache.Snapshot().Select(m => m.Id));
        Assert.True(cache.HasMoreBefore); // trimmed ones can be fetched again
    }

    [Fact]
    public void Delete_removes()
    {
        var cache = new MessageCache();
        cache.Upsert(M(1));
        cache.Upsert(M(2));
        Assert.True(cache.Remove([1, 99]));
        Assert.Equal([2UL], cache.Snapshot().Select(m => m.Id));
    }
}

public class TypingTrackerTests
{
    [Fact]
    public void Typing_expires_after_ten_seconds()
    {
        var time = new FakeTimeProvider(TestData.Now);
        var typing = new TypingTracker(time);
        typing.Start(TestData.GeneralId, TestData.AdaId, "Ada");
        time.Advance(TimeSpan.FromSeconds(5));
        typing.Start(TestData.GeneralId, TestData.BobId, "Bob");

        Assert.Equal(["Ada", "Bob"], typing.GetTypingNames(TestData.GeneralId));
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(["Bob"], typing.GetTypingNames(TestData.GeneralId));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(typing.GetTypingNames(TestData.GeneralId));
    }

    [Fact]
    public void Sending_a_message_stops_typing_and_self_is_excluded()
    {
        var typing = new TypingTracker(new FakeTimeProvider(TestData.Now));
        typing.Start(TestData.GeneralId, TestData.AdaId, "Ada");
        typing.Start(TestData.GeneralId, TestData.BotId, "Whiskers");
        Assert.Equal(["Ada"], typing.GetTypingNames(TestData.GeneralId, excludeUserId: TestData.BotId));
        Assert.True(typing.Stop(TestData.GeneralId, TestData.AdaId));
        Assert.Empty(typing.GetTypingNames(TestData.GeneralId, TestData.BotId));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "Ada" }, "Ada is typing…")]
    [InlineData(new[] { "Ada", "Bob" }, "Ada and Bob are typing…")]
    [InlineData(new[] { "Ada", "Bob", "Cy" }, "Ada, Bob, and Cy are typing…")]
    [InlineData(new[] { "A", "B", "C", "D" }, "Several people are typing…")]
    public void Describes_like_discord(string[] names, string expected) => Assert.Equal(expected, TypingTracker.Describe(names));
}

public class UnreadTrackerTests
{
    [Fact]
    public void New_channels_start_read_then_go_unread_on_new_messages()
    {
        var unread = new UnreadTracker();
        unread.TrackChannel(TestData.GeneralId, lastMessageId: 50);
        Assert.False(unread.IsUnread(TestData.GeneralId));

        Assert.True(unread.OnMessage(TestData.GeneralId, 60, isActiveChannel: false));
        Assert.True(unread.IsUnread(TestData.GeneralId));
        Assert.False(unread.OnMessage(TestData.GeneralId, 70, isActiveChannel: false)); // already unread

        Assert.True(unread.MarkRead(TestData.GeneralId));
        Assert.False(unread.IsUnread(TestData.GeneralId));
    }

    [Fact]
    public void Messages_in_the_open_channel_are_read_immediately()
    {
        var unread = new UnreadTracker();
        unread.TrackChannel(TestData.GeneralId, 50);
        unread.OnMessage(TestData.GeneralId, 60, isActiveChannel: true);
        Assert.False(unread.IsUnread(TestData.GeneralId));
    }

    [Fact]
    public void Read_state_round_trips_and_detects_messages_missed_while_offline()
    {
        var before = new UnreadTracker();
        before.TrackChannel(TestData.GeneralId, 50);
        var saved = before.Serialize();

        var after = new UnreadTracker();
        after.Restore(saved);
        after.TrackChannel(TestData.GeneralId, lastMessageId: 80); // newer message arrived while closed
        Assert.True(after.IsUnread(TestData.GeneralId));

        new UnreadTracker().Restore("garbage"); // never throws
    }
}

public class ChannelTreeTests
{
    [Fact]
    public void Orders_like_the_discord_sidebar()
    {
        ChannelInfo C(ulong id, string name, ChannelKind kind = ChannelKind.Text, ulong? parent = null, int pos = 0) =>
            new() { Id = id, GuildId = TestData.GuildId, Name = name, Kind = kind, ParentId = parent, Position = pos };

        var groups = ChannelTree.Build(
        [
            C(1, "Voice", ChannelKind.Category, pos: 1),
            C(2, "Text", ChannelKind.Category, pos: 0),
            C(3, "lounge", ChannelKind.Voice, 1, 0),
            C(4, "b-chat", parent: 2, pos: 1),
            C(5, "a-chat", parent: 2, pos: 0),
            C(6, "rules", pos: 0),
            C(7, "voice-in-text", ChannelKind.Voice, 2, 0),
            C(8, "orphan", parent: 999, pos: 5),
        ]);

        Assert.Equal([null, "Text", "Voice"], groups.Select(g => g.Category?.Name));
        Assert.Equal(["rules", "orphan"], groups[0].Channels.Select(c => c.Name));
        Assert.Equal(["a-chat", "b-chat", "voice-in-text"], groups[1].Channels.Select(c => c.Name));
    }
}

public class MemberGroupingTests
{
    [Fact]
    public void Groups_by_highest_hoisted_role_and_colors_by_highest_colored_role()
    {
        var roles = new Dictionary<ulong, RoleInfo>
        {
            [1] = new() { Id = 1, GuildId = TestData.GuildId, Name = "Admin", Hoist = true, Position = 5, Color = 0xFF0000 },
            [2] = new() { Id = 2, GuildId = TestData.GuildId, Name = "Mods", Hoist = true, Position = 3 },
            [3] = new() { Id = 3, GuildId = TestData.GuildId, Name = "Blue", Position = 4, Color = 0x0000FF },
        };
        var zed = TestData.Member(10, "zed", null, 1, 2);
        var amy = TestData.Member(11, "amy", null, 2, 3);
        var bob = TestData.Member(12, "bob", "Bobby", 3);
        var cat = TestData.Member(13, "cat");

        var groups = MemberGrouping.Group([zed, amy, bob, cat], roles);

        Assert.Equal(["Admin", "Mods", "Members"], groups.Select(g => g.Title));
        Assert.Equal(["Bobby", "cat"], groups[2].Members.Select(m => m.DisplayName));
        Assert.Equal("#ff0000", MemberGrouping.NameColor(zed, roles));
        Assert.Equal("#0000ff", MemberGrouping.NameColor(amy, roles));
        Assert.Null(MemberGrouping.NameColor(cat, roles));
    }
}
