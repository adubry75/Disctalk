using DisctalkClient.Core.Models;

namespace DisctalkClient.UI.Services;

public static class MessageGrouping
{
    public static readonly TimeSpan GroupWindow = TimeSpan.FromMinutes(7);

    /// <summary>
    /// Whether <paramref name="current"/> continues <paramref name="previous"/>'s group (no avatar/name header),
    /// like Discord: same author (and same webhook persona), within 7 minutes, not a reply or system message.
    /// </summary>
    public static bool ContinuesGroup(ChatMessage? previous, ChatMessage current)
    {
        if (previous is null) return false;
        if (current.Kind != ChatMessageKind.Default || previous.Kind == ChatMessageKind.System) return false;
        if (previous.Author.Id != current.Author.Id) return false;
        if (current.Author.IsWebhook && (previous.Author.DisplayName != current.Author.DisplayName || previous.Author.AvatarUrl != current.Author.AvatarUrl)) return false;
        return current.Timestamp - previous.Timestamp < GroupWindow;
    }

    public static bool StartsNewDay(ChatMessage? previous, ChatMessage current, TimeZoneInfo zone) =>
        previous is null || TimeZoneInfo.ConvertTime(previous.Timestamp, zone).Date != TimeZoneInfo.ConvertTime(current.Timestamp, zone).Date;
}
