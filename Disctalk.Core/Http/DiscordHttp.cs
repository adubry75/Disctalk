using System.Net;
using Microsoft.Extensions.Logging;

namespace Disctalk.Core.Http;

/// <summary>Builds the HttpClient pipeline every Discord REST call goes through: headers → rate limits → network.</summary>
public static class DiscordHttp
{
    public const string ApiBase = "https://discord.com/api/v10";

    public static HttpClient CreateClient(HeaderProfile profile, BuildNumberProvider buildNumbers,
        Func<CancellationToken, ValueTask<string?>> tokenProvider, RateLimitHandler? rateLimits = null,
        HttpMessageHandler? network = null, ILoggerFactory? loggers = null)
    {
        network ??= new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        rateLimits ??= new RateLimitHandler(logger: loggers?.CreateLogger<RateLimitHandler>());
        rateLimits.InnerHandler = network;
        var headers = new DiscordHeadersHandler(profile, buildNumbers, tokenProvider) { InnerHandler = rateLimits };
        return new HttpClient(headers) { Timeout = TimeSpan.FromSeconds(100) };
    }
}
