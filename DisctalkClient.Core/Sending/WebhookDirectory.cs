using System.Collections.Concurrent;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Sending;

/// <summary>
/// Finds (or creates, once) the client's own webhook in each channel. The bot needs Manage Webhooks for this.
/// Webhooks created by this app are recognized by application id, falling back to the well-known name.
/// </summary>
public sealed class WebhookDirectory(IBotRestApi rest, Func<ulong?> applicationId)
{
    public const string WebhookName = "Disctalk Client";

    readonly ConcurrentDictionary<ulong, WebhookInfo> _byChannel = new();
    readonly ConcurrentDictionary<ulong, byte> _ownIds = new();
    readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True if <paramref name="webhookId"/> is one of ours (only known once that channel was resolved).</summary>
    public bool IsOwnWebhook(ulong? webhookId) => webhookId is ulong id && _ownIds.ContainsKey(id);

    public WebhookInfo? TryGetCached(ulong channelId) => _byChannel.GetValueOrDefault(channelId);

    public async Task<WebhookInfo?> FindAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        if (_byChannel.TryGetValue(channelId, out var cached)) return cached;
        var hooks = await rest.GetChannelWebhooksAsync(channelId, cancellationToken).ConfigureAwait(false);
        var appId = applicationId();
        var own = hooks.Where(h => h.CanExecute)
            .OrderByDescending(h => appId is not null && h.ApplicationId == appId)
            .FirstOrDefault(h => (appId is not null && h.ApplicationId == appId) || h.Name == WebhookName);
        if (own is not null) Remember(own);
        return own;
    }

    public async Task<WebhookInfo> GetOrCreateAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        if (_byChannel.TryGetValue(channelId, out var cached)) return cached;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await FindAsync(channelId, cancellationToken).ConfigureAwait(false);
            if (existing is not null) return existing;
            var created = await rest.CreateWebhookAsync(channelId, WebhookName, cancellationToken).ConfigureAwait(false);
            if (!created.CanExecute) throw new InvalidOperationException("Discord created the webhook but returned no token.");
            Remember(created);
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }

    void Remember(WebhookInfo hook)
    {
        _byChannel[hook.ChannelId] = hook;
        _ownIds[hook.Id] = 0;
    }
}
