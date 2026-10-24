namespace DisctalkClient.Core.Abstractions;

/// <summary>One live bot session: a gateway connection plus the REST client that goes with the same token.</summary>
public interface IDiscordConnection : IAsyncDisposable
{
    event Func<GatewayEvent, ValueTask>? EventReceived;

    IBotRestApi Rest { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}

public interface IDiscordConnectionFactory
{
    /// <summary>Creates (but does not start) a connection. The token stays inside the connection; it is never logged.</summary>
    IDiscordConnection Create(string botToken);
}
