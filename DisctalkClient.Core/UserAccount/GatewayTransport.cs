using System.IO.Compression;
using System.Net.WebSockets;
using Disctalk.Core.Http;

namespace DisctalkClient.Core.UserAccount;

/// <summary>One gateway WebSocket. Abstracted so the protocol logic in <see cref="UserGatewayConnection"/> is testable.</summary>
public interface IGatewayTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>The next complete (decompressed) JSON payload, or null once the socket closed.</summary>
    Task<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken);

    Task SendAsync(ReadOnlyMemory<byte> json, CancellationToken cancellationToken);

    Task CloseAsync(int code, CancellationToken cancellationToken);

    /// <summary>The close code Discord sent (4004 = bad token, …), once closed.</summary>
    int? CloseCode { get; }
}

/// <summary>
/// Discord's <c>compress=zlib-stream</c>: one zlib context for the whole connection; each payload ends with a
/// Z_SYNC_FLUSH marker (00 00 FF FF), possibly spread over several WebSocket frames.
/// </summary>
public sealed class ZlibStreamDecoder : IDisposable
{
    static ReadOnlySpan<byte> SyncFlushSuffix => [0x00, 0x00, 0xFF, 0xFF];

    readonly FeedStream _input = new();
    readonly ZLibStream _inflater;
    readonly MemoryStream _pending = new();
    readonly byte[] _buffer = new byte[64 * 1024];

    public ZlibStreamDecoder() => _inflater = new ZLibStream(_input, CompressionMode.Decompress);

    /// <summary>Adds received bytes. Returns the decompressed payload once a complete one has arrived, else null.</summary>
    public byte[]? Push(ReadOnlySpan<byte> data)
    {
        _pending.Write(data);
        if (_pending.Length < 4 || !_pending.GetBuffer().AsSpan(0, (int)_pending.Length).EndsWith(SyncFlushSuffix)) return null;

        _input.Feed(_pending.ToArray());
        _pending.SetLength(0);
        using var output = new MemoryStream();
        int n;
        while ((n = _inflater.Read(_buffer)) > 0) output.Write(_buffer, 0, n);
        return output.ToArray();
    }

    public void Dispose()
    {
        _inflater.Dispose();
        _pending.Dispose();
    }

    /// <summary>Hands the inflater exactly the bytes fed so far, then reports "no more data" until fed again.</summary>
    sealed class FeedStream : Stream
    {
        byte[] _data = [];
        int _position;

        public void Feed(byte[] data)
        {
            _data = data;
            _position = 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int n = Math.Min(buffer.Length, _data.Length - _position);
            _data.AsSpan(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// <see cref="ClientWebSocket"/> transport. The handshake carries the same browser identity as REST (User-Agent,
/// Accept-Language, Origin from the archiver's header profile).
/// </summary>
public sealed class WebSocketGatewayTransport(HeaderProfile profile) : IGatewayTransport
{
    readonly ClientWebSocket _socket = new();
    readonly ZlibStreamDecoder _zlib = new();
    readonly byte[] _buffer = new byte[16 * 1024];

    public int? CloseCode { get; private set; }

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("User-Agent", profile.UserAgent);
        _socket.Options.SetRequestHeader("Origin", profile.GetHeader("Origin") ?? "https://discord.com");
        if (profile.GetHeader("Accept-Language") is { } lang) _socket.Options.SetRequestHeader("Accept-Language", lang);
        _socket.Options.KeepAliveInterval = TimeSpan.Zero; // Discord's own heartbeat (op 1) keeps the connection alive.
        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReadOnlyMemory<byte>?> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                CloseCode = (int?)_socket.CloseStatus;
                return null;
            }
            if (result.MessageType == WebSocketMessageType.Binary)
            {
                if (_zlib.Push(_buffer.AsSpan(0, result.Count)) is { } payload) return payload;
                continue; // a zlib payload can span several messages; keep reading until the flush marker
            }
            message.Write(_buffer, 0, result.Count);
            if (result.EndOfMessage) return message.ToArray();
        }
    }

    public Task SendAsync(ReadOnlyMemory<byte> json, CancellationToken cancellationToken) =>
        _socket.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

    public async Task CloseAsync(int code, CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _socket.CloseOutputAsync((WebSocketCloseStatus)code, "", cancellationToken).ConfigureAwait(false); }
            catch (WebSocketException) { }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _zlib.Dispose();
        return ValueTask.CompletedTask;
    }
}
