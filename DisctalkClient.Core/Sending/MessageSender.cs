using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Settings;

namespace DisctalkClient.Core.Sending;

public sealed record OutgoingMessage
{
    public required ulong ChannelId { get; init; }
    public string Content { get; init; } = "";
    public ChatMessage? ReplyTo { get; init; }
    public IReadOnlyList<OutgoingFile> Files { get; init; } = [];
    /// <summary>Overrides the send mode from settings for this one message.</summary>
    public SendMode? Mode { get; init; }
}

public sealed class SendException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Routes sends, edits and deletes either through the bot user or through the channel webhook, based on the
/// send-mode setting (or the message's own author for edits/deletes).
/// </summary>
public sealed class MessageSender(IBotRestApi rest, WebhookDirectory webhooks, IWebhookExecutor webhookExecutor, SettingsService settings, Func<ulong?> botUserId)
{
    public const int MaxContentLength = 2000;
    public const int MaxFiles = 10;

    /// <returns>The message, when the route returns one (bot sends). Webhook sends return null; the gateway delivers them.</returns>
    public async Task<ChatMessage?> SendAsync(OutgoingMessage message, CancellationToken cancellationToken = default)
    {
        var s = settings.Current;
        var content = message.Content.TrimEnd();
        if (content.Length == 0 && message.Files.Count == 0) throw new SendException("Nothing to send.");
        if (message.Files.Count > MaxFiles) throw new SendException($"You can attach at most {MaxFiles} files.");

        var mode = message.Mode ?? s.SendMode;
        if (mode == SendMode.Webhook) return await SendViaWebhookAsync(message, content, s, cancellationToken).ConfigureAwait(false);

        if (content.Length > MaxContentLength) throw new SendException($"Message is {content.Length} characters; Discord allows {MaxContentLength}.");
        return await rest.SendMessageAsync(new BotMessageRequest
        {
            ChannelId = message.ChannelId,
            Content = content,
            ReplyToMessageId = message.ReplyTo?.Id,
            AllowedMentions = MentionPolicy.For(content, s.AllowEveryoneMentions, pingRepliedUser: s.PingOnReply),
            Files = message.Files,
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task<ChatMessage?> SendViaWebhookAsync(OutgoingMessage message, string content, ClientSettings s, CancellationToken ct)
    {
        var finalContent = content;
        IEnumerable<ulong>? exclude = null;
        if (message.ReplyTo is { } replyTo)
        {
            finalContent = WebhookReplyFormatter.Format(content, replyTo);
            if (!s.PingOnReply) exclude = [replyTo.Author.Id];
        }
        if (finalContent.Length > MaxContentLength)
            throw new SendException($"Message is {finalContent.Length} characters (including the reply header); Discord allows {MaxContentLength}.");

        WebhookInfo hook;
        try
        {
            hook = await webhooks.GetOrCreateAsync(message.ChannelId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SendException("Couldn't get a webhook for this channel. Does the bot have the Manage Webhooks permission?", ex);
        }

        var payload = new WebhookPayload
        {
            Content = finalContent,
            Username = string.IsNullOrWhiteSpace(s.WebhookDisplayName) ? null : s.WebhookDisplayName.Trim(),
            AvatarUrl = string.IsNullOrWhiteSpace(s.WebhookAvatarUrl) ? null : s.WebhookAvatarUrl.Trim(),
            AllowedMentions = MentionPolicy.For(finalContent, s.AllowEveryoneMentions, pingRepliedUser: false, exclude),
        };
        await webhookExecutor.ExecuteAsync(hook, payload, message.Files, ct).ConfigureAwait(false);
        return null;
    }

    public bool CanEdit(ChatMessage message) =>
        message.WebhookId is not null ? webhooks.IsOwnWebhook(message.WebhookId) : message.Author.Id == botUserId();

    public async Task<ChatMessage?> EditAsync(ChatMessage message, string newContent, CancellationToken cancellationToken = default)
    {
        newContent = newContent.TrimEnd();
        if (newContent.Length == 0 && message.Attachments.Count == 0) throw new SendException("A message can't be empty. Delete it instead.");
        if (newContent.Length > MaxContentLength) throw new SendException($"Message is {newContent.Length} characters; Discord allows {MaxContentLength}.");
        var s = settings.Current;

        if (message.WebhookId is not null)
        {
            var hook = await webhooks.FindAsync(message.ChannelId, cancellationToken).ConfigureAwait(false);
            if (hook is null || hook.Id != message.WebhookId) throw new SendException("Only messages sent through this client's webhook can be edited.");
            await webhookExecutor.EditAsync(hook, message.Id, newContent,
                MentionPolicy.For(newContent, s.AllowEveryoneMentions, pingRepliedUser: false), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (message.Author.Id != botUserId()) throw new SendException("The bot can only edit its own messages.");
        return await rest.EditMessageAsync(message.ChannelId, message.Id, newContent,
            MentionPolicy.For(newContent, s.AllowEveryoneMentions, pingRepliedUser: s.PingOnReply), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        if (message.WebhookId is not null)
        {
            var hook = await webhooks.FindAsync(message.ChannelId, cancellationToken).ConfigureAwait(false);
            if (hook is not null && hook.Id == message.WebhookId)
            {
                await webhookExecutor.DeleteAsync(hook, message.Id, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        // Own messages, or someone else's with Manage Messages.
        await rest.DeleteMessageAsync(message.ChannelId, message.Id, cancellationToken).ConfigureAwait(false);
    }
}
