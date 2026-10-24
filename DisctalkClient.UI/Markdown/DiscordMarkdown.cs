using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.UI.Markdown;

/// <summary>
/// Discord-flavored markdown → HTML. Every piece of user text is HTML-escaped; the only markup in the output is
/// what this class emits, and links are restricted to http(s). Supported: **bold**, *italic*/_italic_,
/// __underline__, ~~strike~~, ||spoiler||, `code`, ```code blocks```, &gt; and &gt;&gt;&gt; quotes, # headings,
/// -# subtext, lists, masked links, autolinks, &lt;@user&gt;/&lt;@&amp;role&gt;/&lt;#channel&gt; mentions,
/// @everyone/@here, custom emoji and &lt;t:unix:style&gt; timestamps.
/// </summary>
public static partial class DiscordMarkdown
{
    const int MaxDepth = 12;
    const int MaxJumboEmoji = 30;

    [GeneratedRegex(@"```(?:([A-Za-z0-9_+\-.#]+)\n)?\n?([\s\S]*?)```")]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"\G<@!?(\d{15,21})>")]
    private static partial Regex UserMention();

    [GeneratedRegex(@"\G<@&(\d{15,21})>")]
    private static partial Regex RoleMention();

    [GeneratedRegex(@"\G<#(\d{15,21})>")]
    private static partial Regex ChannelMention();

    [GeneratedRegex(@"\G<(a?):(\w{2,32}):(\d{15,21})>")]
    private static partial Regex CustomEmoji();

    [GeneratedRegex(@"\G<t:(-?\d{1,13})(?::([tTdDfFR]))?>")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"\G</([\w\- ]{1,96}):(\d{15,21})>")]
    private static partial Regex SlashCommand();

    [GeneratedRegex(@"\G<(https?://[^\s<>]+)>")]
    private static partial Regex AngleLink();

    [GeneratedRegex(@"\Ghttps?://[^\s<]+[^\s<.,:;""'\]!?*_~|]")]
    private static partial Regex BareUrl();

    [GeneratedRegex(@"\G\[([^\[\]\n]+)\]\(<?(https?://[^\s)<>]+)>?(?:\s+""[^""]*"")?\)")]
    private static partial Regex MaskedLink();

    [GeneratedRegex(@"<a?:\w{2,32}:\d{15,21}>")]
    private static partial Regex CustomEmojiAnywhere();

    // ------------------------------------------------------------------ entry points

    public static string ToHtml(string? text, IMarkdownContext? context = null, MarkdownOptions? options = null)
    {
        if (string.IsNullOrEmpty(text)) return "";
        context ??= EmptyMarkdownContext.Instance;
        options ??= MarkdownOptions.Message;
        var r = new Renderer(context, options, jumbo: options.AllowJumboEmoji && IsEmojiOnly(text));
        return r.Render(text);
    }

    /// <summary>True when the text is nothing but emoji (custom or unicode) and whitespace, at most 30 of them.</summary>
    public static bool IsEmojiOnly(string text)
    {
        int count = CustomEmojiAnywhere().Count(text);
        var rest = CustomEmojiAnywhere().Replace(text, "");
        var e = StringInfo.GetTextElementEnumerator(rest);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            if (string.IsNullOrWhiteSpace(element)) continue;
            if (!IsEmojiElement(element)) return false;
            count++;
        }
        return count is > 0 and <= MaxJumboEmoji;
    }

    static bool IsEmojiElement(string element)
    {
        var rune = Rune.GetRuneAt(element, 0);
        int v = rune.Value;
        if (v is >= 0x1F000 and <= 0x1FAFF) return true; // pictographs, emoticons, transport, flags (regional indicators)
        if (v is >= 0x2600 and <= 0x27BF) return true;   // misc symbols, dingbats
        if (v is >= 0x2300 and <= 0x23FF) return true;   // ⌚ ⏰ etc.
        if (v is >= 0x2B00 and <= 0x2BFF) return true;   // ⭐ ⬛
        if (v is 0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x3030 or 0x303D or 0x3297 or 0x3299) return true;
        // Keycaps: 1️⃣
        return element.Length > 1 && element.Contains('⃣');
    }

    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 16);
        AppendEscaped(sb, text);
        return sb.ToString();
    }

    static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
    }

    // ------------------------------------------------------------------ renderer

    sealed class Renderer(IMarkdownContext ctx, MarkdownOptions options, bool jumbo)
    {
        readonly StringBuilder _sb = new();

        public string Render(string text)
        {
            text = text.Replace("\r\n", "\n");
            int pos = 0;
            foreach (Match m in CodeBlock().Matches(text))
            {
                if (m.Index > pos) RenderBlocks(text[pos..m.Index]);
                RenderCodeBlock(m.Groups[1].Value, m.Groups[2].Value);
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) RenderBlocks(text[pos..]);
            return _sb.ToString();
        }

        void RenderCodeBlock(string language, string code)
        {
            if (code.EndsWith('\n')) code = code[..^1];
            _sb.Append("<pre class=\"md-codeblock\"><code");
            if (language.Length > 0)
            {
                _sb.Append(" class=\"language-");
                AppendEscaped(_sb, language.ToLowerInvariant());
                _sb.Append('"');
            }
            _sb.Append('>');
            AppendEscaped(_sb, code);
            _sb.Append("</code></pre>");
        }

        // A code block swallows its trailing newline visually; trim one leading/trailing newline around it.
        void RenderBlocks(string text)
        {
            if (!options.Blocks)
            {
                _sb.Append(Inline(text, 0));
                return;
            }

            var lines = text.Split('\n');
            int start = 0, end = lines.Length;
            if (_sb.Length > 0 && lines.Length > 1 && lines[0].Length == 0) start = 1; // right after a code block
            if (end - start > 1 && lines[end - 1].Length == 0) end--;                 // right before a code block

            var paragraph = new List<string>();
            for (int i = start; i < end; i++)
            {
                var line = lines[i];

                if (line.StartsWith(">>> ", StringComparison.Ordinal) || line == ">>>")
                {
                    FlushParagraph(paragraph);
                    var rest = new List<string> { line.Length > 4 ? line[4..] : "" };
                    rest.AddRange(lines[(i + 1)..end]);
                    _sb.Append("<blockquote class=\"md-quote\">").Append(Inline(string.Join('\n', rest), 0, newlines: true)).Append("</blockquote>");
                    return;
                }

                if (line.StartsWith("> ", StringComparison.Ordinal) || line == ">")
                {
                    FlushParagraph(paragraph);
                    var quote = new List<string>();
                    while (i < end && (lines[i].StartsWith("> ", StringComparison.Ordinal) || lines[i] == ">"))
                    {
                        quote.Add(lines[i].Length > 2 ? lines[i][2..] : "");
                        i++;
                    }
                    i--;
                    _sb.Append("<blockquote class=\"md-quote\">").Append(Inline(string.Join('\n', quote), 0, newlines: true)).Append("</blockquote>");
                    continue;
                }

                if (HeadingLevel(line) is int level)
                {
                    FlushParagraph(paragraph);
                    _sb.Append($"<h{level} class=\"md-h\">").Append(Inline(line[(level + 1)..], 0)).Append($"</h{level}>");
                    continue;
                }

                if (line.StartsWith("-# ", StringComparison.Ordinal))
                {
                    FlushParagraph(paragraph);
                    _sb.Append("<div class=\"md-subtext\">").Append(Inline(line[3..], 0)).Append("</div>");
                    continue;
                }

                if (ListItem(line) is { } first)
                {
                    FlushParagraph(paragraph);
                    var items = new List<(int Indent, bool Ordered, int Number, string Text)> { first };
                    while (i + 1 < end && ListItem(lines[i + 1]) is { } next)
                    {
                        items.Add(next);
                        i++;
                    }
                    RenderList(items);
                    continue;
                }

                paragraph.Add(line);
            }
            FlushParagraph(paragraph);
        }

        void FlushParagraph(List<string> lines)
        {
            if (lines.Count == 0) return;
            _sb.Append(Inline(string.Join('\n', lines), 0, newlines: true));
            lines.Clear();
            // Separate the paragraph from a following block element the way Discord's line break would.
        }

        static int? HeadingLevel(string line)
        {
            int n = 0;
            while (n < line.Length && n < 4 && line[n] == '#') n++;
            return n is >= 1 and <= 3 && line.Length > n + 1 && line[n] == ' ' && !string.IsNullOrWhiteSpace(line[(n + 1)..]) ? n : null;
        }

        static (int Indent, bool Ordered, int Number, string Text)? ListItem(string line)
        {
            int spaces = 0;
            while (spaces < line.Length && line[spaces] == ' ') spaces++;
            var rest = line.AsSpan(spaces);
            if (rest.Length > 2 && (rest[0] == '-' || rest[0] == '*') && rest[1] == ' ' && !rest[2..].IsWhiteSpace())
                return (Math.Min(spaces / 2, 3), false, 0, rest[2..].ToString());
            int digits = 0;
            while (digits < rest.Length && digits < 9 && char.IsAsciiDigit(rest[digits])) digits++;
            if (digits > 0 && rest.Length > digits + 2 && rest[digits] == '.' && rest[digits + 1] == ' ')
                return (Math.Min(spaces / 2, 3), true, int.Parse(rest[..digits], CultureInfo.InvariantCulture), rest[(digits + 2)..].ToString());
            return null;
        }

        void RenderList(List<(int Indent, bool Ordered, int Number, string Text)> items)
        {
            bool ordered = items[0].Ordered;
            _sb.Append(ordered ? $"<ol class=\"md-list\" start=\"{items[0].Number}\">" : "<ul class=\"md-list\">");
            foreach (var item in items)
            {
                _sb.Append(item.Indent > 0 ? $"<li class=\"md-li-{item.Indent}\">" : "<li>");
                _sb.Append(Inline(item.Text, 0)).Append("</li>");
            }
            _sb.Append(ordered ? "</ol>" : "</ul>");
        }

        // ------------------------------------------------------------------ inline

        string Inline(string s, int depth, bool newlines = true, bool allowLinks = true)
        {
            var sb = new StringBuilder(s.Length + 16);
            if (depth > MaxDepth)
            {
                AppendEscaped(sb, s);
                return sb.ToString();
            }

            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\' when i + 1 < s.Length && IsEscapable(s[i + 1]):
                        AppendEscaped(sb, s.AsSpan(i + 1, 1));
                        i += 2;
                        continue;

                    case '\n':
                        sb.Append(newlines ? "<br>" : " ");
                        i++;
                        continue;

                    case '`':
                        if (TryInlineCode(s, ref i, sb)) continue;
                        break;

                    case '<':
                        if (TryAngle(s, ref i, sb, allowLinks)) continue;
                        break;

                    case '|' when At(s, i, "||"):
                        if (TryWrap(s, ref i, sb, "||", "<span class=\"md-spoiler\" tabindex=\"0\">", "</span>", depth, allowLinks)) continue;
                        break;

                    case '*' when At(s, i, "**"):
                        if (TryWrap(s, ref i, sb, "**", "<strong>", "</strong>", depth, allowLinks)) continue;
                        break;

                    case '_' when At(s, i, "__"):
                        if (TryWrap(s, ref i, sb, "__", "<u>", "</u>", depth, allowLinks)) continue;
                        break;

                    case '~' when At(s, i, "~~"):
                        if (TryWrap(s, ref i, sb, "~~", "<s>", "</s>", depth, allowLinks)) continue;
                        break;

                    case '*':
                        if (TryItalic(s, ref i, sb, '*', depth, allowLinks)) continue;
                        break;

                    case '_':
                        if (TryItalic(s, ref i, sb, '_', depth, allowLinks)) continue;
                        break;

                    case '[' when allowLinks && options.MaskedLinks:
                        if (TryMaskedLink(s, ref i, sb, depth)) continue;
                        break;

                    case 'h' when allowLinks:
                        if (TryBareUrl(s, ref i, sb)) continue;
                        break;

                    case '@':
                        if (At(s, i, "@everyone") || At(s, i, "@here"))
                        {
                            var word = At(s, i, "@everyone") ? "@everyone" : "@here";
                            sb.Append("<span class=\"md-mention md-mention-everyone\">").Append(word).Append("</span>");
                            i += word.Length;
                            continue;
                        }
                        break;
                }

                AppendEscaped(sb, s.AsSpan(i, 1));
                i++;
            }
            return sb.ToString();
        }

        static bool At(string s, int i, string token) => string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

        static bool IsEscapable(char c) => c is '*' or '_' or '~' or '`' or '|' or '\\' or '<' or '>' or '#' or '-' or '[' or ']' or '(' or ')' or '@' or ':' or '.';

        bool TryInlineCode(string s, ref int i, StringBuilder sb)
        {
            int run = 0;
            while (i + run < s.Length && s[i + run] == '`') run++;
            if (run > 2) return false;
            var delim = new string('`', run);
            int close = s.IndexOf(delim, i + run, StringComparison.Ordinal);
            // A double-backtick span may contain single backticks; a single one must not end inside a longer run.
            while (close >= 0 && run == 1 && close + 1 < s.Length && s[close + 1] == '`')
                close = s.IndexOf(delim, close + 2, StringComparison.Ordinal);
            if (close < 0 || close == i + run) return false;
            var code = s[(i + run)..close];
            if (run == 2 && code.Length > 1 && code[0] == ' ' && code[^1] == ' ') code = code[1..^1];
            sb.Append("<code class=\"md-code\">");
            AppendEscaped(sb, code);
            sb.Append("</code>");
            i = close + run;
            return true;
        }

        bool TryWrap(string s, ref int i, StringBuilder sb, string delim, string open, string close, int depth, bool allowLinks)
        {
            int start = i + delim.Length;
            int j = s.IndexOf(delim, start, StringComparison.Ordinal);
            if (j < 0) return false;
            // ***x*** → the closing ** is the last two of the run, so the inner * pair becomes italic.
            while (j + delim.Length < s.Length && s[j + delim.Length] == delim[0]) j++;
            if (j <= start) return false;
            sb.Append(open).Append(Inline(s[start..j], depth + 1, true, allowLinks)).Append(close);
            i = j + delim.Length;
            return true;
        }

        bool TryItalic(string s, ref int i, StringBuilder sb, char marker, int depth, bool allowLinks)
        {
            int start = i + 1;
            if (start >= s.Length || char.IsWhiteSpace(s[start])) return false;
            // _italic_ only at word boundaries, so snake_case_names stay as typed.
            if (marker == '_' && i > 0 && char.IsLetterOrDigit(s[i - 1])) return false;

            int j = start;
            while (true)
            {
                j = s.IndexOf(marker, j);
                if (j < 0) return false;
                bool doubled = j + 1 < s.Length && s[j + 1] == marker;
                if (marker == '*' && doubled)
                {
                    // Skip over a nested **bold** span inside the italic.
                    int closeBold = s.IndexOf("**", j + 2, StringComparison.Ordinal);
                    if (closeBold < 0) return false;
                    j = closeBold + 2;
                    continue;
                }
                if (marker == '_' && doubled) { j += 2; continue; }
                if (j > start && !char.IsWhiteSpace(s[j - 1]) && (marker == '*' || j + 1 >= s.Length || !char.IsLetterOrDigit(s[j + 1])))
                    break;
                j++;
            }
            sb.Append("<em>").Append(Inline(s[start..j], depth + 1, true, allowLinks)).Append("</em>");
            i = j + 1;
            return true;
        }

        bool TryAngle(string s, ref int i, StringBuilder sb, bool allowLinks)
        {
            Match m;
            if ((m = UserMention().Match(s, i)).Success)
            {
                var id = ulong.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                var name = ctx.UserName(id);
                sb.Append(id == ctx.SelfUserId ? "<span class=\"md-mention md-mention-self\" data-user-id=\"" : "<span class=\"md-mention\" data-user-id=\"")
                  .Append(id).Append("\">@");
                AppendEscaped(sb, name ?? "unknown-user");
                sb.Append("</span>");
            }
            else if ((m = RoleMention().Match(s, i)).Success)
            {
                var id = ulong.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                var role = ctx.Role(id);
                sb.Append("<span class=\"md-mention md-mention-role\"");
                if (role?.Color is { } color && IsHexColor(color))
                    sb.Append(" style=\"color:").Append(color).Append(";background-color:").Append(color).Append("26\"");
                sb.Append(">@");
                AppendEscaped(sb, role?.Name ?? "unknown-role");
                sb.Append("</span>");
            }
            else if ((m = ChannelMention().Match(s, i)).Success)
            {
                var id = ulong.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
                sb.Append("<span class=\"md-mention md-mention-channel\" data-channel-id=\"").Append(id).Append("\">#");
                AppendEscaped(sb, ctx.ChannelName(id) ?? "unknown-channel");
                sb.Append("</span>");
            }
            else if ((m = CustomEmoji().Match(s, i)).Success)
            {
                var emoji = new EmojiRef(ulong.Parse(m.Groups[3].ValueSpan, CultureInfo.InvariantCulture), m.Groups[2].Value, m.Groups[1].Value == "a");
                sb.Append("<img class=\"md-emoji").Append(jumbo ? " md-emoji-jumbo" : "").Append("\" src=\"")
                  .Append(emoji.ImageUrl(jumbo ? 96 : 48)).Append("\" alt=\":");
                AppendEscaped(sb, emoji.Name);
                sb.Append(":\" title=\":");
                AppendEscaped(sb, emoji.Name);
                sb.Append(":\" draggable=\"false\" loading=\"lazy\">");
            }
            else if ((m = Timestamp().Match(s, i)).Success)
            {
                if (!long.TryParse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var unix) || unix is < -62135596800 or > 253402300799)
                    return false;
                char style = m.Groups[2].Success ? m.Groups[2].Value[0] : 'f';
                sb.Append("<span class=\"md-timestamp\" title=\"");
                AppendEscaped(sb, ctx.FormatTimestampTooltip(unix));
                sb.Append("\">");
                AppendEscaped(sb, ctx.FormatTimestamp(unix, style));
                sb.Append("</span>");
            }
            else if ((m = SlashCommand().Match(s, i)).Success)
            {
                sb.Append("<span class=\"md-mention md-mention-command\">/");
                AppendEscaped(sb, m.Groups[1].Value);
                sb.Append("</span>");
            }
            else if (allowLinks && (m = AngleLink().Match(s, i)).Success)
            {
                AppendLink(sb, m.Groups[1].Value, Escape(m.Groups[1].Value));
            }
            else return false;

            i += m.Length;
            return true;
        }

        bool TryMaskedLink(string s, ref int i, StringBuilder sb, int depth)
        {
            var m = MaskedLink().Match(s, i);
            if (!m.Success) return false;
            AppendLink(sb, m.Groups[2].Value, Inline(m.Groups[1].Value, depth + 1, false, allowLinks: false));
            i += m.Length;
            return true;
        }

        bool TryBareUrl(string s, ref int i, StringBuilder sb)
        {
            if (i > 0 && char.IsLetterOrDigit(s[i - 1])) return false;
            var m = BareUrl().Match(s, i);
            if (!m.Success) return false;
            var url = m.Value;
            // Keep a trailing ')' only if the URL has a matching '(' (Wikipedia-style links).
            while (url.EndsWith(')') && url.Count(ch => ch == '(') < url.Count(ch => ch == ')')) url = url[..^1];
            AppendLink(sb, url, Escape(url));
            i += url.Length;
            return true;
        }

        static void AppendLink(StringBuilder sb, string url, string innerHtml)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                sb.Append(innerHtml);
                return;
            }
            sb.Append("<a class=\"md-link\" href=\"");
            AppendEscaped(sb, url);
            sb.Append("\" title=\"");
            AppendEscaped(sb, url);
            sb.Append("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">").Append(innerHtml).Append("</a>");
        }

        static bool IsHexColor(string color) =>
            color.Length == 7 && color[0] == '#' && color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF") == false;
    }
}
