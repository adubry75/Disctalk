using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Archive;
using DisctalkClient.Core.Models;
using MySqlConnector;

namespace DisctalkClient.Tests.Archive;

/// <summary>
/// Runs the real SQL against a MySQL/MariaDB server with tables shaped like the archiver's. Opt-in: set
/// DISCTALK_TEST_MYSQL to a connection string for a throwaway database (the test drops and recreates its tables).
/// </summary>
public sealed class MySqlArchiveIntegrationTests
{
    static readonly string? ConnectionString = Environment.GetEnvironmentVariable("DISCTALK_TEST_MYSQL");

    static ArchiveConnection Connection()
    {
        var b = new MySqlConnectionStringBuilder(ConnectionString!);
        return new ArchiveConnection(b.Server, b.Port, b.Database, b.UserID, b.Password);
    }

    static async Task SeedAsync()
    {
        await using var conn = new MySqlConnection(ConnectionString);
        await conn.OpenAsync();
        var at = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero);
        ulong Id(int minutes) => Snowflakes.FromTimestamp(at.AddMinutes(minutes));
        string sql = $$"""
            DROP TABLE IF EXISTS messages; DROP TABLE IF EXISTS channels; DROP TABLE IF EXISTS usersServerInfo;
            CREATE TABLE messages (messageId BIGINT PRIMARY KEY, channelId BIGINT, authorId BIGINT, authorUsername VARCHAR(100),
                content TEXT, timestamp DATETIME, json LONGTEXT, lastUpdated DATETIME);
            CREATE TABLE channels (serverId BIGINT, channelId BIGINT PRIMARY KEY, type INT, lastMessageId BIGINT, name VARCHAR(100),
                rateLimit INT, topic TEXT, position INT, rawJson LONGTEXT);
            CREATE TABLE usersServerInfo (serverId BIGINT, userId BIGINT, username VARCHAR(100), joinDate DATETIME, nick VARCHAR(100),
                lastModified DATETIME, rawJson LONGTEXT, globalName VARCHAR(100));
            INSERT INTO channels (serverId, channelId, type, name) VALUES (1, 10, 0, 'general'), (1, 11, 0, 'random'), (2, 20, 0, 'general');
            INSERT INTO usersServerInfo (serverId, userId, username, nick, globalName) VALUES (1, 101, 'ada', 'Countess', 'Ada');
            INSERT INTO messages (messageId, channelId, authorId, authorUsername, content, timestamp, json) VALUES
                ({{Id(0)}}, 10, 101, 'ada', 'the cat is a loaf', NOW(), NULL),
                ({{Id(1)}}, 10, 102, 'bob', 'no cat here, just 100% dog_stuff', NOW(), NULL),
                ({{Id(2)}}, 11, 101, 'ada', 'another CAT pic https://example.com/cat.png', NOW(),
                    '{"id":"{{Id(2)}}","channel_id":"11","type":0,"content":"another CAT pic https://example.com/cat.png","author":{"id":"101","username":"ada","global_name":"Ada"},"attachments":[]}'),
                ({{Id(60 * 24 * 40)}}, 10, 101, 'ada', 'cat, much later', NOW(), NULL),
                ({{Id(3)}}, 20, 101, 'ada', 'cat on the other server', NOW(), NULL);
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<IReadOnlyList<string>> Search(ArchiveSearchService service, string text, ulong? server = null) =>
        (await service.SearchAsync(text, server)).Hits.Select(h => h.Message.Content).ToList();

    [Fact]
    public async Task Search_runs_against_a_real_server()
    {
        Assert.SkipWhen(string.IsNullOrEmpty(ConnectionString), "Set DISCTALK_TEST_MYSQL to run against a real MySQL/MariaDB.");
        await SeedAsync();
        var service = new ArchiveSearchService(new InMemorySecretStore(), new MySqlArchiveDatabase());
        Assert.Null(await service.TestAsync(Connection()));
        await service.SaveConnectionAsync(Connection());

        Assert.Equal(["cat, much later", "another CAT pic https://example.com/cat.png", "no cat here, just 100% dog_stuff", "the cat is a loaf"],
            await Search(service, "cat", server: 1));
        Assert.Equal(5, (await Search(service, "cat")).Count);
        Assert.Equal(["no cat here, just 100% dog_stuff"], await Search(service, "100% dog_"));
        Assert.Empty(await Search(service, "100%%"));
        Assert.Equal(["another CAT pic https://example.com/cat.png"], await Search(service, "cat has:link in:random"));
        Assert.Equal(4, (await Search(service, "cat from:Countess")).Count);   // nickname
        Assert.Equal(4, (await Search(service, "cat from:ad")).Count);         // username prefix
        Assert.Equal(["no cat here, just 100% dog_stuff"], await Search(service, "from:102"));
        Assert.Equal(["cat, much later"], await Search(service, "cat after:2025-03-15"));
        Assert.Equal(4, (await Search(service, "cat during:2025-03-01")).Count);
        Assert.Equal(["the cat is a loaf"], await Search(service, "\"is a loaf\" in:general"));

        // The stored JSON is rendered when present.
        var withJson = (await service.SearchAsync("pic")).Hits.Single();
        Assert.Equal(("Ada", "random"), (withJson.Message.Author.DisplayName, withJson.ChannelName));

        // Bad credentials come back as a readable message without the password.
        var bad = Connection() with { Password = "wrong-password-xyz" };
        var error = await service.TestAsync(bad);
        Assert.NotNull(error);
        Assert.DoesNotContain("wrong-password-xyz", error);
    }
}
