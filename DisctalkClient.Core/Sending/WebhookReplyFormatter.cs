using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Sending;

/// <summary>
/// Webhooks can't create real replies (no message_reference), so a reply sent in webhook mode gets a small
/// subtext header that mentions the original author and links to the original message.
/// </summary>
public static class WebhookReplyFormatter
{
    public const int MaxPreviewLength = 60;

    public static string Format(string content, ChatMessage replyTo)
    {
        string who = replyTo.Author.IsWebhook ? $"**{Escape(replyTo.Author.DisplayName)}**" : $"<@{replyTo.Author.Id}>";
        string link = replyTo.GuildId is ulong guildId
            ? $"https://discord.com/channels/{guildId}/{replyTo.ChannelId}/{replyTo.Id}"
            : $"https://discord.com/channels/@me/{replyTo.ChannelId}/{replyTo.Id}";
        return $"-# ↪ Replying to {who} · [jump]({link})\n{content}";
    }

    static string Escape(string text) => text.Replace("*", "\\*").Replace("_", "\\_").Replace("~", "\\~").Replace("`", "\\`");
}
