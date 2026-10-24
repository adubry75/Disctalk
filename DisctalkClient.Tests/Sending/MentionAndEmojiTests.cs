using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;

namespace DisctalkClient.Tests.Sending;

public class MentionPolicyTests
{
    [Fact]
    public void Extracts_distinct_user_mentions_including_nickname_form()
    {
        var ids = MentionPolicy.ExtractUserMentions($"<@{TestData.AdaId}> <@!{TestData.AdaId}> <@{TestData.BobId}> <@&{TestData.ModRoleId}> <#{TestData.GeneralId}>");
        Assert.Equal([TestData.AdaId, TestData.BobId], ids);
    }

    [Fact]
    public void Policy_lists_users_explicitly_and_allows_roles()
    {
        var m = MentionPolicy.For($"hey <@{TestData.AdaId}> @everyone", allowEveryone: false, pingRepliedUser: true);
        Assert.Equal([TestData.AdaId], m.Users);
        Assert.True(m.AllRoles);
        Assert.False(m.Everyone);
        Assert.True(m.RepliedUser);
    }

    [Fact]
    public void Excluded_users_are_not_pinged() =>
        Assert.Empty(MentionPolicy.For($"<@{TestData.AdaId}>", false, false, exclude: [TestData.AdaId]).Users);
}

public class EmojiShortcodeTests
{
    static readonly GuildEmojiInfo[] Server =
    [
        new(900000000000000500, TestData.GuildId, "blobcat", false),
        new(900000000000000501, TestData.GuildId, "catjam", true),
    ];

    [Theory]
    [InlineData("hi :blobcat:", "hi <:blobcat:900000000000000500>")]
    [InlineData(":catjam: :catjam:", "<a:catjam:900000000000000501> <a:catjam:900000000000000501>")]
    [InlineData("so :smile_cat: :thumbsup:", "so 😸 👍")]
    [InlineData(":not_a_real_emoji:", ":not_a_real_emoji:")]
    [InlineData("time 10:30:45", "time 10:30:45")]
    [InlineData("already <:blobcat:900000000000000500>", "already <:blobcat:900000000000000500>")]
    [InlineData("`:blobcat:` stays code", "`:blobcat:` stays code")]
    [InlineData("```\n:smile:\n``` :smile:", "```\n:smile:\n``` 😄")]
    public void Expands_shortcodes_like_the_official_client(string input, string expected) =>
        Assert.Equal(expected, EmojiShortcodes.Expand(input, Server));

    [Fact]
    public void Server_emoji_win_over_unicode_names()
    {
        var custom = new[] { new GuildEmojiInfo(900000000000000502, TestData.GuildId, "cat", false) };
        Assert.Equal("<:cat:900000000000000502>", EmojiShortcodes.Expand(":cat:", custom));
    }
}
