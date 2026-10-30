using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.UserAccount;
using static DisctalkClient.Tests.UserAccount.UserTestKit;

namespace DisctalkClient.Tests.UserAccount;

public class UserEventTranslatorTests
{
    readonly UserEventTranslator _t = new();

    const string ReadyJson = """
    {
      "v": 9, "session_id": "sess-1", "resume_gateway_url": "wss://gateway-us-east1-b.discord.gg",
      "user": {"id":"110000000000000100","username":"whiskers","global_name":"Whiskers"},
      "users": [{"id":"5","username":"bob"},{"id":"6","username":"cy","global_name":"Cy"}],
      "guilds": [
        {"id":"1","name":"Cafe","icon":null,
         "channels":[{"id":"10","type":0,"name":"general","position":0,"last_message_id":"500"},{"id":"11","type":4,"name":"Chat"}],
         "threads":[{"id":"12","type":11,"name":"a thread","parent_id":"10","guild_id":"1"}],
         "roles":[{"id":"1","name":"@everyone"},{"id":"300","name":"mods","color":255,"hoist":true,"position":2}],
         "emojis":[{"id":"77","name":"blobcat"}]},
        {"id":"2","unavailable":true}
      ],
      "merged_members": [[{"user_id":"110000000000000100","nick":"Whisk","roles":["300"]}], []],
      "private_channels": [
        {"id":"20","type":1,"recipient_ids":["5"],"last_message_id":"600"},
        {"id":"21","type":3,"recipient_ids":["5","6"],"last_message_id":"601"}
      ],
      "read_state": {"entries":[{"id":"10","last_message_id":"499"},{"id":"20","last_message_id":"600"}],"partial":false,"version":1}
    }
    """;

    [Fact]
    public void Ready_yields_user_read_state_guilds_and_dms_in_that_order()
    {
        var events = _t.Translate("READY", Parse(ReadyJson));

        Assert.Equal(("sess-1", "wss://gateway-us-east1-b.discord.gg"), (_t.SessionId, _t.ResumeGatewayUrl));
        Assert.Collection(events,
            e => Assert.Equal("Whiskers", Assert.IsType<ReadyEvent>(e).CurrentUser.DisplayName),
            e => Assert.Equal(new Dictionary<ulong, ulong> { [10] = 499, [20] = 600 }, Assert.IsType<ReadStateEvent>(e).LastReadMessageIds),
            e =>
            {
                var g = Assert.IsType<GuildAvailableEvent>(e).Snapshot;
                Assert.Equal("Cafe", g.Guild.Name);
                Assert.Equal(["general", "Chat", "a thread"], g.Channels.Select(c => c.Name));
                Assert.Equal(ChannelKind.Thread, g.Channels[2].Kind);
                Assert.Equal(10UL, g.Channels[2].ParentId);
                Assert.Equal("Whisk", Assert.Single(g.Members).DisplayName); // our own merged member
                Assert.Equal("blobcat", Assert.Single(g.Emojis).Name);
                Assert.Equal(2, g.Roles.Count);
            },
            e =>
            {
                // Guild 2 is unavailable (comes later as GUILD_CREATE); DMs come as the pseudo guild.
                var dms = Assert.IsType<GuildAvailableEvent>(e).Snapshot;
                Assert.True(dms.Guild.IsDirectMessages);
                Assert.Equal(["bob", "bob, Cy"], dms.Channels.Select(c => c.Name));
                Assert.Equal(["bob", "Cy"], dms.Members.Select(m => m.DisplayName));
            });
    }

    [Fact]
    public void Read_state_as_a_plain_array_is_accepted()
    {
        var events = _t.Translate("READY", Element(new { user = SelfUser, read_state = new[] { new { id = "10", last_message_id = "7" } } }));
        Assert.Equal(7UL, events.OfType<ReadStateEvent>().Single().LastReadMessageIds[10]);
    }

    [Fact]
    public void Guild_messages_pair_the_member_with_the_author()
    {
        var events = _t.Translate("MESSAGE_CREATE", Parse("""
            {"id":"9","channel_id":"10","guild_id":"1","type":0,"content":"hi",
             "author":{"id":"5","username":"bob"},"member":{"nick":"Bobby","roles":["300"]}}
            """));

        var members = Assert.IsType<MembersUpsertEvent>(events[0]);
        Assert.Equal((1UL, "Bobby"), (members.GuildId, Assert.Single(members.Members).DisplayName));
        Assert.Equal("hi", Assert.IsType<MessageCreateEvent>(events[1]).Message.Content);
    }

    [Fact]
    public void Updates_without_an_author_become_patches()
    {
        var full = _t.Translate("MESSAGE_UPDATE", Parse("""{"id":"9","channel_id":"10","author":{"id":"5","username":"bob"},"content":"edited","edited_timestamp":"2026-10-24T15:05:00+00:00"}"""));
        var partial = _t.Translate("MESSAGE_UPDATE", Parse("""{"id":"9","channel_id":"10","embeds":[{"type":"link","title":"preview"}]}"""));

        Assert.Equal("edited", Assert.IsType<MessageUpdateEvent>(Assert.Single(full)).Message.Content);
        var patch = Assert.IsType<MessagePatchEvent>(Assert.Single(partial));
        Assert.Equal("preview", Assert.Single(patch.Embeds!).Title);
        Assert.Null(patch.Content);
    }

    [Fact]
    public void Deletes_reactions_and_typing_translate()
    {
        Assert.Equal([9UL], Assert.IsType<MessageDeleteEvent>(Assert.Single(_t.Translate("MESSAGE_DELETE", Parse("""{"id":"9","channel_id":"10"}""")))).MessageIds);
        Assert.Equal([8UL, 9UL], Assert.IsType<MessageDeleteEvent>(Assert.Single(_t.Translate("MESSAGE_DELETE_BULK", Parse("""{"ids":["8","9"],"channel_id":"10"}""")))).MessageIds);

        var add = Assert.IsType<ReactionAddEvent>(Assert.Single(_t.Translate("MESSAGE_REACTION_ADD", Parse("""{"user_id":"5","channel_id":"10","message_id":"9","emoji":{"id":null,"name":"🐱"}}"""))));
        Assert.Equal((10UL, 9UL, 5UL, "🐱"), (add.ChannelId, add.MessageId, add.UserId, add.Emoji.Name));
        Assert.Null(Assert.IsType<ReactionClearEvent>(Assert.Single(_t.Translate("MESSAGE_REACTION_REMOVE_ALL", Parse("""{"channel_id":"10","message_id":"9"}""")))).Emoji);

        var typing = Assert.IsType<TypingStartEvent>(Assert.Single(_t.Translate("TYPING_START", Parse("""{"channel_id":"10","guild_id":"1","user_id":"5","timestamp":1761318245,"member":{"nick":"Bobby","user":{"id":"5","username":"bob"}}}"""))));
        Assert.Equal(("Bobby", DateTimeOffset.FromUnixTimeSeconds(1761318245)), (typing.Member!.DisplayName, typing.Timestamp));
    }

    [Fact]
    public void Guild_outages_are_not_treated_as_leaving()
    {
        Assert.Empty(_t.Translate("GUILD_DELETE", Parse("""{"id":"1","unavailable":true}""")));
        Assert.Equal(1UL, Assert.IsType<GuildRemovedEvent>(Assert.Single(_t.Translate("GUILD_DELETE", Parse("""{"id":"1"}""")))).GuildId);
    }

    [Fact]
    public void New_dms_bring_their_recipients_and_archived_threads_are_removed()
    {
        _t.Translate("READY", Element(new { user = SelfUser }));
        var dm = _t.Translate("CHANNEL_CREATE", Parse("""{"id":"30","type":1,"recipients":[{"id":"5","username":"bob"}]}"""));
        Assert.Equal("bob", Assert.Single(Assert.IsType<MembersUpsertEvent>(dm[0]).Members).Username);
        Assert.Equal(GuildInfo.DirectMessagesId, Assert.IsType<ChannelUpsertEvent>(dm[1]).Channel.GuildId);

        var archived = _t.Translate("THREAD_UPDATE", Parse("""{"id":"12","type":11,"guild_id":"1","name":"t","thread_metadata":{"archived":true}}"""));
        var removed = Assert.IsType<ChannelDeleteEvent>(Assert.Single(archived));
        Assert.Equal((1UL, 12UL), (removed.GuildId, removed.ChannelId));
    }

    [Fact]
    public void Member_list_updates_yield_members_and_skip_group_headers()
    {
        var events = _t.Translate("GUILD_MEMBER_LIST_UPDATE", Parse("""
            {"guild_id":"1","id":"everyone","ops":[
              {"op":"SYNC","range":[0,99],"items":[{"group":{"id":"online","count":2}},{"member":{"user":{"id":"5","username":"bob"},"roles":[]}}]},
              {"op":"UPDATE","index":1,"item":{"member":{"user":{"id":"6","username":"cy"},"nick":"C"}}}]}
            """));

        Assert.Equal(["bob", "C"], Assert.IsType<MembersUpsertEvent>(Assert.Single(events)).Members.Select(m => m.DisplayName));
    }

    [Theory]
    [InlineData("PRESENCE_UPDATE")]
    [InlineData("SESSIONS_REPLACE")]
    [InlineData("READY_SUPPLEMENTAL")]
    public void Events_the_client_does_not_use_are_ignored(string type) => Assert.Empty(_t.Translate(type, Parse("{}")));
}
