using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Abstractions;

public sealed record WebhookPayload
{
    public required string Content { get; init; }
    public string? Username { get; init; }
    public string? AvatarUrl { get; init; }
    public AllowedMentions AllowedMentions { get; init; } = AllowedMentions.None;
}

/// <summary>Executes, edits and deletes webhook messages. Webhook calls authenticate with the webhook token, not the bot token.</summary>
public interface IWebhookExecutor
{
    /// <returns>The id of the created message.</returns>
    Task<ulong> ExecuteAsync(WebhookInfo webhook, WebhookPayload payload, IReadOnlyList<OutgoingFile> files, CancellationToken cancellationToken = default);

    Task EditAsync(WebhookInfo webhook, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default);

    Task DeleteAsync(WebhookInfo webhook, ulong messageId, CancellationToken cancellationToken = default);
}
