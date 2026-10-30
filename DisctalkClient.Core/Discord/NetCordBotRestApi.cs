using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using NetCord.Rest;

namespace DisctalkClient.Core.Discord;

internal sealed class NetCordBotRestApi(RestClient rest) : IDiscordRestApi
{
    public async Task<IReadOnlyList<ChatMessage>> GetMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit, CancellationToken cancellationToken = default)
    {
        var pagination = new PaginationProperties<ulong>
        {
            From = beforeMessageId,
            Direction = PaginationDirection.Before,
            BatchSize = Math.Clamp(limit, 1, 100),
        };
        var result = new List<ChatMessage>(limit);
        await foreach (var message in rest.GetMessagesAsync(channelId, pagination).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            result.Add(NetCordMapper.ToMessage(message));
            if (result.Count >= limit) break;
        }
        return result;
    }

    public async Task<ChatMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var properties = new MessageProperties
        {
            Content = request.Content,
            AllowedMentions = ToProperties(request.AllowedMentions),
            MessageReference = request.ReplyToMessageId is ulong replyId ? MessageReferenceProperties.Reply(replyId, failIfNotExists: false) : null,
            Attachments = request.Files.Count == 0 ? null : request.Files.Select(f => new AttachmentProperties(f.FileName, new MemoryStream(f.Content.ToArray()))).ToList(),
        };
        var sent = await rest.SendMessageAsync(request.ChannelId, properties, cancellationToken: cancellationToken).ConfigureAwait(false);
        return NetCordMapper.ToMessage(sent);
    }

    public async Task<ChatMessage> EditMessageAsync(ulong channelId, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default)
    {
        var edited = await rest.ModifyMessageAsync(channelId, messageId, o =>
        {
            o.Content = content;
            o.AllowedMentions = ToProperties(allowedMentions);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return NetCordMapper.ToMessage(edited);
    }

    public Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default) =>
        rest.DeleteMessageAsync(channelId, messageId, cancellationToken: cancellationToken);

    public Task AddReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default) =>
        rest.AddMessageReactionAsync(channelId, messageId, ToProperties(emoji), cancellationToken: cancellationToken);

    public Task RemoveOwnReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default) =>
        rest.DeleteCurrentUserMessageReactionAsync(channelId, messageId, ToProperties(emoji), cancellationToken: cancellationToken);

    public Task TriggerTypingAsync(ulong channelId, CancellationToken cancellationToken = default) =>
        rest.TriggerTypingAsync(channelId, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<WebhookInfo>> GetChannelWebhooksAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        var hooks = await rest.GetChannelWebhooksAsync(channelId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return hooks.Select(h => ToInfo(h, channelId)).ToList();
    }

    public async Task<WebhookInfo> CreateWebhookAsync(ulong channelId, string name, CancellationToken cancellationToken = default)
    {
        var hook = await rest.CreateWebhookAsync(channelId, new WebhookProperties(name), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ToInfo(hook, channelId);
    }

    static WebhookInfo ToInfo(Webhook hook, ulong channelId) =>
        new(hook.Id, hook.ChannelId ?? channelId, hook.Name ?? "", (hook as IncomingWebhook)?.Token, hook.ApplicationId);

    internal static AllowedMentionsProperties ToProperties(AllowedMentions m) => new()
    {
        Everyone = m.Everyone,
        // NetCord: null = "parse all of this kind", empty = none.
        AllowedRoles = m.AllRoles ? null : [],
        AllowedUsers = m.Users,
        ReplyMention = m.RepliedUser,
    };

    static ReactionEmojiProperties ToProperties(EmojiRef emoji) =>
        emoji.Id is ulong id ? new ReactionEmojiProperties(emoji.Name, id) : new ReactionEmojiProperties(emoji.Name);
}
