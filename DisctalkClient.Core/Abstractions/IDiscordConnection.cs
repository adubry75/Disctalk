using System.Text.Json.Serialization;

namespace DisctalkClient.Core.Abstractions;

/// <summary>Which kind of Discord account the client signs in as.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountKind>))]
public enum AccountKind
{
    /// <summary>Your own user account, with the archiver's browser identity (header profile + super properties).</summary>
    User,
    /// <summary>A bot account (catbot) through NetCord and the documented bot API.</summary>
    Bot,
}

/// <summary>One live session: a gateway connection plus the REST client that goes with the same token.</summary>
public interface IDiscordConnection : IAsyncDisposable
{
    event Func<GatewayEvent, ValueTask>? EventReceived;

    AccountKind Kind { get; }

    IDiscordRestApi Rest { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>
    /// The user opened <paramref name="channelId"/> in <paramref name="guildId"/>. User accounts only get typing events and
    /// the member list for guilds they subscribe to, so the user gateway subscribes here (once per guild/channel).
    /// Bots receive everything their intents allow and ignore this.
    /// </summary>
    ValueTask FocusChannelAsync(ulong guildId, ulong channelId) => ValueTask.CompletedTask;
}

public interface IDiscordConnectionFactory
{
    /// <summary>Creates (but does not start) a connection. The token stays inside the connection; it is never logged.</summary>
    IDiscordConnection Create(AccountKind kind, string token);
}
