using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.State;

/// <summary>
/// Messages for one channel, ascending by id. Live gateway messages and REST scrollback pages merge here; ids dedupe.
/// Not thread-safe: <see cref="ChatClient"/> serializes access.
/// </summary>
public sealed class MessageCache(int maxMessages = MessageCache.DefaultMaxMessages)
{
    public const int DefaultMaxMessages = 2000;

    readonly List<ChatMessage> _messages = new();

    /// <summary>False once a scrollback page came back short (we hit the start of the channel).</summary>
    public bool HasMoreBefore { get; private set; } = true;

    /// <summary>True once the first REST page has been fetched; until then only live messages are here.</summary>
    public bool IsInitialized { get; private set; }

    public int Count => _messages.Count;

    public ulong? OldestId => _messages.Count > 0 ? _messages[0].Id : null;
    public ulong? NewestId => _messages.Count > 0 ? _messages[^1].Id : null;

    public IReadOnlyList<ChatMessage> Snapshot() => _messages.ToArray();

    public ChatMessage? Get(ulong messageId)
    {
        int i = IndexOf(messageId);
        return i >= 0 ? _messages[i] : null;
    }

    /// <summary>Adds a new message or replaces the existing one with the same id.</summary>
    public void Upsert(ChatMessage message)
    {
        int i = IndexOf(message.Id);
        if (i >= 0) _messages[i] = Merge(_messages[i], message);
        else
        {
            _messages.Insert(~i, message);
            Trim();
        }
    }

    /// <summary>Applies an edit. Edits to messages we never loaded are dropped (they'll arrive fresh via scrollback).</summary>
    public bool Update(ChatMessage message)
    {
        int i = IndexOf(message.Id);
        if (i < 0) return false;
        _messages[i] = Merge(_messages[i], message);
        return true;
    }

    public bool Remove(IEnumerable<ulong> messageIds)
    {
        bool removed = false;
        foreach (var id in messageIds)
        {
            int i = IndexOf(id);
            if (i < 0) continue;
            _messages.RemoveAt(i);
            removed = true;
        }
        return removed;
    }

    /// <summary>Merges a REST page of messages older than what we have.</summary>
    /// <param name="page">Messages in any order.</param>
    /// <param name="requestedLimit">The page size asked for; a shorter page means the channel start was reached.</param>
    public void AddHistoryPage(IReadOnlyCollection<ChatMessage> page, int requestedLimit)
    {
        foreach (var m in page)
        {
            int i = IndexOf(m.Id);
            if (i >= 0) _messages[i] = Merge(m, _messages[i]);
            else _messages.Insert(~i, m);
        }
        if (page.Count < requestedLimit) HasMoreBefore = false;
        IsInitialized = true;
        TrimNewestIfOverCap();
    }

    public bool ApplyReaction(ulong messageId, EmojiRef emoji, int delta, bool byMe)
    {
        int i = IndexOf(messageId);
        if (i < 0) return false;
        var message = _messages[i];
        var reactions = message.Reactions.ToList();
        int r = reactions.FindIndex(x => x.Emoji.Matches(emoji));
        if (r >= 0)
        {
            var existing = reactions[r];
            var count = existing.Count + delta;
            var me = byMe ? delta > 0 : existing.Me;
            if (count <= 0) reactions.RemoveAt(r);
            else reactions[r] = existing with { Count = count, Me = me };
        }
        else if (delta > 0)
        {
            reactions.Add(new ReactionInfo(emoji, delta, byMe));
        }
        _messages[i] = message with { Reactions = reactions };
        return true;
    }

    public bool ClearReactions(ulong messageId, EmojiRef? emoji)
    {
        int i = IndexOf(messageId);
        if (i < 0) return false;
        var message = _messages[i];
        _messages[i] = message with
        {
            Reactions = emoji is null ? [] : message.Reactions.Where(r => !r.Emoji.Matches(emoji)).ToList(),
        };
        return true;
    }

    int IndexOf(ulong id)
    {
        int lo = 0, hi = _messages.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            ulong v = _messages[mid].Id;
            if (v == id) return mid;
            if (v < id) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>
    /// Gateway updates are full message objects in API v10, but reactions are tracked through reaction events and the
    /// referenced message is often omitted; keep what we already know.
    /// </summary>
    static ChatMessage Merge(ChatMessage existing, ChatMessage incoming) => incoming with
    {
        Reactions = incoming.Reactions.Count > 0 ? incoming.Reactions : existing.Reactions,
        ReplyTo = incoming.ReplyTo ?? existing.ReplyTo,
    };

    // Live messages push out the oldest; the user can scroll back for them again.
    void Trim()
    {
        if (_messages.Count <= maxMessages) return;
        _messages.RemoveRange(0, _messages.Count - maxMessages);
        HasMoreBefore = true;
    }

    // Scrollback pushes out the newest so the page the user asked for stays.
    void TrimNewestIfOverCap()
    {
        if (_messages.Count <= maxMessages) return;
        _messages.RemoveRange(maxMessages, _messages.Count - maxMessages);
    }
}
