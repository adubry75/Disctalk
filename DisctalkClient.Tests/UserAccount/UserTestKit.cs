using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Disctalk.Core.Http;
using DisctalkClient.Core.UserAccount;

namespace DisctalkClient.Tests.UserAccount;

/// <summary>Records every request and answers from a callback. No network.</summary>
public sealed class FakeHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage>? respond = null) : HttpMessageHandler
{
    public sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body, string? ContentType);

    public List<Sent> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        // NonValidated: the raw strings as added (a parsed User-Agent would come back re-joined with commas).
        var headers = request.Headers.NonValidated.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        lock (Requests) Requests.Add(new Sent(request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.MediaType));
        return respond?.Invoke(request, body) ?? Json("{}");
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>A scripted gateway socket: the test pushes payloads in and reads what the client sent.</summary>
public sealed class FakeTransport : IGatewayTransport
{
    readonly Channel<byte[]?> _inbound = Channel.CreateUnbounded<byte[]?>();
    readonly Channel<JsonNode> _outbound = Channel.CreateUnbounded<JsonNode>();

    readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri? ConnectedTo { get; private set; }
    /// <summary>Completes once the client called <see cref="ConnectAsync"/>.</summary>
    public Task Connected => _connected.Task;
    public int? CloseCode { get; set; }
    public int? ClosedByClientWith { get; private set; }
    public Exception? ConnectFailure { get; set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (ConnectFailure is not null) throw ConnectFailure;
        ConnectedTo = uri;
        _connected.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var next = await _inbound.Reader.ReadAsync(cancellationToken);
        // Careful: "next is null ? null : next" would convert null to an empty ReadOnlyMemory, not a null one.
        if (next is null) return null;
        return new ReadOnlyMemory<byte>(next);
    }

    public Task SendAsync(ReadOnlyMemory<byte> json, CancellationToken cancellationToken)
    {
        _outbound.Writer.TryWrite(JsonNode.Parse(json.Span)!);
        return Task.CompletedTask;
    }

    public Task CloseAsync(int code, CancellationToken cancellationToken)
    {
        ClosedByClientWith ??= code;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Push(object payload) => _inbound.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(payload));
    public void PushRaw(string json) => _inbound.Writer.TryWrite(Encoding.UTF8.GetBytes(json));
    public void Hello(int intervalMs = 1_000_000) => Push(new { op = 10, d = new { heartbeat_interval = intervalMs } });
    public void Dispatch(string type, int seq, object data) => Push(new { op = 0, s = seq, t = type, d = data });

    public void Close(int code)
    {
        CloseCode = code;
        _inbound.Writer.TryWrite(null);
    }

    /// <summary>The next payload the client sent (waits up to 5 s).</summary>
    public async Task<JsonNode> NextSentAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await _outbound.Reader.ReadAsync(timeout.Token);
    }

    public bool TryNextSent(out JsonNode? payload) => _outbound.Reader.TryRead(out payload);
}

public static class UserTestKit
{
    public const string UserToken = TestData.Token;
    public const ulong SelfId = 110000000000000100;

    public static HeaderProfile Profile { get; } = HeaderProfile.LoadEmbedded();

    /// <summary>Build numbers resolve to the profile's fallback: the scan is answered with 404 and nothing is cached.</summary>
    public static BuildNumberProvider BuildNumbers() => new(Profile,
        new FakeHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound)),
        cachePath: Path.Combine(Path.GetTempPath(), "disctalk-tests", Guid.NewGuid().ToString("N"), "build.json"));

    public static HttpClient Http(FakeHttpHandler network) =>
        DiscordHttp.CreateClient(Profile, BuildNumbers(), _ => ValueTask.FromResult<string?>(UserToken),
            new RateLimitHandler(new RateLimitOptions { MinimumInterval = TimeSpan.Zero }), network);

    public static object SelfUser => new { id = SelfId.ToString(), username = "whiskers", global_name = "Whiskers", avatar = (string?)null, discriminator = "0" };

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
}
