using DisctalkClient.UI.Markdown;

namespace DisctalkClient.Tests.Markdown;

public class DiscordMarkdownTests
{
    sealed class Ctx : IMarkdownContext
    {
        public string? UserName(ulong userId) => userId == TestData.AdaId ? "Ada" : userId == TestData.BotId ? "Whiskers" : null;
        public (string Name, string? Color)? Role(ulong roleId) => roleId == TestData.ModRoleId ? ("Moderators", "#6cb4ee") : null;
        public string? ChannelName(ulong channelId) => channelId == TestData.GeneralId ? "general" : null;
        public ulong? SelfUserId => TestData.BotId;
        public string FormatTimestamp(long unixSeconds, char style) => $"TS({unixSeconds},{style})";
        public string FormatTimestampTooltip(long unixSeconds) => $"FULL({unixSeconds})";
    }

    static string Html(string text, MarkdownOptions? options = null) => DiscordMarkdown.ToHtml(text, new Ctx(), options);

    [Theory]
    [InlineData("**bold**", "<strong>bold</strong>")]
    [InlineData("*italic*", "<em>italic</em>")]
    [InlineData("_italic_", "<em>italic</em>")]
    [InlineData("__under__", "<u>under</u>")]
    [InlineData("~~gone~~", "<s>gone</s>")]
    [InlineData("`x < y`", "<code class=\"md-code\">x &lt; y</code>")]
    [InlineData("``a `tick` b``", "<code class=\"md-code\">a `tick` b</code>")]
    [InlineData("||secret||", "<span class=\"md-spoiler\" tabindex=\"0\">secret</span>")]
    public void Inline_formatting(string input, string expected) => Assert.Equal(expected, Html(input));

    [Fact]
    public void Bold_italic_triple_asterisks() => Assert.Equal("<strong><em>both</em></strong>", Html("***both***"));

    [Fact]
    public void Nested_formatting() =>
        Assert.Equal("<strong>bold <em>and italic</em> text</strong>", Html("**bold *and italic* text**"));

    [Fact]
    public void Underline_inside_spoiler_inside_bold() =>
        Assert.Equal("<strong><span class=\"md-spoiler\" tabindex=\"0\"><u>x</u></span></strong>", Html("**||__x__||**"));

    [Theory]
    [InlineData("snake_case_name")]
    [InlineData("2 * 3 * 4")]
    [InlineData("**not closed")]
    [InlineData("a ~~ b")]
    public void Unmatched_or_intraword_markers_stay_literal(string input) => Assert.Equal(input, Html(input));

    [Fact]
    public void Backslash_escapes_markdown() => Assert.Equal("*not italic*", Html(@"\*not italic\*"));

    [Fact]
    public void Html_is_always_escaped()
    {
        var html = Html("<script>alert('x')</script> & <img src=x onerror=alert(1)>");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &lt;img", html);
    }

    [Fact]
    public void Code_is_escaped_and_not_formatted()
    {
        Assert.Equal("<code class=\"md-code\">**&lt;b&gt;**</code>", Html("`**<b>**`"));
    }

    [Fact]
    public void Code_block_with_language()
    {
        var html = Html("look:\n```cs\nvar x = \"<b>\";\n**not bold**\n```\nafter");
        Assert.Contains("<pre class=\"md-codeblock\"><code class=\"language-cs\">var x = &quot;&lt;b&gt;&quot;;\n**not bold**</code></pre>", html);
        Assert.StartsWith("look:", html);
        Assert.EndsWith("after", html);
    }

    [Fact]
    public void Code_block_without_language()
    {
        Assert.Equal("<pre class=\"md-codeblock\"><code>plain</code></pre>", Html("```plain```"));
    }

    [Fact]
    public void Newlines_become_breaks() => Assert.Equal("one<br>two", Html("one\ntwo"));

    [Theory]
    [InlineData("https://example.com/a?b=1&c=2")]
    [InlineData("http://example.com")]
    public void Bare_urls_link(string url)
    {
        var html = Html($"see {url} now");
        var escaped = DiscordMarkdown.Escape(url);
        Assert.Contains($"href=\"{escaped}\"", html);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", html);
        Assert.EndsWith(" now", html);
    }

    [Fact]
    public void Trailing_punctuation_is_not_part_of_url()
    {
        Assert.Contains("href=\"https://example.com\"", Html("go to https://example.com."));
        Assert.Contains("href=\"https://en.wikipedia.org/wiki/Cat_(disambiguation)\"", Html("(https://en.wikipedia.org/wiki/Cat_(disambiguation))"));
    }

    [Fact]
    public void Masked_link()
    {
        var html = Html("[the **docs**](https://discord.com/developers)");
        Assert.Equal("<a class=\"md-link\" href=\"https://discord.com/developers\" title=\"https://discord.com/developers\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">the <strong>docs</strong></a>", html);
    }

    [Theory]
    [InlineData("[click](javascript:alert(1))")]
    [InlineData("[click](data:text/html,hi)")]
    [InlineData("javascript:alert(1)")]
    public void Non_http_links_are_never_rendered(string input)
    {
        var html = Html(input);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("<a", html);
    }

    [Fact]
    public void Angle_bracket_link_suppresses_embed_but_still_links() =>
        Assert.Contains("href=\"https://example.com\"", Html("<https://example.com>"));

    [Fact]
    public void User_mentions_resolve_names_and_flag_self()
    {
        var html = Html($"hi <@{TestData.AdaId}> and <@!{TestData.BotId}> and <@123456789012345678>");
        Assert.Contains($"<span class=\"md-mention\" data-user-id=\"{TestData.AdaId}\">@Ada</span>", html);
        Assert.Contains($"<span class=\"md-mention md-mention-self\" data-user-id=\"{TestData.BotId}\">@Whiskers</span>", html);
        Assert.Contains("@unknown-user", html);
    }

    [Fact]
    public void Role_mention_uses_role_color() =>
        Assert.Equal("<span class=\"md-mention md-mention-role\" style=\"color:#6cb4ee;background-color:#6cb4ee26\">@Moderators</span>",
            Html($"<@&{TestData.ModRoleId}>"));

    [Fact]
    public void Channel_mention_is_navigable() =>
        Assert.Equal($"<span class=\"md-mention md-mention-channel\" data-channel-id=\"{TestData.GeneralId}\">#general</span>", Html($"<#{TestData.GeneralId}>"));

    [Fact]
    public void Everyone_and_here_are_highlighted()
    {
        Assert.Equal("<span class=\"md-mention md-mention-everyone\">@everyone</span> hi", Html("@everyone hi"));
        Assert.Contains("md-mention-everyone\">@here", Html("@here"));
    }

    [Fact]
    public void Custom_emoji_render_from_cdn()
    {
        var html = Html("nice <:blobcat:900000000000000500> and <a:catjam:900000000000000501>");
        Assert.Contains("src=\"https://cdn.discordapp.com/emojis/900000000000000500.webp?size=48\"", html);
        Assert.Contains("src=\"https://cdn.discordapp.com/emojis/900000000000000501.gif?size=48\"", html);
        Assert.Contains("alt=\":blobcat:\"", html);
        Assert.DoesNotContain("md-emoji-jumbo", html);
    }

    [Theory]
    [InlineData("<:blobcat:900000000000000500>", true)]
    [InlineData("😺 🐾", true)]
    [InlineData("👍🏽", true)]
    [InlineData("🐈‍⬛", true)]
    [InlineData("hi 😺", false)]
    [InlineData("", false)]
    public void Emoji_only_messages_are_jumbo(string input, bool jumbo) => Assert.Equal(jumbo, DiscordMarkdown.IsEmojiOnly(input));

    [Fact]
    public void Jumbo_class_applied_to_emoji_only_message() => Assert.Contains("md-emoji-jumbo", Html("<:blobcat:900000000000000500>"));

    [Theory]
    [InlineData("<t:1767225600>", "TS(1767225600,f)")]
    [InlineData("<t:1767225600:R>", "TS(1767225600,R)")]
    [InlineData("<t:1767225600:T>", "TS(1767225600,T)")]
    public void Timestamps_use_the_formatter(string input, string text)
    {
        var html = Html(input);
        Assert.Equal($"<span class=\"md-timestamp\" title=\"FULL(1767225600)\">{text}</span>", html);
    }

    [Fact]
    public void Slash_command_mentions() => Assert.Equal("<span class=\"md-mention md-mention-command\">/ping</span>", Html("</ping:123456789012345678>"));

    [Fact]
    public void Headings_subtext_and_quotes()
    {
        Assert.Equal("<h1 class=\"md-h\">Title</h1>", Html("# Title"));
        Assert.Equal("<h3 class=\"md-h\"><em>Small</em></h3>", Html("### *Small*"));
        Assert.Equal("#nospace", Html("#nospace"));
        Assert.Equal("<div class=\"md-subtext\">fine print</div>", Html("-# fine print"));
        Assert.Equal("<blockquote class=\"md-quote\">a<br>b</blockquote>after", Html("> a\n> b\nafter"));
        Assert.Equal("intro<blockquote class=\"md-quote\">rest<br>of message</blockquote>", Html("intro\n>>> rest\nof message"));
    }

    [Fact]
    public void Headings_are_off_in_inline_only_mode() => Assert.Equal("# Title", Html("# Title", MarkdownOptions.InlineOnly));

    [Fact]
    public void Lists()
    {
        Assert.Equal("<ul class=\"md-list\"><li>one</li><li><strong>two</strong></li><li class=\"md-li-1\">nested</li></ul>", Html("- one\n* **two**\n  - nested"));
        Assert.Equal("<ol class=\"md-list\" start=\"3\"><li>three</li><li>four</li></ol>", Html("3. three\n4. four"));
    }

    [Fact]
    public void Deep_nesting_does_not_blow_up()
    {
        var input = string.Concat(Enumerable.Repeat("**||", 400)) + "x" + string.Concat(Enumerable.Repeat("||**", 400));
        var html = Html(input);
        Assert.Contains("x", html);
    }

    [Fact]
    public void Null_and_empty_render_nothing()
    {
        Assert.Equal("", DiscordMarkdown.ToHtml(null));
        Assert.Equal("", DiscordMarkdown.ToHtml(""));
    }
}
