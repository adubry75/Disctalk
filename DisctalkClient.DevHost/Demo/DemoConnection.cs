using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.DevHost.Demo;

internal sealed class DemoConnection(DemoHub hub) : IDiscordConnection, IBotRestApi
{
    readonly CancellationTokenSource _cts = new();

    public event Func<GatewayEvent, ValueTask>? EventReceived;

    public IBotRestApi Rest => this;

    public async ValueTask Emit(GatewayEvent e)
    {
        if (EventReceived is { } handler) await handler(e);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Emit(new ConnectionStateChangedEvent(ConnectionState.Connecting, "Connecting to the demo café…"));
        await Task.Delay(300, cancellationToken);
        await Emit(new ReadyEvent(DemoHub.Bot));
        await Emit(new GuildAvailableEvent(hub.Snapshot()));
        _ = hub.RunActivityAsync(_cts.Token);
    }

    public Task StopAsync()
    {
        _cts.Cancel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit, CancellationToken cancellationToken = default)
    {
        await Task.Delay(250, cancellationToken); // feel like a network call
        return hub.Before(channelId, beforeMessageId, limit);
    }

    public async Task<ChatMessage> SendMessageAsync(BotMessageRequest request, CancellationToken cancellationToken = default)
    {
        var replyTo = request.ReplyToMessageId is ulong id ? hub.Get(request.ChannelId, id) : null;
        var message = hub.Post(request.ChannelId, DemoHub.Bot, request.Content, replyTo, ToAttachments(request.Files));
        await Emit(new MessageCreateEvent(message));
        return message;
    }

    public async Task<ChatMessage> EditMessageAsync(ulong channelId, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default)
    {
        var existing = hub.Get(channelId, messageId) ?? throw new InvalidOperationException("Unknown Message");
        var edited = existing with { Content = content, EditedAt = DateTimeOffset.UtcNow };
        hub.Replace(edited);
        await Emit(new MessageUpdateEvent(edited));
        return edited;
    }

    public async Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        if (hub.Remove(channelId, messageId)) await Emit(new MessageDeleteEvent(channelId, [messageId]));
    }

    public async Task AddReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default)
    {
        if (hub.Get(channelId, messageId) is { } m) hub.Replace(DemoHub.WithReaction(m, emoji, true));
        await Emit(new ReactionAddEvent(channelId, messageId, DemoHub.BotId, emoji));
    }

    public async Task RemoveOwnReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default)
    {
        if (hub.Get(channelId, messageId) is { } m) hub.Replace(DemoHub.WithReaction(m, emoji, true, -1));
        await Emit(new ReactionRemoveEvent(channelId, messageId, DemoHub.BotId, emoji));
    }

    public Task TriggerTypingAsync(ulong channelId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<WebhookInfo>> GetChannelWebhooksAsync(ulong channelId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WebhookInfo>>(hub.FindWebhook(channelId) is { } hook ? [hook] : []);

    public Task<WebhookInfo> CreateWebhookAsync(ulong channelId, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(hub.CreateWebhook(channelId, name));

    internal static IReadOnlyList<AttachmentInfo> ToAttachments(IReadOnlyList<OutgoingFile> files) =>
        files.Select((f, i) => new AttachmentInfo
        {
            Id = (ulong)(1100000000000000800 + i),
            FileName = f.FileName,
            ContentType = f.ContentType,
            Size = f.Content.Length,
            // Show uploaded images inline in the demo via a data URL.
            Url = f.ContentType?.StartsWith("image/") == true ? $"data:{f.ContentType};base64,{Convert.ToBase64String(f.Content.Span)}" : "#",
        }).ToList();
}
