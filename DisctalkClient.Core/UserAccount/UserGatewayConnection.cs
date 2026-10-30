using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Disctalk.Core.Http;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DisctalkClient.Core.UserAccount;

/// <summary>
/// Signs in as a user account: REST through the archiver's header pipeline, plus a gateway session that identifies
/// with the same super properties. Handles heartbeats, zombie detection, RESUME after network drops, Discord's
/// reconnect / invalid-session requests, and exponential backoff so a dead network never turns into a reconnect storm.
/// </summary>
public sealed class UserGatewayConnection : IDiscordConnection
{
    readonly string _token;
    readonly HeaderProfile _profile;
    readonly BuildNumberProvider _buildNumbers;
    readonly Func<IGatewayTransport> _transports;
    readonly HttpClient _http;
    readonly TimeProvider _time;
    readonly ILogger _log;
    readonly UserEventTranslator _translator = new();
    readonly SemaphoreSlim _sendLock = new(1, 1);
    readonly UserRestApi _rest;

    CancellationTokenSource? _stop;
    Task? _loop;
    IGatewayTransport? _transport;
    long? _sequence;
    DateTimeOffset _lastIdentifyAt = DateTimeOffset.MinValue;
    volatile bool _heartbeatAcked = true;
    volatile bool _ready;
    (ulong Guild, ulong Channel)? _focus;
    (ulong Guild, ulong Channel)? _sentFocus;
    CancellationTokenSource? _backoffSkip;

    public UserGatewayConnection(string token, HeaderProfile profile, BuildNumberProvider buildNumbers, HttpClient http,
        Func<IGatewayTransport>? transports = null, TimeProvider? time = null, ILogger? logger = null)
    {
        _token = token;
        _profile = profile;
        _buildNumbers = buildNumbers;
        _http = http;
        _rest = new UserRestApi(http);
        _transports = transports ?? (() => new WebSocketGatewayTransport(profile));
        _time = time ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
    }

    public event Func<GatewayEvent, ValueTask>? EventReceived;

    public AccountKind Kind => AccountKind.User;

    public IDiscordRestApi Rest => _rest;

    /// <summary>Test hook: replaces backoff waits (heartbeats still use the <see cref="TimeProvider"/>).</summary>
    internal Func<TimeSpan, CancellationToken, Task>? BackoffDelayOverride { get; set; }

    /// <summary>Test hook: jitter source in [0, 1).</summary>
    internal Func<double> Jitter { get; set; } = Random.Shared.NextDouble;

    /// <summary>
    /// Checks the token once over REST (GET /users/@me, as the web client does at startup) so a bad token fails here
    /// and is never saved; then runs the gateway session in the background. A network error here is not fatal: the
    /// gateway loop keeps retrying with backoff.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _rest.GetCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DiscordApiException ex) when (ex.Status is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or DiscordApiException && !cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("Token check failed ({Error}); trying the gateway anyway", SecretRedactor.Redact(ex.Message));
        }

        _stop = new CancellationTokenSource();
        var stop = _stop.Token;
        _loop = Task.Run(() => RunAsync(stop), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_stop is null) return;
        if (_transport is { } transport)
        {
            // 1000 ends the session on Discord's side too: we're signing out, not reconnecting.
            try { await transport.CloseAsync(1000, CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
        }
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stop?.Dispose();
        _http.Dispose();
    }

    public async ValueTask FocusChannelAsync(ulong guildId, ulong channelId)
    {
        if (guildId == GuildInfo.DirectMessagesId) return;
        _focus = (guildId, channelId);
        await SendFocusAsync(CancellationToken.None).ConfigureAwait(false);
    }

    async Task SendFocusAsync(CancellationToken ct)
    {
        if (_focus is not { } focus || !_ready || _sentFocus == focus) return;
        _sentFocus = focus;
        await SendAsync(GatewayProtocol.SubscribeGuild(focus.Guild, focus.Channel), ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- session loop

    sealed record SessionOutcome(ReconnectAction Action, bool ReachedReady, string? Detail = null, bool Immediate = false, bool InvalidSession = false);

    async Task RunAsync(CancellationToken stop)
    {
        int failures = 0;
        bool resume = false;
        bool first = true;
        while (!stop.IsCancellationRequested)
        {
            await EmitAsync(new ConnectionStateChangedEvent(first ? ConnectionState.Connecting : ConnectionState.Reconnecting,
                first ? "Connecting to Discord…" : "Reconnecting…")).ConfigureAwait(false);
            first = false;

            SessionOutcome outcome;
            try
            {
                outcome = await RunSessionAsync(resume && _translator.SessionId is not null && _sequence is not null, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            if (stop.IsCancellationRequested) break;

            if (outcome.Action == ReconnectAction.Stop)
            {
                await EmitAsync(new ConnectionStateChangedEvent(ConnectionState.Failed, outcome.Detail)).ConfigureAwait(false);
                break;
            }

            resume = outcome.Action == ReconnectAction.Resume;
            failures = outcome.ReachedReady ? 1 : failures + 1;
            var delay = outcome.InvalidSession ? GatewayProtocol.InvalidSessionDelay(Jitter())
                : outcome.Immediate ? GatewayProtocol.Backoff(0, Jitter())
                : GatewayProtocol.Backoff(failures, Jitter());
            _log.LogInformation("Gateway session ended ({Detail}); {Action} in {Seconds:F1}s", outcome.Detail ?? "closed", outcome.Action, delay.TotalSeconds);
            await EmitAsync(new ConnectionStateChangedEvent(ConnectionState.Reconnecting, outcome.Detail ?? "Connection lost — reconnecting…")).ConfigureAwait(false);
            try
            {
                await ReconnectBackoffAsync(delay, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // The network came back: try now, and keep the next backoff short.
                failures = 0;
            }
        }
    }

    async Task<SessionOutcome> RunSessionAsync(bool resume, CancellationToken stop)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var ct = session.Token;
        bool reachedReady = false;
        _ready = false;
        _sentFocus = null;
        await using var transport = _transports();
        _transport = transport;
        Task? heartbeat = null;
        try
        {
            await transport.ConnectAsync(GatewayProtocol.GatewayUri(resume ? _translator.ResumeGatewayUrl : null), ct).ConfigureAwait(false);

            using (var hello = await ReceiveAsync(transport, ct).ConfigureAwait(false))
            {
                if (hello is null) return Closed(transport, false);
                if (hello.RootElement.Int("op") != GatewayOpcodes.Hello) return new(ReconnectAction.Resume, false, "Unexpected first gateway message.");
                var interval = TimeSpan.FromMilliseconds(hello.RootElement.Obj("d")?.Long("heartbeat_interval") ?? 41250);
                _heartbeatAcked = true;
                heartbeat = HeartbeatLoopAsync(interval, session, ct);
            }

            if (resume)
            {
                await SendAsync(GatewayProtocol.Resume(_token, _translator.SessionId!, _sequence!.Value), ct).ConfigureAwait(false);
            }
            else
            {
                _sequence = null;
                var wait = _lastIdentifyAt + GatewayProtocol.MinimumIdentifyInterval - _time.GetUtcNow();
                if (wait > TimeSpan.Zero) await BackoffAsync(wait, ct).ConfigureAwait(false);
                var build = await _buildNumbers.GetBuildNumberAsync(ct).ConfigureAwait(false);
                _lastIdentifyAt = _time.GetUtcNow();
                await SendAsync(GatewayProtocol.Identify(_token, _profile, build), ct).ConfigureAwait(false);
            }

            while (true)
            {
                using var doc = await ReceiveAsync(transport, ct).ConfigureAwait(false);
                if (doc is null) return Closed(transport, reachedReady);
                var root = doc.RootElement;
                switch (root.Int("op"))
                {
                    case GatewayOpcodes.Dispatch:
                        if (root.Long("s") is long seq) _sequence = seq;
                        var type = root.Str("t") ?? "";
                        var data = root.TryGetProperty("d", out var d) ? d : default;
                        if (type is "READY" or "RESUMED")
                        {
                            reachedReady = true;
                            _ready = true;
                        }
                        foreach (var e in Translate(type, data)) await EmitAsync(e).ConfigureAwait(false);
                        if (type is "READY" or "RESUMED")
                        {
                            if (type == "RESUMED") await EmitAsync(new ConnectionStateChangedEvent(ConnectionState.Connected)).ConfigureAwait(false);
                            await SendFocusAsync(ct).ConfigureAwait(false);
                        }
                        break;

                    case GatewayOpcodes.Heartbeat:
                        await SendAsync(GatewayProtocol.Heartbeat(_sequence), ct).ConfigureAwait(false);
                        break;

                    case GatewayOpcodes.Reconnect:
                        await transport.CloseAsync(4000, CancellationToken.None).ConfigureAwait(false);
                        return new(ReconnectAction.Resume, reachedReady, "Discord asked us to reconnect.", Immediate: true);

                    case GatewayOpcodes.InvalidSession:
                        bool resumable = root.TryGetProperty("d", out var r) && r.ValueKind == JsonValueKind.True;
                        await transport.CloseAsync(4000, CancellationToken.None).ConfigureAwait(false);
                        return new(resumable ? ReconnectAction.Resume : ReconnectAction.Identify, reachedReady, "Session expired.", InvalidSession: true);

                    case GatewayOpcodes.HeartbeatAck:
                        _heartbeatAcked = true;
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            // The heartbeat loop cancelled the session: no ACK came back, so the connection is a zombie.
            return new(ReconnectAction.Resume, reachedReady, "No heartbeat reply — reconnecting…");
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException or JsonException && !stop.IsCancellationRequested)
        {
            _log.LogInformation("Gateway connection error: {Error}", SecretRedactor.Redact(ex.Message));
            return new(ReconnectAction.Resume, reachedReady, "Connection lost — reconnecting…");
        }
        finally
        {
            _ready = false;
            _transport = null;
            await session.CancelAsync().ConfigureAwait(false);
            if (heartbeat is not null) await heartbeat.ConfigureAwait(false);
        }
    }

    IReadOnlyList<GatewayEvent> Translate(string type, JsonElement data)
    {
        try
        {
            return _translator.Translate(type, data);
        }
        catch (Exception ex)
        {
            // One odd payload must not take the session down.
            _log.LogWarning("Couldn't read {Type}: {Error}", type, SecretRedactor.Redact(ex.Message));
            return [];
        }
    }

    static SessionOutcome Closed(IGatewayTransport transport, bool reachedReady)
    {
        var action = GatewayProtocol.OnClose(transport.CloseCode);
        return new(action, reachedReady, GatewayProtocol.DescribeClose(transport.CloseCode));
    }

    async Task HeartbeatLoopAsync(TimeSpan interval, CancellationTokenSource session, CancellationToken ct)
    {
        try
        {
            // Discord asks for the first beat after interval * jitter so clients don't beat in lockstep.
            await Task.Delay(interval * Jitter(), _time, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                if (!_heartbeatAcked)
                {
                    _log.LogInformation("No heartbeat ACK; treating the connection as dead");
                    await session.CancelAsync().ConfigureAwait(false);
                    return;
                }
                _heartbeatAcked = false;
                await SendAsync(GatewayProtocol.Heartbeat(_sequence), ct).ConfigureAwait(false);
                await Task.Delay(interval, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is WebSocketException or IOException or InvalidOperationException)
        {
            await session.CancelAsync().ConfigureAwait(false);
        }
    }

    static async Task<JsonDocument?> ReceiveAsync(IGatewayTransport transport, CancellationToken ct) =>
        await transport.ReceiveAsync(ct).ConfigureAwait(false) is { } payload ? JsonDocument.Parse(payload) : null;

    async Task SendAsync(JsonObject payload, CancellationToken ct)
    {
        if (_transport is not { } transport) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try { await transport.SendAsync(bytes, ct).ConfigureAwait(false); }
        finally { _sendLock.Release(); }
    }

    Task BackoffAsync(TimeSpan delay, CancellationToken ct) =>
        BackoffDelayOverride?.Invoke(delay, ct) ?? Task.Delay(delay, _time, ct);

    /// <summary>A reconnect wait that <see cref="NetworkAvailable"/> can cut short. (The IDENTIFY spacing wait can't be skipped.)</summary>
    async Task ReconnectBackoffAsync(TimeSpan delay, CancellationToken stop)
    {
        using var skip = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, skip.Token);
        Volatile.Write(ref _backoffSkip, skip);
        try { await BackoffAsync(delay, linked.Token).ConfigureAwait(false); }
        finally { Volatile.Write(ref _backoffSkip, null); }
    }

    public void NetworkAvailable()
    {
        try { Volatile.Read(ref _backoffSkip)?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    async ValueTask EmitAsync(GatewayEvent e)
    {
        if (EventReceived is { } handler)
        {
            try { await handler(e).ConfigureAwait(false); }
            catch (Exception ex) { _log.LogError("Event handler failed for {Event}: {Error}", e.GetType().Name, SecretRedactor.Redact(ex.Message)); }
        }
    }
}
