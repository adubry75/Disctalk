using System.Globalization;
using System.Text.Json.Nodes;
using Disctalk.Core.Http;

namespace DisctalkClient.Core.UserAccount;

public static class GatewayOpcodes
{
    public const int Dispatch = 0, Heartbeat = 1, Identify = 2, Resume = 6, Reconnect = 7, InvalidSession = 9, Hello = 10, HeartbeatAck = 11;
    /// <summary>Bulk guild subscriptions (typing, threads, member list ranges) — what the web client sends when you open a guild.</summary>
    public const int GuildSubscriptionsBulk = 37;
}

/// <summary>What to do after the gateway connection ends.</summary>
public enum ReconnectAction
{
    /// <summary>Reconnect and RESUME the session (no missed events).</summary>
    Resume,
    /// <summary>Reconnect with a fresh IDENTIFY (the session can't be resumed).</summary>
    Identify,
    /// <summary>Don't reconnect: retrying would fail the same way (bad token, invalid payload…).</summary>
    Stop,
}

/// <summary>Builds gateway payloads and decides reconnect behavior. Pure functions; no I/O.</summary>
public static class GatewayProtocol
{
    /// <summary>Same URL shape as the web client (API v9 gateway, JSON, zlib-stream transport compression).</summary>
    public const string DefaultGatewayUrl = "wss://gateway.discord.gg";
    public const string Query = "?encoding=json&v=9&compress=zlib-stream";

    /// <summary>
    /// Default IDENTIFY capabilities when the header profile doesn't set <c>gateway.capabilities</c>:
    /// only DEDUPE_USER_OBJECTS (1 &lt;&lt; 4). Keeps READY compact while staying in the plain JSON shape the parser expects.
    /// </summary>
    public const int DefaultCapabilities = 1 << 4;

    public static Uri GatewayUri(string? resumeUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(resumeUrl) ? DefaultGatewayUrl : resumeUrl.TrimEnd('/');
        return new Uri(baseUrl + "/" + Query);
    }

    public static JsonObject Identify(string token, HeaderProfile profile, int buildNumber) => new()
    {
        ["op"] = GatewayOpcodes.Identify,
        ["d"] = new JsonObject
        {
            ["token"] = token,
            ["capabilities"] = profile.GatewayCapabilities ?? DefaultCapabilities,
            // The archiver's super properties (same object it base64s into X-Super-Properties), per the header profile.
            ["properties"] = profile.BuildSuperProperties(buildNumber),
            // "unknown" keeps whatever status you set in the official client instead of forcing online.
            ["presence"] = new JsonObject { ["status"] = "unknown", ["since"] = 0, ["activities"] = new JsonArray(), ["afk"] = false },
            ["compress"] = false,
            ["client_state"] = new JsonObject { ["guild_versions"] = new JsonObject() },
        },
    };

    public static JsonObject Resume(string token, string sessionId, long sequence) => new()
    {
        ["op"] = GatewayOpcodes.Resume,
        ["d"] = new JsonObject { ["token"] = token, ["session_id"] = sessionId, ["seq"] = sequence },
    };

    public static JsonObject Heartbeat(long? sequence) => new()
    {
        ["op"] = GatewayOpcodes.Heartbeat,
        ["d"] = sequence is long s ? JsonValue.Create(s) : null,
    };

    /// <summary>Typing and thread events for one guild, plus the first 100 entries of a channel's member list.</summary>
    public static JsonObject SubscribeGuild(ulong guildId, ulong? channelId) => new()
    {
        ["op"] = GatewayOpcodes.GuildSubscriptionsBulk,
        ["d"] = new JsonObject
        {
            ["subscriptions"] = new JsonObject
            {
                [guildId.ToString(CultureInfo.InvariantCulture)] = new JsonObject
                {
                    ["typing"] = true,
                    ["threads"] = true,
                    ["activities"] = false,
                    ["member_updates"] = false,
                    ["members"] = new JsonArray(),
                    ["channels"] = channelId is ulong c
                        ? new JsonObject { [c.ToString(CultureInfo.InvariantCulture)] = new JsonArray(new JsonArray(0, 99)) }
                        : new JsonObject(),
                    ["thread_member_lists"] = new JsonArray(),
                },
            },
        },
    };

    /// <summary>
    /// Close codes: 4004 bad token; 4010–4014 shard/version/intent errors a user client should never hit (stop rather
    /// than hammer); 4007 bad sequence and 4009 session timeout need a fresh IDENTIFY; anything else (network drop,
    /// 1006, 4000 unknown error, our own 4000 reconnect close) resumes.
    /// </summary>
    public static ReconnectAction OnClose(int? closeCode) => closeCode switch
    {
        4004 or 4010 or 4011 or 4012 or 4013 or 4014 => ReconnectAction.Stop,
        4007 or 4009 => ReconnectAction.Identify,
        _ => ReconnectAction.Resume,
    };

    public static string DescribeClose(int? closeCode) => closeCode switch
    {
        4004 => "Discord rejected the token. It was probably reset (password change or logout); sign in again.",
        4008 => "Discord says we sent too much too fast. Reconnecting slowly…",
        4010 or 4011 or 4012 or 4013 or 4014 => $"Discord closed the connection ({closeCode}).",
        _ => "Connection lost — reconnecting…",
    };

    /// <summary>
    /// Exponential backoff with jitter: ~1s, 2s, 4s … capped at 60s. <paramref name="jitter"/> is in [0, 1).
    /// Attempt 0 (a reconnect Discord asked for) waits only the jitter, up to 1s.
    /// </summary>
    public static TimeSpan Backoff(int attempt, double jitter)
    {
        var baseSeconds = attempt <= 0 ? 0 : Math.Min(60, Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(60, baseSeconds + jitter));
    }

    /// <summary>Discord asks for 1–5 seconds before re-identifying after an invalid session.</summary>
    public static TimeSpan InvalidSessionDelay(double jitter) => TimeSpan.FromSeconds(1 + 4 * jitter);

    /// <summary>Minimum spacing between IDENTIFYs, so a crash loop can't burn through the daily identify budget.</summary>
    public static readonly TimeSpan MinimumIdentifyInterval = TimeSpan.FromSeconds(5);
}
