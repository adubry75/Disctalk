using System.Globalization;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Tests;

/// <summary>Builders for Discord-shaped test data. Ids encode timestamps, like real snowflakes.</summary>
public static class TestData
{
    /// <summary>Shaped like a real bot token (passes validation); not a real credential.</summary>
    public const string Token = "MTEwMDAwMDAwMDAwMDAwMTAw.TESTXX.test-token-not-real-test-token-not-real";

    public const ulong GuildId = 900000000000000001;
    public const ulong GeneralId = 900000000000000010;
    public const ulong RandomId = 900000000000000011;
    public const ulong CategoryId = 900000000000000020;
    public const ulong BotId = 900000000000000100;
    public const ulong AdaId = 900000000000000101;
    public const ulong BobId = 900000000000000102;
    public const ulong ModRoleId = 900000000000000300;

    public static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>2026-10-24 15:04:05 UTC — a Saturday afternoon.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 15, 4, 5, TimeSpan.Zero);

    public static AuthorInfo Bot => new() { Id = BotId, Username = "whiskers", GlobalName = "Whiskers", AvatarUrl = "bot.png", IsBot = true };
    public static AuthorInfo Ada => new() { Id = AdaId, Username = "ada", GlobalName = "Ada", AvatarUrl = "ada.png" };
    public static AuthorInfo Bob => new() { Id = BobId, Username = "bob", AvatarUrl = "bob.png" };

    public static AuthorInfo Webhook(ulong id, string name = "Andre", string avatar = "andre.png") =>
        new() { Id = id, Username = name, AvatarUrl = avatar, IsBot = true, IsWebhook = true };

    static ulong _sequence;

    public static ulong IdAt(DateTimeOffset at) => Snowflakes.FromTimestamp(at) + Interlocked.Increment(ref _sequence) % 4096;

    public static ChatMessage Message(string content = "hello", AuthorInfo? author = null, DateTimeOffset? at = null, ulong channelId = GeneralId, ulong? id = null) => new()
    {
        Id = id ?? IdAt(at ?? Now.AddMinutes(-1)),
        ChannelId = channelId,
        GuildId = GuildId,
        Author = author ?? Ada,
        Content = content,
    };

    public static MemberInfo Member(ulong id, string username, string? nick = null, params ulong[] roles) => new()
    {
        UserId = id, GuildId = GuildId, Username = username, Nickname = nick, AvatarUrl = $"{username}.png", RoleIds = roles,
    };

    public static GuildSnapshot Guild() => new(
        new GuildInfo(GuildId, "Test Café", null),
        [
            new() { Id = CategoryId, GuildId = GuildId, Name = "Chat", Kind = ChannelKind.Category },
            new() { Id = GeneralId, GuildId = GuildId, Name = "general", ParentId = CategoryId, Position = 0, LastMessageId = 1 },
            new() { Id = RandomId, GuildId = GuildId, Name = "random", ParentId = CategoryId, Position = 1, LastMessageId = 1 },
        ],
        [
            new() { Id = GuildId, GuildId = GuildId, Name = "@everyone" },
            new() { Id = ModRoleId, GuildId = GuildId, Name = "Moderators", Color = 0x6CB4EE, Position = 2, Hoist = true },
        ],
        [
            Member(BotId, "whiskers", "Whiskers"),
            Member(AdaId, "ada", "Ada Lovelace", ModRoleId),
            Member(BobId, "bob"),
        ],
        [new GuildEmojiInfo(900000000000000500, GuildId, "blobcat", false)]);
}

/// <summary>A gateway connection the test drives by hand; REST is an NSubstitute mock.</summary>
public sealed class FakeConnection : IDiscordConnection
{
    public event Func<GatewayEvent, ValueTask>? EventReceived;

    public AccountKind Kind { get; set; } = AccountKind.User;
    public IDiscordRestApi Rest { get; } = Substitute.For<IDiscordRestApi>();
    public List<(ulong Guild, ulong Channel)> Focused { get; } = new();

    public ValueTask FocusChannelAsync(ulong guildId, ulong channelId)
    {
        lock (Focused) Focused.Add((guildId, channelId));
        return ValueTask.CompletedTask;
    }
    public bool Started { get; private set; }
    public Exception? StartFailure { get; set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (StartFailure is not null) throw StartFailure;
        Started = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public async Task RaiseAsync(params GatewayEvent[] events)
    {
        foreach (var e in events)
            if (EventReceived is { } handler) await handler(e);
    }

    /// <summary>Ready + the test guild, i.e. what a fresh login looks like.</summary>
    public Task LoginAsync() => RaiseAsync(new ReadyEvent(TestData.Bot), new GuildAvailableEvent(TestData.Guild()));
}

public sealed class FakeConnectionFactory : IDiscordConnectionFactory
{
    public FakeConnection Connection { get; private set; } = new();
    public string? LastToken { get; private set; }
    public AccountKind? LastKind { get; private set; }

    public IDiscordConnection Create(AccountKind kind, string token)
    {
        LastToken = token;
        LastKind = kind;
        Connection.Kind = kind;
        return Connection;
    }

    public void Reset() => Connection = new FakeConnection();
}
