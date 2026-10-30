using DisctalkClient.Core.Models;
using DisctalkClient.Core.UserAccount;
using static DisctalkClient.Tests.UserAccount.UserTestKit;

namespace DisctalkClient.Tests.UserAccount;

public class DiscordJsonTests
{
    const string Ada = """{"id":"900000000000000101","username":"ada","global_name":"Ada","avatar":"abc123","discriminator":"0"}""";

    [Fact]
    public void Maps_a_full_reply_with_attachments_embeds_reactions_and_stickers()
    {
        var json = Parse($$$"""
        {
          "id": "1300000000000000001", "channel_id": "900000000000000010", "guild_id": "900000000000000001", "type": 19,
          "author": {{{Ada}}}, "content": "look <@900000000000000102>",
          "edited_timestamp": "2026-10-24T15:05:00.123000+00:00",
          "attachments": [{"id":"5","filename":"cat.png","url":"https://cdn.discordapp.com/a/cat.png","proxy_url":"https://media.discordapp.net/a/cat.png","content_type":"image/png","size":1234,"width":64,"height":48}],
          "embeds": [{"type":"rich","title":"T","description":"D","color":16711680,"fields":[{"name":"f","value":"v","inline":true}],"image":{"url":"u","proxy_url":"p"},"footer":{"text":"foot"}}],
          "reactions": [{"emoji":{"id":null,"name":"🐱"},"count":2,"me":true},{"emoji":{"id":"77","name":"blobcat","animated":true},"count":1,"me":false}],
          "sticker_items": [{"id":"88","name":"wave","format_type":4}],
          "mentions": [{"id":"900000000000000102","username":"bob"}], "mention_roles": ["900000000000000300"], "mention_everyone": false,
          "message_reference": {"message_id":"1299999999999999999","channel_id":"900000000000000010"},
          "referenced_message": {"id":"1299999999999999999","channel_id":"900000000000000010","type":0,"author":{"id":"900000000000000102","username":"bob"},"content":"original"},
          "thread": {"id":"1400000000000000000","name":"cat chat","message_count":12}
        }
        """);

        var m = DiscordJson.ToMessage(json);

        Assert.Equal(1300000000000000001UL, m.Id);
        Assert.Equal(TestData.GeneralId, m.ChannelId);
        Assert.Equal(TestData.GuildId, m.GuildId);
        Assert.Equal(ChatMessageKind.Reply, m.Kind);
        Assert.Equal("Ada", m.Author.DisplayName);
        Assert.Equal("https://cdn.discordapp.com/avatars/900000000000000101/abc123.png?size=128", m.Author.AvatarUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 15, 5, 0, 123, TimeSpan.Zero), m.EditedAt);
        var a = Assert.Single(m.Attachments);
        Assert.True(a.IsImage);
        Assert.Equal((64, 48), (a.Width, a.Height));
        var e = Assert.Single(m.Embeds);
        Assert.Equal(("T", "D", 16711680, "p", "foot"), (e.Title, e.Description, e.Color, e.ImageUrl, e.FooterText));
        Assert.Equal(new EmbedFieldInfo("f", "v", true), Assert.Single(e.Fields));
        Assert.Equal([("🐱", 2, true), ("blobcat", 1, false)], m.Reactions.Select(r => (r.Emoji.Name, r.Count, r.Me)));
        Assert.True(m.Reactions[1].Emoji.Animated);
        Assert.EndsWith("/stickers/88.gif?size=160", Assert.Single(m.Stickers).ImageUrl);
        Assert.Equal([900000000000000102UL], m.MentionedUserIds);
        Assert.Equal([TestData.ModRoleId], m.MentionedRoleIds);
        Assert.Equal(1299999999999999999UL, m.ReplyToId);
        Assert.Equal("original", m.ReplyTo!.Content);
        Assert.Equal(new ThreadRef(1400000000000000000, "cat chat", 12), m.Thread);
    }

    [Fact]
    public void Reply_to_a_deleted_message_keeps_the_reference_without_a_snapshot()
    {
        var m = DiscordJson.ToMessage(Parse($$$"""{"id":"2","channel_id":"3","type":19,"author":{{{Ada}}},"content":"hm","message_reference":{"message_id":"1"},"referenced_message":null}"""));
        Assert.Equal(1UL, m.ReplyToId);
        Assert.Null(m.ReplyTo);
    }

    [Fact]
    public void Forwarded_messages_show_the_forwarded_content()
    {
        var m = DiscordJson.ToMessage(Parse($$$"""
        {"id":"2","channel_id":"3","type":0,"author":{{{Ada}}},"content":"",
         "message_reference":{"type":1,"message_id":"1","channel_id":"9"},
         "message_snapshots":[{"message":{"content":"the original text","attachments":[{"id":"4","filename":"a.txt","url":"u"}],"embeds":[]}}]}
        """));

        Assert.True(m.IsForwarded);
        Assert.Equal("the original text", m.Content);
        Assert.Equal("a.txt", Assert.Single(m.Attachments).FileName);
        Assert.Null(m.ReplyToId);
    }

    [Theory]
    [InlineData(7, "Ada joined the server.")]
    [InlineData(6, "Ada pinned a message to this channel.")]
    [InlineData(3, "Ada started a call.")]
    [InlineData(999, "Ada: message type 999")]
    public void System_messages_get_readable_text(int type, string text)
    {
        var m = DiscordJson.ToMessage(Parse($$$"""{"id":"2","channel_id":"3","type":{{{type}}},"author":{{{Ada}}},"content":""}"""));
        Assert.Equal(ChatMessageKind.System, m.Kind);
        Assert.Equal(text, m.SystemText);
    }

    [Theory]
    [InlineData(0, ChannelKind.Text)]
    [InlineData(5, ChannelKind.Announcement)]
    [InlineData(4, ChannelKind.Category)]
    [InlineData(2, ChannelKind.Voice)]
    [InlineData(13, ChannelKind.Voice)]
    [InlineData(11, ChannelKind.Thread)]
    [InlineData(15, ChannelKind.Forum)]
    [InlineData(99, ChannelKind.Other)]
    public void Guild_channel_types_map_to_kinds(int type, ChannelKind kind)
    {
        var c = DiscordJson.ToChannel(Parse($$$"""{"id":"10","type":{{{type}}},"guild_id":"1","name":"x","parent_id":"20","position":3,"topic":"t","last_message_id":"30"}"""))!;
        Assert.Equal(kind, c.Kind);
        Assert.Equal((1UL, 20UL, 3, "t", 30UL), (c.GuildId, c.ParentId!.Value, c.Position, c.Topic, c.LastMessageId!.Value));
    }

    [Fact]
    public void Archived_threads_are_not_listed() =>
        Assert.Null(DiscordJson.ToChannel(Parse("""{"id":"10","type":11,"guild_id":"1","name":"old","thread_metadata":{"archived":true}}""")));

    [Fact]
    public void Dms_are_named_after_the_other_person_and_resolve_deduplicated_recipients()
    {
        var users = new Dictionary<ulong, AuthorInfo> { [5] = new() { Id = 5, Username = "bob", AvatarUrl = "bob.png" } };

        var dm = DiscordJson.ToChannel(Parse("""{"id":"10","type":1,"recipient_ids":["5"],"last_message_id":"99"}"""), users: users, selfId: SelfId)!;
        var group = DiscordJson.ToChannel(Parse($$$"""{"id":"11","type":3,"recipients":[{{{Ada}}},{"id":"6","username":"cy"}]}"""), selfId: SelfId)!;
        var named = DiscordJson.ToChannel(Parse($$$"""{"id":"12","type":3,"name":"Cat Club","icon":"ic","recipients":[{{{Ada}}}]}"""))!;

        Assert.Equal((GuildInfo.DirectMessagesId, ChannelKind.DirectMessage, "bob", "bob.png", 99UL), (dm.GuildId, dm.Kind, dm.Name, dm.IconUrl, dm.LastMessageId!.Value));
        Assert.True(dm.IsMessageable && dm.IsPrivate);
        Assert.Equal("Ada, cy", group.Name);
        Assert.Equal("Cat Club", named.Name);
        Assert.Equal("https://cdn.discordapp.com/channel-icons/12/ic.png?size=64", named.IconUrl);
    }

    [Fact]
    public void Guild_name_and_icon_are_read_from_properties_with_client_state_v2()
    {
        var plain = DiscordJson.ToGuild(Parse("""{"id":"1","name":"Cafe","icon":"a_x"}"""));
        var v2 = DiscordJson.ToGuild(Parse("""{"id":"1","properties":{"name":"Cafe","icon":"h"}}"""));
        Assert.Equal(("Cafe", "https://cdn.discordapp.com/icons/1/a_x.gif?size=128"), (plain.Name, plain.IconUrl));
        Assert.Equal(("Cafe", "https://cdn.discordapp.com/icons/1/h.png?size=128"), (v2.Name, v2.IconUrl));
    }

    [Fact]
    public void Members_use_nick_guild_avatar_and_roles()
    {
        var m = DiscordJson.ToMember(1, Parse($$$"""{"nick":"Countess","avatar":"g1","roles":["300"],"user":{{{Ada}}}}"""))!;
        Assert.Equal(("Countess", "https://cdn.discordapp.com/guilds/1/users/900000000000000101/avatars/g1.png?size=128"), (m.DisplayName, m.AvatarUrl));
        Assert.Equal([300UL], m.RoleIds);
    }

    [Fact]
    public void Roles_read_the_new_colors_object_or_the_legacy_color()
    {
        Assert.Equal(0x6cb4ee, DiscordJson.ToRole(1, Parse("""{"id":"2","name":"m","colors":{"primary_color":7124206},"position":1,"hoist":true}""")).Color);
        Assert.Equal(0x6cb4ee, DiscordJson.ToRole(1, Parse("""{"id":"2","name":"m","color":7124206}""")).Color);
        Assert.Null(DiscordJson.ToRole(1, Parse("""{"id":"2","name":"m","color":0}""")).Color);
    }

    [Theory]
    [InlineData(80351110224678912UL, "0", 0)]  // unique usernames: (id >> 22) % 6
    [InlineData(1UL, "1337", 2)]               // legacy discriminator % 5
    public void Default_avatars_follow_discords_rules(ulong id, string discriminator, int legacyIndex)
    {
        int expected = discriminator == "0" ? (int)((id >> 22) % 6) : legacyIndex;
        Assert.Equal($"https://cdn.discordapp.com/embed/avatars/{expected}.png", DiscordJson.DefaultAvatarUrl(id, discriminator));
    }

    [Fact]
    public void Missing_or_odd_fields_never_throw()
    {
        var m = DiscordJson.ToMessage(Parse("""{"id":42,"channel_id":"3","attachments":null,"embeds":"nope","reactions":[{"count":1}]}"""));
        Assert.Equal(42UL, m.Id);
        Assert.Equal("Unknown", m.Author.Username);
        Assert.Empty(m.Attachments);
        Assert.Empty(m.Reactions);
    }
}
