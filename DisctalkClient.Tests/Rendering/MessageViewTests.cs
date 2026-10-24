using Bunit;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.UI.Components;

namespace DisctalkClient.Tests.Rendering;

public class MessageViewTests : UiTestBase
{
    static readonly DateTimeOffset At = new(2026, 10, 24, 15, 3, 5, TimeSpan.Zero);

    IRenderedComponent<MessageView> Render(ChatMessage message, bool grouped = false, bool compact = false, bool canEdit = false, int version = 0) =>
        Render<MessageView>(p => p
            .Add(c => c.Message, message)
            .Add(c => c.Grouped, grouped)
            .Add(c => c.Compact, compact)
            .Add(c => c.CanEdit, canEdit)
            .Add(c => c.Version, version));

    [Fact]
    public async Task Header_shows_nickname_role_color_and_timestamp_with_seconds()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("hello", TestData.Ada, At));

        Assert.Equal("Ada Lovelace", cut.Find(".dt-author").TextContent);
        Assert.Equal("color:#6cb4ee", cut.Find(".dt-author").GetAttribute("style"));
        Assert.Equal("Today at 3:03:05 PM", cut.Find("time.dt-time").TextContent);
        Assert.Equal("Saturday, October 24, 2026 3:03:05 PM", cut.Find("time.dt-time").GetAttribute("title"));
        Assert.Equal("ada.png", cut.Find("img.dt-avatar").GetAttribute("src"));
    }

    [Fact]
    public async Task Grouped_messages_hide_the_header_and_show_hover_time()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("again", TestData.Ada, At), grouped: true);

        Assert.Empty(cut.FindAll(".dt-message-header"));
        Assert.Contains("grouped", cut.Find(".dt-message").ClassList);
        Assert.Equal("3:03:05 PM", cut.Find(".dt-time-hover").TextContent);
    }

    [Fact]
    public async Task Compact_mode_is_one_line_with_time_first()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("short", TestData.Ada, At), compact: true);

        var line = cut.Find(".dt-compact-line");
        Assert.Equal("3:03:05 PM", line.QuerySelector(".dt-time-compact")!.TextContent);
        Assert.Equal("Ada Lovelace", line.QuerySelector(".dt-author")!.TextContent);
        Assert.Empty(cut.FindAll("img.dt-avatar"));
    }

    [Fact]
    public async Task Timestamp_setting_changes_rendering()
    {
        await LoginAsync();
        var message = TestData.Message("x", TestData.Ada, At);
        var cut = Render(message);
        await Settings.UpdateAsync(s => s with { TimestampFormat = "HH:mm:ss" });
        cut.Render(p => p.Add(c => c.Message, message).Add(c => c.Version, 1));

        Assert.Equal("Today at 15:03:05", cut.Find("time.dt-time").TextContent);
    }

    [Fact]
    public async Task Markdown_is_rendered_and_html_is_escaped()
    {
        await LoginAsync();
        var cut = Render(TestData.Message($"**hi** <@{TestData.BobId}> <b>raw</b>", TestData.Ada, At));

        var content = cut.Find(".dt-content");
        Assert.Equal("hi", content.QuerySelector("strong")!.TextContent);
        Assert.Equal("@bob", content.QuerySelector(".md-mention")!.TextContent);
        Assert.Null(content.QuerySelector("b"));
        Assert.Contains("<b>raw</b>", content.TextContent);
    }

    [Fact]
    public async Task Edited_marker_has_full_timestamp_tooltip()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("fixed", TestData.Ada, At) with { EditedAt = At.AddSeconds(30) });
        Assert.Equal("Saturday, October 24, 2026 3:03:35 PM", cut.Find(".dt-edited").GetAttribute("title"));
    }

    [Fact]
    public async Task Reply_preview_shows_parent_author_and_snippet()
    {
        await LoginAsync();
        var parent = TestData.Message("```cs\nvar a = 1;\n```", TestData.Ada, At.AddMinutes(-1));
        var cut = Render(TestData.Message("same", TestData.Bob, At) with { ReplyToId = parent.Id, ReplyTo = parent, Kind = ChatMessageKind.Reply });

        Assert.Equal("Ada Lovelace", cut.Find(".dt-reply-author").TextContent);
        Assert.Equal("var a = 1;", cut.Find(".dt-reply-content").TextContent);
    }

    [Fact]
    public async Task Reply_to_deleted_message_says_so()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("hm", TestData.Bob, At) with { ReplyToId = 1, Kind = ChatMessageKind.Reply });
        Assert.Equal("Original message was deleted", cut.Find(".dt-reply-missing").TextContent);
    }

    [Fact]
    public async Task Attachments_render_images_files_and_spoilers()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("", TestData.Ada, At) with
        {
            Attachments =
            [
                new AttachmentInfo { Id = 1, FileName = "cat.png", Url = "https://cdn/cat.png", ProxyUrl = "https://media/cat.png", ContentType = "image/png", Width = 800, Height = 600 },
                new AttachmentInfo { Id = 2, FileName = "notes.txt", Url = "https://cdn/notes.txt", Size = 1536 },
                new AttachmentInfo { Id = 3, FileName = "SPOILER_surprise.jpg", Url = "https://cdn/s.jpg" },
            ],
        });

        var images = cut.FindAll(".dt-attachment-media img");
        Assert.Equal(2, images.Count);
        Assert.Equal("https://media/cat.png?width=800&height=600", images[0].GetAttribute("src"));
        Assert.Equal("400", images[0].GetAttribute("width"));
        Assert.Equal("1.5 KB", cut.Find(".dt-attachment-file small").TextContent);
        Assert.Equal("https://cdn/notes.txt", cut.Find(".dt-attachment-file").GetAttribute("href"));
        Assert.Contains("spoiler", cut.FindAll(".dt-attachment-media")[1].ClassList);

        cut.FindAll(".dt-attachment-media")[1].Click();
        Assert.DoesNotContain("spoiler", cut.FindAll(".dt-attachment-media")[1].ClassList);
        cut.FindAll(".dt-attachment-media")[0].Click();
        Assert.Equal("https://cdn/cat.png", Ui.LightboxUrl);
    }

    [Fact]
    public async Task Embeds_render_title_description_fields_and_color()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("look", TestData.Ada, At) with
        {
            Embeds =
            [
                new EmbedInfo
                {
                    Title = "Cat", Url = "https://en.wikipedia.org/wiki/Cat", Description = "A **small** mammal", Color = 0xF2A65A,
                    Fields = [new("Lifespan", "12–18 years", true)], FooterText = "Wikipedia", Timestamp = At,
                },
                new EmbedInfo { Title = "Evil", Url = "javascript:alert(1)" },
            ],
        });

        var embeds = cut.FindAll(".dt-embed");
        Assert.Equal("border-left-color:#f2a65a", embeds[0].GetAttribute("style"));
        Assert.Equal("https://en.wikipedia.org/wiki/Cat", embeds[0].QuerySelector(".dt-embed-title a")!.GetAttribute("href"));
        Assert.Equal("small", embeds[0].QuerySelector(".dt-embed-description strong")!.TextContent);
        Assert.Equal("Lifespan", embeds[0].QuerySelector(".dt-embed-field-name")!.TextContent);
        Assert.Contains("Today at 3:03:05 PM", embeds[0].QuerySelector(".dt-embed-footer")!.TextContent);
        Assert.Null(embeds[1].QuerySelector("a"));
    }

    [Fact]
    public async Task Reactions_show_counts_and_toggle()
    {
        await LoginAsync();
        var custom = new EmojiRef(900000000000000500, "blobcat");
        EmojiRef? toggled = null;
        var cut = Render<MessageView>(p => p
            .Add(c => c.Message, TestData.Message("x", TestData.Ada, At) with { Reactions = [new(new EmojiRef(null, "🐾"), 3, true), new(custom, 1, false)] })
            .Add(c => c.OnToggleReaction, r => toggled = r.Emoji));

        var buttons = cut.FindAll(".dt-reaction");
        Assert.Contains("me", buttons[0].ClassList);
        Assert.Equal("3", buttons[0].QuerySelector(".dt-reaction-count")!.TextContent);
        Assert.Equal(custom.ImageUrl(32), buttons[1].QuerySelector("img")!.GetAttribute("src"));
        buttons[1].Click();
        Assert.Equal(custom, toggled);
    }

    [Fact]
    public async Task Webhook_messages_use_their_persona_and_app_tag()
    {
        await LoginAsync();
        // The webhook id happens to equal a member id: the persona must still win.
        var cut = Render(TestData.Message("hi", TestData.Webhook(TestData.AdaId, "Andre", "andre.png"), At) with { WebhookId = TestData.AdaId });

        Assert.Equal("Andre", cut.Find(".dt-author").TextContent);
        Assert.Null(cut.Find(".dt-author").GetAttribute("style"));
        Assert.Equal("APP", cut.Find(".dt-tag").TextContent);
        Assert.Equal("andre.png", cut.Find("img.dt-avatar").GetAttribute("src"));
    }

    [Fact]
    public async Task Mentions_of_the_bot_are_highlighted_but_not_own_messages()
    {
        await LoginAsync();
        var toBot = Render(TestData.Message("hey", TestData.Ada, At) with { MentionedUserIds = [TestData.BotId] });
        var fromBot = Render(TestData.Message("me", TestData.Bot, At) with { MentionedUserIds = [TestData.BotId] }, canEdit: true);
        var everyone = Render(TestData.Message("@everyone", TestData.Ada, At) with { MentionsEveryone = true });

        Assert.Contains("mentioned", toBot.Find(".dt-message").ClassList);
        Assert.DoesNotContain("mentioned", fromBot.Find(".dt-message").ClassList);
        Assert.Contains("mentioned", everyone.Find(".dt-message").ClassList);
    }

    [Fact]
    public async Task Edit_button_only_when_editable_and_actions_raise_callbacks()
    {
        await LoginAsync();
        ChatMessage? replied = null, edited = null;
        (ChatMessage, bool)? deleted = null;
        var message = TestData.Message("x", TestData.Bot, At);
        var cut = Render<MessageView>(p => p
            .Add(c => c.Message, message)
            .Add(c => c.CanEdit, true)
            .Add(c => c.OnReply, m => replied = m)
            .Add(c => c.OnEdit, m => edited = m)
            .Add(c => c.OnDelete, d => deleted = d));

        cut.Find("button[aria-label=Reply]").Click();
        cut.Find("button[aria-label=Edit]").Click();
        cut.Find("button[aria-label=Delete]").Click(new() { ShiftKey = true });

        Assert.Same(message, replied);
        Assert.Same(message, edited);
        Assert.Equal((message, true), deleted);

        var readOnly = Render(TestData.Message("x", TestData.Ada, At));
        Assert.Empty(readOnly.FindAll("button[aria-label=Edit]"));
    }

    [Fact]
    public async Task System_messages_render_as_a_single_line()
    {
        await LoginAsync();
        var cut = Render(TestData.Message("", TestData.Ada, At) with { Kind = ChatMessageKind.System, SystemText = "Ada joined the server." });
        Assert.Equal("Ada joined the server.", cut.Find(".dt-system-text").TextContent);
        Assert.Empty(cut.FindAll(".dt-message-actions"));
    }

    [Fact]
    public async Task Unchanged_messages_skip_rerendering()
    {
        await LoginAsync();
        var message = TestData.Message("x", TestData.Ada, At);
        var cut = Render(message);
        var renders = cut.RenderCount;

        cut.Render(p => p.Add(c => c.Message, message));
        Assert.Equal(renders, cut.RenderCount);

        cut.Render(p => p.Add(c => c.Message, message with { Content = "y" }));
        Assert.True(cut.RenderCount > renders);
        Assert.Equal("y", cut.Find(".dt-content").TextContent);
    }
}
