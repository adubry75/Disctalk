using Disctalk.Core.Http;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Discord;
using DisctalkClient.Core.Security;
using DisctalkClient.Core.UserAccount;
using Microsoft.Extensions.Logging;

namespace DisctalkClient.Core;

/// <summary>
/// User accounts get <see cref="UserGatewayConnection"/> (the archiver's identity: header profile, super properties,
/// build number, rate limiting); bot accounts get NetCord.
/// </summary>
public sealed class DiscordConnectionFactory(ILoggerFactory? loggerFactory = null) : IDiscordConnectionFactory
{
    readonly Lock _lock = new();
    (HeaderProfile Profile, BuildNumberProvider BuildNumbers)? _identity;

    public IDiscordConnection Create(AccountKind kind, string token)
    {
        if (kind == AccountKind.Bot) return new NetCordConnection(token, loggerFactory);

        var (profile, buildNumbers) = Identity();
        var http = DiscordHttp.CreateClient(profile, buildNumbers, _ => ValueTask.FromResult<string?>(token),
            // RateLimitHandler logs only the route (ids, never the token) when it has to wait out a 429.
            rateLimits: new RateLimitHandler(new RateLimitOptions(), logger: loggerFactory?.CreateLogger<RateLimitHandler>()));
        return new UserGatewayConnection(token, profile, buildNumbers, http, logger: loggerFactory?.CreateLogger<UserGatewayConnection>());
    }

    /// <summary>Loaded once per process, exactly like the archiver: headerprofile.json (override file or embedded copy) + build number.</summary>
    (HeaderProfile, BuildNumberProvider) Identity()
    {
        lock (_lock)
        {
            if (_identity is { } existing) return existing;
            var profile = HeaderProfile.Load();
            var log = loggerFactory?.CreateLogger<DiscordConnectionFactory>();
            foreach (var problem in HeaderProfileValidator.Validate(profile))
                log?.LogWarning("Header profile warning ({Source}): {Problem}", profile.Source, SecretRedactor.Redact(problem));
            var buildNumbers = new BuildNumberProvider(profile, logger: loggerFactory?.CreateLogger<BuildNumberProvider>());
            _identity = (profile, buildNumbers);
            return (profile, buildNumbers);
        }
    }
}
