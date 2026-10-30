using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.UserAccount;
using Microsoft.Extensions.Time.Testing;
using static DisctalkClient.Tests.UserAccount.UserTestKit;

namespace DisctalkClient.Tests.UserAccount;

public sealed class UserGatewayConnectionTests : IAsyncDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    readonly Channel<FakeTransport> _created = Channel.CreateUnbounded<FakeTransport>();
    readonly List<GatewayEvent> _events = new();
    readonly List<TimeSpan> _backoffs = new();
    readonly FakeTimeProvider _time = new(TestData.Now);
    readonly UserGatewayConnection _conn;

    public UserGatewayConnectionTests() : this(HttpStatusCode.OK) { }

    UserGatewayConnectionTests(HttpStatusCode meStatus)
    {
        var network = new FakeHttpHandler((_, _) => FakeHttpHandler.Json("""{"id":"110000000000000100","username":"whiskers"}""", meStatus));
        _conn = new UserGatewayConnection(UserToken, Profile, BuildNumbers(), Http(network), NewTransport, _time)
        {
            BackoffDelayOverride = (delay, _) =>
            {
                lock (_backoffs) _backoffs.Add(delay);
                return Task.CompletedTask;
            },
            Jitter = () => 0.5,
        };
        _conn.EventReceived += e =>
        {
            lock (_events) _events.Add(e);
            return ValueTask.CompletedTask;
        };
    }

    FakeTransport NewTransport()
    {
        var t = new FakeTransport();
        _created.Writer.TryWrite(t);
        return t;
    }

    async Task<FakeTransport> NextTransportAsync()
    {
        using var cts = new CancellationTokenSource(Timeout);
        return await _created.Reader.ReadAsync(cts.Token);
    }

    async Task<T> WaitForEventAsync<T>(Func<T, bool>? match = null) where T : GatewayEvent
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (_events)
                if (_events.OfType<T>().FirstOrDefault(e => match?.Invoke(e) ?? true) is { } found) return found;
            await Task.Delay(10);
        }
        lock (_events) throw new TimeoutException($"No matching {typeof(T).Name} arrived. Got: {string.Join(", ", _events)}");
    }

    /// <summary>Connect, identify and receive READY. Returns the live transport.</summary>
    async Task<FakeTransport> ReadyAsync()
    {
        await _conn.StartAsync();
        var t = await NextTransportAsync();
        t.Hello();
        Assert.Equal(GatewayOpcodes.Identify, (int)(await t.NextSentAsync())["op"]!);
        t.Dispatch("READY", 1, new { session_id = "sess-1", resume_gateway_url = "wss://resume.discord.gg", user = SelfUser, guilds = Array.Empty<object>(), private_channels = Array.Empty<object>() });
        await WaitForEventAsync<ReadyEvent>();
        return t;
    }

    [Fact]
    public async Task Identifies_with_the_token_and_profile_then_reports_ready()
    {
        await _conn.StartAsync();
        var t = await NextTransportAsync();
        t.Hello();

        var identify = await t.NextSentAsync();
        Assert.Equal(new Uri("wss://gateway.discord.gg/?encoding=json&v=9&compress=zlib-stream"), t.ConnectedTo);
        Assert.Equal(UserToken, (string)identify["d"]!["token"]!);
        Assert.Equal(Profile.FallbackBuildNumber, (int)identify["d"]!["properties"]!["client_build_number"]!);

        t.Dispatch("READY", 1, new { session_id = "s", user = SelfUser });
        Assert.Equal("Whiskers", (await WaitForEventAsync<ReadyEvent>()).CurrentUser.DisplayName);
        Assert.Equal(ConnectionState.Connecting, (await WaitForEventAsync<ConnectionStateChangedEvent>()).State);
    }

    [Fact]
    public async Task A_bad_token_fails_before_any_gateway_connection()
    {
        await using var fixture = new UserGatewayConnectionTests(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<DiscordApiException>(() => fixture._conn.StartAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        Assert.False(fixture._created.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Reconnect_request_resumes_on_the_resume_url_with_the_last_sequence()
    {
        var t1 = await ReadyAsync();
        t1.Dispatch("MESSAGE_CREATE", 2, new { id = "9", channel_id = "10", author = SelfUser, content = "hi" });
        await WaitForEventAsync<MessageCreateEvent>();

        t1.Push(new { op = 7, d = (object?)null });

        var t2 = await NextTransportAsync();
        Assert.Equal(4000, t1.ClosedByClientWith); // 4000 keeps the session resumable; 1000 would end it
        Assert.Equal("resume.discord.gg", t2.ConnectedTo!.Host);
        t2.Hello();
        var resume = await t2.NextSentAsync();
        Assert.Equal("""{"op":6,"d":{"token":"MTEwMDAwMDAwMDAwMDAwMTAw.TESTXX.test-token-not-real-test-token-not-real","session_id":"sess-1","seq":2}}""", resume.ToJsonString());

        t2.Dispatch("RESUMED", 3, new { });
        await WaitForEventAsync<ConnectionStateChangedEvent>(e => e.State == ConnectionState.Connected);
    }

    [Fact]
    public async Task A_network_drop_backs_off_and_resumes()
    {
        var t1 = await ReadyAsync();

        t1.Close(1006);

        var t2 = await NextTransportAsync();
        await WaitForEventAsync<ConnectionStateChangedEvent>(e => e.State == ConnectionState.Reconnecting);
        t2.Hello();
        Assert.Equal(GatewayOpcodes.Resume, (int)(await t2.NextSentAsync())["op"]!);
        lock (_backoffs) Assert.Contains(GatewayProtocol.Backoff(1, 0.5), _backoffs);
    }

    [Fact]
    public async Task Repeated_failures_back_off_exponentially()
    {
        await _conn.StartAsync();
        for (int i = 0; i < 4; i++)
        {
            var t = await NextTransportAsync();
            t.Close(1006); // dies before Hello every time
        }
        await NextTransportAsync();

        lock (_backoffs)
            Assert.Equal([1.5, 2.5, 4.5, 8.5], _backoffs.Take(4).Select(b => b.TotalSeconds));
    }

    [Fact]
    public async Task Close_4004_stops_for_good_with_an_explanation()
    {
        var t1 = await ReadyAsync();

        t1.Close(4004);

        var failed = await WaitForEventAsync<ConnectionStateChangedEvent>(e => e.State == ConnectionState.Failed);
        Assert.Contains("rejected the token", failed.Detail);
        await Task.Delay(100);
        Assert.False(_created.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Invalid_session_waits_then_identifies_fresh()
    {
        var t1 = await ReadyAsync();

        t1.Push(new { op = 9, d = false });

        var t2 = await NextTransportAsync();
        Assert.Equal("gateway.discord.gg", t2.ConnectedTo!.Host); // not the old session's resume URL
        t2.Hello();
        Assert.Equal(GatewayOpcodes.Identify, (int)(await t2.NextSentAsync())["op"]!);
        lock (_backoffs) Assert.Contains(GatewayProtocol.InvalidSessionDelay(0.5), _backoffs);
    }

    [Fact]
    public async Task Heartbeats_follow_the_interval_and_a_missing_ack_means_reconnect()
    {
        await _conn.StartAsync();
        var t1 = await NextTransportAsync();
        t1.Hello(intervalMs: 1000);
        Assert.Equal(GatewayOpcodes.Identify, (int)(await t1.NextSentAsync())["op"]!);
        t1.Dispatch("READY", 5, new { session_id = "s", resume_gateway_url = "wss://resume.discord.gg", user = SelfUser });
        await WaitForEventAsync<ReadyEvent>();

        _time.Advance(TimeSpan.FromMilliseconds(500)); // first beat at interval * jitter
        var beat = await t1.NextSentAsync();
        Assert.Equal("""{"op":1,"d":5}""", beat.ToJsonString());

        t1.Push(new { op = 11 });
        await Task.Delay(50);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(GatewayOpcodes.Heartbeat, (int)(await t1.NextSentAsync())["op"]!);

        // No ACK for that one: the next tick declares the connection dead and resumes on a new socket.
        _time.Advance(TimeSpan.FromSeconds(1));
        var t2 = await NextTransportAsync();
        t2.Hello();
        Assert.Equal(GatewayOpcodes.Resume, (int)(await t2.NextSentAsync())["op"]!);
    }

    [Fact]
    public async Task Discord_can_ask_for_a_heartbeat_immediately()
    {
        var t = await ReadyAsync();
        t.Push(new { op = 1, d = (object?)null });
        Assert.Equal("""{"op":1,"d":1}""", (await t.NextSentAsync()).ToJsonString());
    }

    [Fact]
    public async Task Focusing_a_channel_subscribes_its_guild_once()
    {
        var t = await ReadyAsync();

        await _conn.FocusChannelAsync(1, 10);
        await _conn.FocusChannelAsync(1, 10);
        await _conn.FocusChannelAsync(0, 20); // DMs need no subscription

        var sub = await t.NextSentAsync();
        Assert.Equal(GatewayOpcodes.GuildSubscriptionsBulk, (int)sub["op"]!);
        Assert.NotNull(sub["d"]!["subscriptions"]!["1"]!["channels"]!["10"]);
        Assert.False(t.TryNextSent(out _));
    }

    [Fact]
    public async Task Focus_is_restored_after_a_reconnect()
    {
        var t1 = await ReadyAsync();
        await _conn.FocusChannelAsync(1, 10);
        await t1.NextSentAsync();

        t1.Close(1006);
        var t2 = await NextTransportAsync();
        t2.Hello();
        await t2.NextSentAsync(); // resume
        t2.Dispatch("RESUMED", 2, new { });

        Assert.Equal(GatewayOpcodes.GuildSubscriptionsBulk, (int)(await t2.NextSentAsync())["op"]!);
    }

    [Fact]
    public async Task A_payload_that_fails_to_translate_does_not_drop_the_session()
    {
        var t = await ReadyAsync();
        t.PushRaw("""{"op":0,"s":2,"t":"MESSAGE_CREATE","d":"not an object"}""");
        t.Dispatch("MESSAGE_CREATE", 3, new { id = "9", channel_id = "10", author = SelfUser, content = "still here" });

        Assert.Equal("still here", (await WaitForEventAsync<MessageCreateEvent>()).Message.Content);
        Assert.False(_created.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Stopping_closes_normally_and_never_reconnects()
    {
        var t = await ReadyAsync();

        await _conn.StopAsync();

        Assert.Equal(1000, t.ClosedByClientWith);
        await Task.Delay(50);
        Assert.False(_created.Reader.TryRead(out _));
    }

    public async ValueTask DisposeAsync() => await _conn.DisposeAsync();
}
