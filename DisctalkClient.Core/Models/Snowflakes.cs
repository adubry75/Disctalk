namespace DisctalkClient.Core.Models;

public static class Snowflakes
{
    const long DiscordEpochMs = 1420070400000;

    public static DateTimeOffset ToTimestamp(ulong id) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(id >> 22) + DiscordEpochMs);

    public static ulong FromTimestamp(DateTimeOffset timestamp) =>
        (ulong)(timestamp.ToUnixTimeMilliseconds() - DiscordEpochMs) << 22;
}
