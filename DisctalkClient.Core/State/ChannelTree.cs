using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.State;

public sealed record ChannelGroup(ChannelInfo? Category, IReadOnlyList<ChannelInfo> Channels);

public static class ChannelTree
{
    /// <summary>
    /// Discord's sidebar order: uncategorized channels first, then categories by position; inside each group
    /// text-like channels come before voice, each by position then id. Active threads follow their parent channel
    /// (most recently active first). Direct messages are one group, most recent conversation first.
    /// </summary>
    public static IReadOnlyList<ChannelGroup> Build(IEnumerable<ChannelInfo> channels)
    {
        var all = channels.ToList();
        if (all.Count > 0 && all.All(c => c.IsPrivate))
            return [new ChannelGroup(null, all.OrderByDescending(c => c.LastMessageId ?? c.Id).ThenByDescending(c => c.Id).ToList())];

        var threads = all.Where(c => c.Kind == ChannelKind.Thread).ToList();
        var listed = all.Where(c => c.Kind != ChannelKind.Thread).ToList();
        var listedIds = listed.Select(c => c.Id).ToHashSet();
        var threadsByParent = threads.Where(t => t.ParentId is ulong p && listedIds.Contains(p)).ToLookup(t => t.ParentId!.Value);
        // A thread whose parent we can't see is shown on its own rather than dropped.
        listed.AddRange(threads.Where(t => t.ParentId is not ulong p || !listedIds.Contains(p)));

        var categories = listed.Where(c => c.Kind == ChannelKind.Category).OrderBy(c => c.Position).ThenBy(c => c.Id).ToList();
        var categoryIds = categories.Select(c => c.Id).ToHashSet();
        var children = listed.Where(c => c.Kind != ChannelKind.Category).ToLookup(c => c.ParentId is ulong p && categoryIds.Contains(p) ? p : (ulong?)null);

        var groups = new List<ChannelGroup>();
        var loose = Order(children[null], threadsByParent);
        if (loose.Count > 0) groups.Add(new ChannelGroup(null, loose));
        foreach (var category in categories) groups.Add(new ChannelGroup(category, Order(children[category.Id], threadsByParent)));
        return groups;
    }

    static List<ChannelInfo> Order(IEnumerable<ChannelInfo> channels, ILookup<ulong, ChannelInfo> threadsByParent) => channels
        .OrderBy(c => c.Kind == ChannelKind.Voice ? 1 : 0)
        .ThenBy(c => c.Position)
        .ThenBy(c => c.Id)
        .SelectMany(c => threadsByParent[c.Id].OrderByDescending(t => t.LastMessageId ?? t.Id).Prepend(c))
        .ToList();
}

public sealed record MemberGroup(string Title, string? Color, IReadOnlyList<MemberInfo> Members);

public static class MemberGrouping
{
    /// <summary>Members grouped under their highest hoisted role (Discord's "display separately"), the rest under "Members".</summary>
    public static IReadOnlyList<MemberGroup> Group(IEnumerable<MemberInfo> members, IReadOnlyDictionary<ulong, RoleInfo> roles)
    {
        var buckets = new Dictionary<ulong, List<MemberInfo>>();
        var rest = new List<MemberInfo>();
        foreach (var m in members)
        {
            var top = m.RoleIds.Select(id => roles.GetValueOrDefault(id)).Where(r => r is { Hoist: true })
                .OrderByDescending(r => r!.Position).FirstOrDefault();
            if (top is null) rest.Add(m);
            else (buckets.TryGetValue(top.Id, out var list) ? list : buckets[top.Id] = new()).Add(m);
        }

        var groups = buckets
            .Select(kv => roles[kv.Key])
            .OrderByDescending(r => r.Position)
            .Select(r => new MemberGroup(r.Name, r.CssColor, Sort(buckets[r.Id])))
            .ToList();
        if (rest.Count > 0) groups.Add(new MemberGroup("Members", null, Sort(rest)));
        return groups;
    }

    /// <summary>CSS color of the member's highest colored role, or null.</summary>
    public static string? NameColor(MemberInfo? member, IReadOnlyDictionary<ulong, RoleInfo> roles) =>
        member?.RoleIds.Select(id => roles.GetValueOrDefault(id)).Where(r => r?.CssColor is not null)
            .OrderByDescending(r => r!.Position).FirstOrDefault()?.CssColor;

    static List<MemberInfo> Sort(List<MemberInfo> members) =>
        members.OrderBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
}
