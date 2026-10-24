using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Disctalk.Core.Http;

public sealed class RateLimitOptions
{
    /// <summary>Minimum gap between any two requests, regardless of bucket. Real account: keep this conservative.</summary>
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How many times a 429 is retried (after waiting Retry-After) before it is handed back to the caller.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Retry-After values longer than this are not waited out; the 429 is returned instead.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Extra margin added to every wait so we never fire right on the reset boundary.</summary>
    public TimeSpan SafetyMargin { get; init; } = TimeSpan.FromMilliseconds(100);
}

/// <summary>
/// Honors Discord's per-route rate-limit buckets (X-RateLimit-Bucket / Remaining / Reset-After), Retry-After on 429,
/// and global limits. Requests in the same bucket are serialized, and every request is spaced by
/// <see cref="RateLimitOptions.MinimumInterval"/>.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    readonly RateLimitOptions _options;
    readonly TimeProvider _time;
    readonly ILogger _log;
    readonly ConcurrentDictionary<string, string> _routeToBucketHash = new();
    readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    readonly SemaphoreSlim _spacing = new(1, 1);
    DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;
    DateTimeOffset _globalResetAt = DateTimeOffset.MinValue;

    public RateLimitHandler(RateLimitOptions? options = null, TimeProvider? time = null, ILogger? logger = null)
    {
        _options = options ?? new RateLimitOptions();
        _time = time ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
    }

    /// <summary>Raised whenever the handler decides to wait. For diagnostics and tests.</summary>
    public event Action<string, TimeSpan>? Waiting;

    internal sealed class Bucket
    {
        public readonly SemaphoreSlim Lock = new(1, 1);
        public int? Remaining;
        public DateTimeOffset ResetAt = DateTimeOffset.MinValue;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!DiscordHeadersHandler.IsDiscordApiHost(request.RequestUri))
            return await base.SendAsync(request, ct).ConfigureAwait(false);

        var (route, major) = GetRoute(request.Method, request.RequestUri!);
        var bucket = GetBucket(route, major);

        if (request.Content != null) await request.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);

        await bucket.Lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                await WaitForGlobalAsync(ct).ConfigureAwait(false);

                var now = _time.GetUtcNow();
                if (bucket.Remaining == 0 && bucket.ResetAt > now)
                    await DelayAsync(route, bucket.ResetAt - now + _options.SafetyMargin, ct).ConfigureAwait(false);

                await SpaceRequestsAsync(route, ct).ConfigureAwait(false);

                var toSend = attempt == 0 ? request : await CloneAsync(request).ConfigureAwait(false);
                var response = await base.SendAsync(toSend, ct).ConfigureAwait(false);

                Update(route, major, bucket, response);

                if (response.StatusCode != HttpStatusCode.TooManyRequests) return response;

                var (retryAfter, isGlobal) = await ReadRetryAfterAsync(response, ct).ConfigureAwait(false);
                var resetAt = _time.GetUtcNow() + retryAfter;
                if (isGlobal) _globalResetAt = resetAt;
                else { bucket.Remaining = 0; bucket.ResetAt = resetAt; }

                _log.LogWarning("429 on {Route} ({Scope}); retry after {Seconds:F2}s (attempt {Attempt})",
                    route, isGlobal ? "global" : "bucket", retryAfter.TotalSeconds, attempt + 1);

                if (attempt >= _options.MaxRetries || retryAfter > _options.MaxRetryAfter) return response;
                response.Dispose();
            }
        }
        finally
        {
            bucket.Lock.Release();
        }
    }

    Bucket GetBucket(string route, string major)
    {
        string key = _routeToBucketHash.TryGetValue(route, out var hash) ? hash + ":" + major : route;
        return _buckets.GetOrAdd(key, _ => new Bucket());
    }

    void Update(string route, string major, Bucket bucket, HttpResponseMessage response)
    {
        var h = response.Headers;
        if (Header(h, "X-RateLimit-Bucket") is { } hash)
        {
            _routeToBucketHash[route] = hash;
            // Later requests on this route resolve to the hash-keyed bucket; seed it with what we just learned.
            var shared = _buckets.GetOrAdd(hash + ":" + major, _ => bucket);
            if (!ReferenceEquals(shared, bucket)) Apply(shared, h);
        }
        Apply(bucket, h);
    }

    void Apply(Bucket bucket, System.Net.Http.Headers.HttpResponseHeaders h)
    {
        if (Header(h, "X-RateLimit-Remaining") is { } rem && int.TryParse(rem, NumberStyles.Integer, CultureInfo.InvariantCulture, out int remaining))
            bucket.Remaining = remaining;
        if (Header(h, "X-RateLimit-Reset-After") is { } ra && double.TryParse(ra, NumberStyles.Float, CultureInfo.InvariantCulture, out double resetAfter))
            bucket.ResetAt = _time.GetUtcNow() + TimeSpan.FromSeconds(resetAfter);
    }

    static string? Header(System.Net.Http.Headers.HttpResponseHeaders headers, string name) =>
        headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    async Task<(TimeSpan RetryAfter, bool Global)> ReadRetryAfterAsync(HttpResponseMessage response, CancellationToken ct)
    {
        double? seconds = null;
        bool global = string.Equals(Header(response.Headers, "X-RateLimit-Global"), "true", StringComparison.OrdinalIgnoreCase);

        // The JSON body has the precise (fractional) value; Retry-After is whole seconds.
        try
        {
            await response.Content.LoadIntoBufferAsync(ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("retry_after", out var ra) && ra.TryGetDouble(out var d)) seconds = d;
            if (doc.RootElement.TryGetProperty("global", out var g) && g.ValueKind == JsonValueKind.True) global = true;
        }
        catch (JsonException) { /* Cloudflare HTML page or empty body */ }

        if (response.Headers.RetryAfter is { } retryAfterHeader)
        {
            var headerSeconds = retryAfterHeader.Delta?.TotalSeconds
                ?? (retryAfterHeader.Date is { } date ? (date - _time.GetUtcNow()).TotalSeconds : (double?)null);
            if (headerSeconds is double hs) seconds = Math.Max(seconds ?? 0, hs);
        }

        // No information at all (e.g. a Cloudflare ban page): back off hard rather than hammering.
        return (TimeSpan.FromSeconds(seconds ?? 60) + _options.SafetyMargin, global);
    }

    async Task WaitForGlobalAsync(CancellationToken ct)
    {
        var wait = _globalResetAt - _time.GetUtcNow();
        if (wait > TimeSpan.Zero) await DelayAsync("global", wait, ct).ConfigureAwait(false);
    }

    async Task SpaceRequestsAsync(string route, CancellationToken ct)
    {
        await _spacing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestAt + _options.MinimumInterval - _time.GetUtcNow();
            if (wait > TimeSpan.Zero) await DelayAsync(route, wait, ct).ConfigureAwait(false);
            _lastRequestAt = _time.GetUtcNow();
        }
        finally
        {
            _spacing.Release();
        }
    }

    /// <summary>Test hook: replaces the actual wait (e.g. to advance a fake clock instantly).</summary>
    internal Func<TimeSpan, CancellationToken, Task>? DelayOverride { get; set; }

    Task DelayAsync(string route, TimeSpan wait, CancellationToken ct)
    {
        Waiting?.Invoke(route, wait);
        return DelayOverride?.Invoke(wait, ct) ?? Task.Delay(wait, _time, ct);
    }

    static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version, VersionPolicy = original.VersionPolicy };
        foreach (var header in original.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in original.Options) ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        if (original.Content != null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    /// <summary>
    /// Normalizes a request into Discord's rate-limit route: top-level resource IDs (channel, guild, webhook) are
    /// "major parameters" and stay distinct; other IDs collapse to {id}, and every reaction route shares one key.
    /// </summary>
    public static (string Route, string Major) GetRoute(HttpMethod method, Uri uri)
    {
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        int start = 0;
        if (segments.Length > 1 && segments[0] == "api" && segments[1].StartsWith('v')) start = 2;

        var sb = new StringBuilder(method.Method).Append(' ');
        string major = "";
        for (int i = start; i < segments.Length; i++)
        {
            string seg = segments[i];
            string prev = i > start ? segments[i - 1] : "";
            if (seg == "reactions") { sb.Append("/reactions/*"); break; }
            if (IsId(seg))
            {
                if (i == start + 1 && prev is "channels" or "guilds" or "webhooks")
                {
                    major = seg;
                    sb.Append('/').Append(seg);
                }
                else sb.Append("/{id}");
            }
            else sb.Append('/').Append(seg);
        }
        return (sb.ToString(), major);
    }

    static bool IsId(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
}
