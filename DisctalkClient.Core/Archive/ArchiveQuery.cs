using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DisctalkClient.Core.Archive;

/// <summary>
/// A search over the archiver's database, parsed from Discord-style text:
/// <c>cat pictures "exact phrase" from:ada in:general after:2025-01-01 before:2025-06-01 has:link</c>.
/// </summary>
public sealed record ArchiveQuery
{
    /// <summary>Every term must appear in the message (case-insensitive substring; quoted phrases stay together).</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];
    /// <summary>Username, display name or nickname (prefix match), or a numeric user id.</summary>
    public string? From { get; init; }
    /// <summary>Channel name (exact, without #) or numeric channel id.</summary>
    public string? In { get; init; }
    /// <summary>Inclusive lower bound (UTC). Date-only values mean midnight UTC.</summary>
    public DateTimeOffset? After { get; init; }
    /// <summary>Exclusive upper bound (UTC).</summary>
    public DateTimeOffset? Before { get; init; }
    public bool HasLink { get; init; }

    public bool IsEmpty => Terms.Count == 0 && From is null && In is null && After is null && Before is null && !HasLink;
}

public static partial class ArchiveQueryParser
{
    [GeneratedRegex(@"(?<key>from|in|after|before|during|has):(?:""(?<qv>[^""]*)""|(?<v>\S+))|""(?<phrase>[^""]+)""|(?<word>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    public static ArchiveQuery Parse(string? text)
    {
        var terms = new List<string>();
        string? from = null, @in = null;
        DateTimeOffset? after = null, before = null;
        bool hasLink = false;

        foreach (Match m in TokenRegex().Matches(text ?? ""))
        {
            if (m.Groups["key"].Success)
            {
                var value = m.Groups["qv"].Success ? m.Groups["qv"].Value : m.Groups["v"].Value;
                switch (m.Groups["key"].Value.ToLowerInvariant())
                {
                    case "from": from = value.TrimStart('@'); break;
                    case "in": @in = value.TrimStart('#'); break;
                    case "after" when ParseDate(value) is { } d: after = d; break;
                    case "before" when ParseDate(value) is { } d: before = d; break;
                    case "during" when ParseDate(value) is { } d: after = d; before = d.AddDays(1); break;
                    case "has" when value.Equals("link", StringComparison.OrdinalIgnoreCase): hasLink = true; break;
                    default: terms.Add(m.Value); break; // not a filter we understand: search for it literally
                }
            }
            else if (m.Groups["phrase"].Success) terms.Add(m.Groups["phrase"].Value);
            else terms.Add(m.Groups["word"].Value);
        }

        return new ArchiveQuery
        {
            Terms = terms.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            From = string.IsNullOrWhiteSpace(from) ? null : from,
            In = string.IsNullOrWhiteSpace(@in) ? null : @in,
            After = after,
            Before = before,
            HasLink = hasLink,
        };
    }

    static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}

/// <summary>A parameterized SQL statement. Values never get concatenated into the SQL text.</summary>
public sealed record SqlCommandText(string Sql, IReadOnlyList<KeyValuePair<string, object>> Parameters);

/// <summary>
/// Builds the search SELECT against the archiver's schema (<c>messages</c>, <c>channels</c>, <c>usersServerInfo</c>).
/// Date filters become snowflake ranges on <c>messageId</c>: exact UTC regardless of how the archiver's MySQL session
/// stored <c>timestamp</c>, and they use the primary-key index.
/// </summary>
public static class ArchiveSql
{
    public const int MaxLimit = 200;

    public static SqlCommandText Build(ArchiveQuery query, ulong? serverId = null, ulong? beforeMessageId = null, int limit = 50)
    {
        var sql = new StringBuilder("""
            SELECT m.messageId, m.channelId, m.authorId, m.authorUsername, m.content, m.json, c.name AS channelName, c.serverId
            FROM messages m
            LEFT JOIN channels c ON c.channelId = m.channelId
            WHERE 1 = 1
            """);
        var p = new List<KeyValuePair<string, object>>();
        string Add(object value)
        {
            var name = "@p" + p.Count.ToString(CultureInfo.InvariantCulture);
            p.Add(new(name, value));
            return name;
        }

        foreach (var term in query.Terms) sql.Append("\n  AND m.content LIKE ").Append(Add(Contains(term))).Append(@" ESCAPE '\\'");
        if (query.HasLink) sql.Append("\n  AND (m.content LIKE '%http://%' OR m.content LIKE '%https://%')");

        if (query.In is { } channel)
        {
            if (ulong.TryParse(channel, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId)) sql.Append("\n  AND m.channelId = ").Append(Add(channelId));
            else sql.Append("\n  AND c.name = ").Append(Add(channel));
        }

        if (query.From is { } from)
        {
            if (ulong.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)) sql.Append("\n  AND m.authorId = ").Append(Add(userId));
            else
            {
                var prefix = Add(StartsWith(from));
                sql.Append($"\n  AND (m.authorUsername LIKE {prefix} ESCAPE '\\\\' OR m.authorId IN (SELECT us.userId FROM usersServerInfo us WHERE us.nick LIKE {prefix} ESCAPE '\\\\' OR us.globalName LIKE {prefix} ESCAPE '\\\\'))");
            }
        }

        if (serverId is ulong server) sql.Append("\n  AND c.serverId = ").Append(Add(server));
        if (query.After is { } after) sql.Append("\n  AND m.messageId >= ").Append(Add(Models.Snowflakes.FromTimestamp(after)));
        if (query.Before is { } before) sql.Append("\n  AND m.messageId < ").Append(Add(Models.Snowflakes.FromTimestamp(before)));
        if (beforeMessageId is ulong cursor) sql.Append("\n  AND m.messageId < ").Append(Add(cursor));

        sql.Append("\nORDER BY m.messageId DESC\nLIMIT ").Append(Math.Clamp(limit, 1, MaxLimit).ToString(CultureInfo.InvariantCulture));
        return new SqlCommandText(sql.ToString(), p);
    }

    /// <summary>LIKE pattern for "contains", with %, _ and \ in the user's text matched literally.</summary>
    public static string Contains(string text) => "%" + EscapeLike(text) + "%";

    public static string StartsWith(string text) => EscapeLike(text) + "%";

    public static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
