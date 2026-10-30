using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Abstractions;

/// <summary>Mentions Discord is allowed to ping for an outgoing message (maps to <c>allowed_mentions</c>).</summary>
public sealed record AllowedMentions
{
    public bool Everyone { get; init; }
    public bool AllRoles { get; init; }
    public IReadOnlyList<ulong> Users { get; init; } = [];
    public bool RepliedUser { get; init; }

    public static AllowedMentions None { get; } = new();
}

public sealed record SendMessageRequest
{
    public required ulong ChannelId { get; init; }
    public required string Content { get; init; }
    public ulong? ReplyToMessageId { get; init; }
    public AllowedMentions AllowedMentions { get; init; } = AllowedMentions.None;
    public IReadOnlyList<OutgoingFile> Files { get; init; } = [];
}

/// <summary>The slice of Discord's REST API the client uses with the bot token.</summary>
public interface IDiscordRestApi
{
    Task<IReadOnlyList<ChatMessage>> GetMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit, CancellationToken cancellationToken = default);

    Task<ChatMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    Task<ChatMessage> EditMessageAsync(ulong channelId, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default);

    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default);

    Task AddReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default);

    Task RemoveOwnReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default);

    Task TriggerTypingAsync(ulong channelId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WebhookInfo>> GetChannelWebhooksAsync(ulong channelId, CancellationToken cancellationToken = default);

    Task<WebhookInfo> CreateWebhookAsync(ulong channelId, string name, CancellationToken cancellationToken = default);
}
