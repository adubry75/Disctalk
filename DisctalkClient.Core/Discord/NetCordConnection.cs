using System.Net.WebSockets;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace DisctalkClient.Core.Discord;

/// <summary>A NetCord <see cref="GatewayClient"/> (bot token, documented gateway + REST only) behind <see cref="IDiscordConnection"/>.</summary>
public sealed class NetCordConnection : IDiscordConnection
{
    /// <summary>
    /// Guilds, GuildMessages, MessageContent (privileged), GuildUsers = GUILD_MEMBERS (privileged), reactions, typing.
    /// GuildEmojisAndStickers keeps the custom emoji picker current when emoji are added or removed.
    /// </summary>
    public const GatewayIntents Intents =
        GatewayIntents.Guilds
        | GatewayIntents.GuildMessages
        | GatewayIntents.MessageContent
        | GatewayIntents.GuildUsers
        | GatewayIntents.GuildMessageReactions
        | GatewayIntents.GuildMessageTyping
        | GatewayIntents.GuildEmojisAndStickers;

    readonly GatewayClient _client;
    readonly ILogger _log;

    public NetCordConnection(string botToken, ILoggerFactory? loggerFactory = null)
    {
        _log = loggerFactory?.CreateLogger<NetCordConnection>() ?? (ILogger)NullLogger.Instance;
        var gatewayLogger = new RedactingNetCordLogger(loggerFactory?.CreateLogger("NetCord.Gateway") ?? NullLogger.Instance);
        var restLogger = new RedactingNetCordLogger(loggerFactory?.CreateLogger("NetCord.Rest") ?? NullLogger.Instance);

        _client = new GatewayClient(new BotToken(botToken), new GatewayClientConfiguration
        {
            Intents = Intents,
            Logger = gatewayLogger,
            RestClientConfiguration = new RestClientConfiguration { Logger = restLogger },
        });
        Rest = new NetCordBotRestApi(_client.Rest);
        Wire();
    }

    public event Func<GatewayEvent, ValueTask>? EventReceived;

    public AccountKind Kind => AccountKind.Bot;

    public IDiscordRestApi Rest { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default) =>
        await _client.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

    public async Task StopAsync()
    {
        if (_client.Status != WebSocketStatus.Disconnected)
            await _client.CloseAsync(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    async ValueTask Emit(GatewayEvent e)
    {
        if (EventReceived is { } handler) await handler(e).ConfigureAwait(false);
    }

    void Wire()
    {
        var c = _client;
        c.Connecting += () => Emit(new ConnectionStateChangedEvent(ConnectionState.Connecting, "Connecting to Discord…"));
        c.Resume += () => Emit(new ConnectionStateChangedEvent(ConnectionState.Connected));
        c.Disconnect += args => Emit(new ConnectionStateChangedEvent(
            args.Reconnect ? ConnectionState.Reconnecting : ConnectionState.Failed, DescribeClose(args)));
        c.Ready += args => Emit(new ReadyEvent(NetCordMapper.ToAuthor(args.User)));

        c.GuildCreate += async args =>
        {
            if (args.Guild is not { } guild) return;
            await Emit(new GuildAvailableEvent(NetCordMapper.ToSnapshot(guild))).ConfigureAwait(false);
            // Large guilds only include a slice of members in GUILD_CREATE; ask for the rest (needs GUILD_MEMBERS).
            if (guild.Users.Count < guild.UserCount)
            {
                try { await c.RequestGuildUsersAsync(new GuildUsersRequestProperties(guild.Id).WithQuery("").WithLimit(0)).ConfigureAwait(false); }
                catch (Exception ex) { _log.LogWarning("Member request failed: {Error}", SecretRedactor.Redact(ex.Message)); }
            }
        };
        c.GuildUpdate += guild => Emit(new GuildUpdatedEvent(NetCordMapper.ToGuild(guild)));
        c.GuildDelete += args => Emit(new GuildRemovedEvent(args.GuildId));

        c.GuildChannelCreate += ch => NetCordMapper.ToChannel(ch) is { } info ? Emit(new ChannelUpsertEvent(info)) : ValueTask.CompletedTask;
        c.GuildChannelUpdate += ch => NetCordMapper.ToChannel(ch) is { } info ? Emit(new ChannelUpsertEvent(info)) : ValueTask.CompletedTask;
        c.GuildChannelDelete += ch => Emit(new ChannelDeleteEvent(ch.GuildId, ch.Id));

        c.RoleCreate += role => Emit(new RoleUpsertEvent(NetCordMapper.ToRole(role)));
        c.RoleUpdate += role => Emit(new RoleUpsertEvent(NetCordMapper.ToRole(role)));
        c.RoleDelete += args => Emit(new RoleDeleteEvent(args.GuildId, args.RoleId));

        c.GuildUserAdd += user => Emit(new MembersUpsertEvent(user.GuildId, [NetCordMapper.ToMember(user)]));
        c.GuildUserUpdate += user => Emit(new MembersUpsertEvent(user.GuildId, [NetCordMapper.ToMember(user)]));
        c.GuildUserRemove += args => Emit(new MemberRemoveEvent(args.GuildId, args.User.Id));
        c.GuildUserChunk += args => Emit(new MembersUpsertEvent(args.GuildId, args.Users.Select(NetCordMapper.ToMember).ToList()));
        c.GuildEmojisUpdate += args => Emit(new EmojisUpdateEvent(args.GuildId, args.Emojis.Values.Select(NetCordMapper.ToEmoji).ToList()));

        c.MessageCreate += m => Emit(new MessageCreateEvent(NetCordMapper.ToMessage(m)));
        c.MessageUpdate += m => Emit(new MessageUpdateEvent(NetCordMapper.ToMessage(m)));
        c.MessageDelete += args => Emit(new MessageDeleteEvent(args.ChannelId, [args.MessageId]));
        c.MessageDeleteBulk += args => Emit(new MessageDeleteEvent(args.ChannelId, args.MessageIds));

        c.MessageReactionAdd += args => Emit(new ReactionAddEvent(args.ChannelId, args.MessageId, args.UserId, NetCordMapper.ToEmojiRef(args.Emoji)));
        c.MessageReactionRemove += args => Emit(new ReactionRemoveEvent(args.ChannelId, args.MessageId, args.UserId, NetCordMapper.ToEmojiRef(args.Emoji)));
        c.MessageReactionRemoveAll += args => Emit(new ReactionClearEvent(args.ChannelId, args.MessageId, null));
        c.MessageReactionRemoveEmoji += args => Emit(new ReactionClearEvent(args.ChannelId, args.MessageId, NetCordMapper.ToEmojiRef(args.Emoji)));

        c.TypingStart += args => Emit(new TypingStartEvent(args.ChannelId, args.UserId,
            args.User is { } member ? NetCordMapper.ToMember(member) : null, args.Timestamp));
    }

    /// <summary>Plain-language reasons for the close codes a misconfigured bot actually hits.</summary>
    internal static string DescribeClose(DisconnectEventArgs args) => (int?)args.CloseStatus switch
    {
        4004 => "Discord rejected the bot token. Reset it in the Developer Portal and enter the new one.",
        4013 => "Invalid intents requested.",
        4014 => "Discord refused the privileged intents. In Developer Portal → Bot, enable Server Members Intent and Message Content Intent.",
        _ when args.Reconnect => "Connection lost — reconnecting…",
        _ => "Disconnected" + (string.IsNullOrEmpty(args.CloseStatusDescription) ? "." : $": {SecretRedactor.Redact(args.CloseStatusDescription)}"),
    };
}
