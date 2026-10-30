using System.Globalization;
using System.Text.Json;

namespace DisctalkClient.Core.State;

/// <summary>
/// Unread dots: a channel is unread when its newest message id is past the last id the user saw there.
/// Channels seen for the first time start out read, so a fresh install isn't a wall of dots.
/// </summary>
public sealed class UnreadTracker
{
    readonly Dictionary<ulong, ulong> _lastRead = new();
    readonly Dictionary<ulong, ulong> _latest = new();
    readonly Lock _lock = new();

    public void TrackChannel(ulong channelId, ulong? lastMessageId)
    {
        lock (_lock)
        {
            if (lastMessageId is ulong latest) _latest[channelId] = Math.Max(latest, _latest.GetValueOrDefault(channelId));
            if (!_lastRead.ContainsKey(channelId)) _lastRead[channelId] = _latest.GetValueOrDefault(channelId);
        }
    }

    /// <summary>
    /// Discord's own read markers (user accounts, from READY). Never moves a marker backwards, so reading on this
    /// device wins over a stale server value.
    /// </summary>
    public void SeedLastRead(IReadOnlyDictionary<ulong, ulong> lastRead)
    {
        lock (_lock)
            foreach (var (channel, id) in lastRead)
                _lastRead[channel] = Math.Max(id, _lastRead.GetValueOrDefault(channel));
    }

    /// <returns>True if the channel just went from read to unread.</returns>
    public bool OnMessage(ulong channelId, ulong messageId, bool isActiveChannel)
    {
        lock (_lock)
        {
            bool wasUnread = IsUnreadLocked(channelId);
            if (messageId > _latest.GetValueOrDefault(channelId)) _latest[channelId] = messageId;
            if (isActiveChannel) _lastRead[channelId] = _latest[channelId];
            else _lastRead.TryAdd(channelId, 0);
            return !wasUnread && IsUnreadLocked(channelId);
        }
    }

    public bool MarkRead(ulong channelId)
    {
        lock (_lock)
        {
            bool wasUnread = IsUnreadLocked(channelId);
            _lastRead[channelId] = _latest.GetValueOrDefault(channelId);
            return wasUnread;
        }
    }

    public bool IsUnread(ulong channelId)
    {
        lock (_lock) return IsUnreadLocked(channelId);
    }

    bool IsUnreadLocked(ulong channelId) =>
        _latest.TryGetValue(channelId, out var latest) && latest > _lastRead.GetValueOrDefault(channelId);

    public string Serialize()
    {
        lock (_lock)
            return JsonSerializer.Serialize(_lastRead.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value.ToString(CultureInfo.InvariantCulture)));
    }

    public void Restore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        Dictionary<string, string>? data;
        try { data = JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException) { return; }
        if (data is null) return;
        lock (_lock)
            foreach (var (k, v) in data)
                if (ulong.TryParse(k, out var channel) && ulong.TryParse(v, out var id))
                    _lastRead[channel] = Math.Max(id, _lastRead.GetValueOrDefault(channel));
    }
}
