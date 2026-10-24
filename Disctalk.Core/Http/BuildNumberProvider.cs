using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Disctalk.Core.Http;

/// <summary>
/// Resolves the current Discord web <c>client_build_number</c>. Order: in-memory value, a disk cache younger
/// than <see cref="CacheLifetime"/>, a fresh scan of discord.com/app's script bundles, then the profile's fallback.
/// Never throws; a failed scan just means the fallback (or an older cached value) is used.
/// </summary>
public sealed partial class BuildNumberProvider
{
    public static readonly Uri AppUri = new("https://discord.com/app");

    readonly HeaderProfile _profile;
    readonly HttpMessageHandler _handler;
    readonly string? _cachePath;
    readonly TimeProvider _time;
    readonly ILogger _log;
    readonly SemaphoreSlim _gate = new(1, 1);
    int? _resolved;

    public BuildNumberProvider(HeaderProfile profile, HttpMessageHandler? handler = null, string? cachePath = null,
        TimeProvider? time = null, ILogger? logger = null)
    {
        _profile = profile;
        _handler = handler ?? new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All };
        _cachePath = cachePath ?? DefaultCachePath();
        _time = time ?? TimeProvider.System;
        _log = logger ?? NullLogger.Instance;
    }

    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromHours(12);

    /// <summary>Most script bundles fetched per scan. Keeps the scan polite.</summary>
    public int MaxAssetsToScan { get; init; } = 8;

    /// <summary>Where the last resolved value came from: memory, cache, network or fallback.</summary>
    public string? LastSource { get; private set; }

    public async Task<int> GetBuildNumberAsync(CancellationToken ct = default)
    {
        if (_resolved is int r) return r;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_resolved is int r2) return r2;

            var cached = ReadCache();
            if (cached is { } c && _time.GetUtcNow() - c.FetchedAt < CacheLifetime)
            {
                LastSource = "cache";
                return (_resolved = c.BuildNumber).Value;
            }

            int? fetched = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                fetched = await FetchAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _log.LogWarning("Could not fetch the Discord build number: {Message}", ex.Message);
            }

            if (fetched is int f)
            {
                WriteCache(f);
                LastSource = "network";
                return (_resolved = f).Value;
            }

            // A stale cached value is still newer than the profile's fallback.
            if (cached is { } stale && stale.BuildNumber > _profile.FallbackBuildNumber)
            {
                LastSource = "stale cache";
                return (_resolved = stale.BuildNumber).Value;
            }

            LastSource = "fallback";
            return (_resolved = _profile.FallbackBuildNumber).Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<int?> FetchAsync(CancellationToken ct)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        string html = await GetStringAsync(http, AppUri, "document", ct).ConfigureAwait(false);

        if (FindBuildNumber(html) is int inline) return inline;

        foreach (var asset in OrderAssets(ExtractScriptAssets(html)).Take(MaxAssetsToScan))
        {
            string js = await GetStringAsync(http, new Uri(AppUri, asset), "script", ct).ConfigureAwait(false);
            if (FindBuildNumber(js) is int n) return n;
        }
        return null;
    }

    async Task<string> GetStringAsync(HttpClient http, Uri uri, string dest, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("User-Agent", _profile.UserAgent);
        if (_profile.GetHeader("Accept-Language") is { } lang) req.Headers.TryAddWithoutValidation("Accept-Language", lang);
        req.Headers.TryAddWithoutValidation("Accept", dest == "document" ? "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8" : "*/*");
        foreach (var name in new[] { "Sec-Ch-Ua", "Sec-Ch-Ua-Mobile", "Sec-Ch-Ua-Platform" })
            if (_profile.GetHeader(name) is { } v) req.Headers.TryAddWithoutValidation(name, v);
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", dest);
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", dest == "document" ? "navigate" : "no-cors");
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", dest == "document" ? "none" : "same-origin");
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Script bundle paths referenced by the app HTML, in document order.</summary>
    public static IReadOnlyList<string> ExtractScriptAssets(string html) =>
        ScriptSrcRegex().Matches(html).Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>The build number is usually in the main "web." bundle or the sentry bundle; try those first, then the rest last-to-first.</summary>
    internal static IEnumerable<string> OrderAssets(IReadOnlyList<string> assets)
    {
        static bool Preferred(string a)
        {
            var name = a[(a.LastIndexOf('/') + 1)..];
            return name.StartsWith("web.", StringComparison.Ordinal) || name.StartsWith("sentry.", StringComparison.Ordinal);
        }
        return assets.Where(Preferred).Concat(assets.Where(a => !Preferred(a)).Reverse());
    }

    public static int? FindBuildNumber(string text)
    {
        foreach (var regex in new[] { BuildNumberRegex(), BuildNumberCallRegex() })
        {
            var m = regex.Match(text);
            if (m.Success && int.TryParse(m.Groups["n"].Value, out int n) && n > 0) return n;
        }
        return null;
    }

    sealed record CacheEntry(int BuildNumber, DateTimeOffset FetchedAt);

    CacheEntry? ReadCache()
    {
        try
        {
            if (_cachePath == null || !File.Exists(_cachePath)) return null;
            return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(_cachePath));
        }
        catch (Exception ex)
        {
            _log.LogDebug("Ignoring unreadable build number cache: {Message}", ex.Message);
            return null;
        }
    }

    void WriteCache(int buildNumber)
    {
        if (_cachePath == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(new CacheEntry(buildNumber, _time.GetUtcNow())));
        }
        catch (Exception ex)
        {
            _log.LogDebug("Could not write build number cache: {Message}", ex.Message);
        }
    }

    static string? DefaultCachePath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "Disctalk", "build-number.json");
    }

    [GeneratedRegex(@"<script[^>]+src=""(/assets/[^""]+\.js)""", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrcRegex();

    // build_number:"649389", buildNumber:649389, "BUILD_NUMBER":"649389", buildNumber = "649389"
    [GeneratedRegex(@"(?:build_number|buildNumber|BUILD_NUMBER)[""']?\s*[:=]\s*[""']?(?<n>\d{4,9})")]
    private static partial Regex BuildNumberRegex();

    // e.g. ("buildNumber","649389") as emitted by some bundles
    [GeneratedRegex(@"[""']buildNumber[""']\s*,\s*[""']?(?<n>\d{4,9})")]
    private static partial Regex BuildNumberCallRegex();
}
