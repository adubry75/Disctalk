using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.State;

public sealed record ChannelGroup(ChannelInfo? Category, IReadOnlyList<ChannelInfo> Channels);

public static class ChannelTree
{
    /// <summary>
    /// Discord's sidebar order: uncategorized channels first, then categories by position; inside each group
    /// text-like channels come before voice, each by position then id.
    /// </summary>
    public static IReadOnlyList<ChannelGroup> Build(IEnumerable<ChannelInfo> channels)
    {
        var all = channels.ToList();
        var categories = all.Where(c => c.Kind == ChannelKind.Category).OrderBy(c => c.Position).ThenBy(c => c.Id).ToList();
        var categoryIds = categories.Select(c => c.Id).ToHashSet();
        var children = all.Where(c => c.Kind != ChannelKind.Category).ToLookup(c => c.ParentId is ulong p && categoryIds.Contains(p) ? p : (ulong?)null);

        var groups = new List<ChannelGroup>();
        var loose = Order(children[null]);
        if (loose.Count > 0) groups.Add(new ChannelGroup(null, loose));
        foreach (var category in categories) groups.Add(new ChannelGroup(category, Order(children[category.Id])));
        return groups;
    }

    static List<ChannelInfo> Order(IEnumerable<ChannelInfo> channels) => channels
        .OrderBy(c => c.Kind == ChannelKind.Voice ? 1 : 0)
        .ThenBy(c => c.Position)
        .ThenBy(c => c.Id)
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
