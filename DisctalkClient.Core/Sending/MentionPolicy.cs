using System.Text.RegularExpressions;
using DisctalkClient.Core.Abstractions;

namespace DisctalkClient.Core.Sending;

/// <summary>
/// Decides who an outgoing message may ping. Users written as &lt;@id&gt; are pinged explicitly (Discord rejects
/// <c>parse: ["users"]</c> together with a <c>users</c> list), role mentions are allowed, @everyone/@here only when
/// the setting says so.
/// </summary>
public static partial class MentionPolicy
{
    [GeneratedRegex(@"<@!?(\d{15,21})>")]
    private static partial Regex UserMention();

    public static IReadOnlyList<ulong> ExtractUserMentions(string content) =>
        UserMention().Matches(content)
            .Select(m => ulong.Parse(m.Groups[1].ValueSpan))
            .Distinct()
            .ToList();

    public static AllowedMentions For(string content, bool allowEveryone, bool pingRepliedUser, IEnumerable<ulong>? exclude = null)
    {
        var excluded = exclude?.ToHashSet() ?? [];
        return new AllowedMentions
        {
            Users = ExtractUserMentions(content).Where(id => !excluded.Contains(id)).Take(100).ToList(),
            AllRoles = true,
            Everyone = allowEveryone,
            RepliedUser = pingRepliedUser,
        };
    }
}
