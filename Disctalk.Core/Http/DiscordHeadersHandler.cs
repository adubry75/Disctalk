namespace Disctalk.Core.Http;

/// <summary>
/// Adds the header profile (plus Authorization and X-Super-Properties) to every request bound for discord.com.
/// Headers already set on a request win. Requests to other hosts (CDN, media proxy) are left alone apart from User-Agent.
/// </summary>
public sealed class DiscordHeadersHandler : DelegatingHandler
{
    readonly HeaderProfile _profile;
    readonly BuildNumberProvider _buildNumbers;
    readonly Func<CancellationToken, ValueTask<string?>> _tokenProvider;
    string? _superPropertiesHeader;

    public DiscordHeadersHandler(HeaderProfile profile, BuildNumberProvider buildNumbers, Func<CancellationToken, ValueTask<string?>> tokenProvider)
    {
        _profile = profile;
        _buildNumbers = buildNumbers;
        _tokenProvider = tokenProvider;
    }

    public static bool IsDiscordApiHost(Uri? uri) =>
        uri is { Host: "discord.com" or "ptb.discord.com" or "canary.discord.com" };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!IsDiscordApiHost(request.RequestUri))
        {
            if (!request.Headers.Contains("User-Agent")) request.Headers.TryAddWithoutValidation("User-Agent", _profile.UserAgent);
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        foreach (var (name, value) in _profile.Headers)
        {
            if (!request.Headers.Contains(name)) request.Headers.TryAddWithoutValidation(name, value);
        }

        if (!request.Headers.Contains("X-Super-Properties"))
        {
            _superPropertiesHeader ??= HeaderProfile.EncodeSuperProperties(
                _profile.BuildSuperProperties(await _buildNumbers.GetBuildNumberAsync(ct).ConfigureAwait(false)));
            request.Headers.TryAddWithoutValidation("X-Super-Properties", _superPropertiesHeader);
        }

        if (!request.Headers.Contains("Authorization") && await _tokenProvider(ct).ConfigureAwait(false) is { Length: > 0 } token)
        {
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }
}
