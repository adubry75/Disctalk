using DisctalkClient.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DisctalkClient.Core.Discord;

public sealed class NetCordConnectionFactory(ILoggerFactory? loggerFactory = null) : IDiscordConnectionFactory
{
    public IDiscordConnection Create(string botToken) => new NetCordConnection(botToken, loggerFactory);
}
