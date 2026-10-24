namespace DisctalkClient.Core.State;

/// <summary>Who is typing where. Discord sends TYPING_START every ~8s while typing; an entry lives 10s.</summary>
public sealed class TypingTracker(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    readonly Dictionary<ulong, Dictionary<ulong, (string Name, DateTimeOffset Expires)>> _channels = new();
    readonly Lock _lock = new();

    public void Start(ulong channelId, ulong userId, string displayName)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(channelId, out var users)) _channels[channelId] = users = new();
            users[userId] = (displayName, time.GetUtcNow() + Lifetime);
        }
    }

    /// <summary>A message from the user ends their typing indicator immediately.</summary>
    public bool Stop(ulong channelId, ulong userId)
    {
        lock (_lock) return _channels.TryGetValue(channelId, out var users) && users.Remove(userId);
    }

    public IReadOnlyList<string> GetTypingNames(ulong channelId, ulong? excludeUserId = null)
    {
        lock (_lock)
        {
            if (!_channels.TryGetValue(channelId, out var users)) return [];
            var now = time.GetUtcNow();
            foreach (var expired in users.Where(u => u.Value.Expires <= now).Select(u => u.Key).ToList()) users.Remove(expired);
            return users.Where(u => u.Key != excludeUserId).OrderBy(u => u.Value.Expires).Select(u => u.Value.Name).ToList();
        }
    }

    public static string Describe(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => $"{names[0]} is typing…",
        2 => $"{names[0]} and {names[1]} are typing…",
        3 => $"{names[0]}, {names[1]}, and {names[2]} are typing…",
        _ => "Several people are typing…",
    };
}
