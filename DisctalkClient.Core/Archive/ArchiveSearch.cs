using System.Data.Common;
using System.Text.Json;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Security;
using DisctalkClient.Core.UserAccount;
using MySqlConnector;

namespace DisctalkClient.Core.Archive;

/// <summary>One archived message. <see cref="Message"/> is rebuilt from the stored Discord JSON when it parses.</summary>
public sealed record ArchiveHit(ulong MessageId, ulong ChannelId, ulong? ServerId, string? ChannelName, ChatMessage Message)
{
    public DateTimeOffset Timestamp => Snowflakes.ToTimestamp(MessageId);
}

public sealed record ArchiveSearchPage(IReadOnlyList<ArchiveHit> Hits, bool HasMore)
{
    public ulong? NextCursor => HasMore && Hits.Count > 0 ? Hits[^1].MessageId : null;
}

/// <summary>Connection details for the archiver's MySQL database. The password makes the whole thing a secret.</summary>
public sealed record ArchiveConnection(string Host, uint Port, string Database, string User, string Password)
{
    /// <summary>The archiver's own variables (MYSQLHOST, MYSQLUSER, MYSQLPASSWORD, MYSQLDATABASE, optional MYSQLPORT).</summary>
    public static ArchiveConnection? FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("MYSQLHOST");
        var db = Environment.GetEnvironmentVariable("MYSQLDATABASE");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(db)) return null;
        uint port = uint.TryParse(Environment.GetEnvironmentVariable("MYSQLPORT"), out var p) ? p : 3306;
        return new(host, port, db, Environment.GetEnvironmentVariable("MYSQLUSER") ?? "", Environment.GetEnvironmentVariable("MYSQLPASSWORD") ?? "");
    }

    public string ToConnectionString() => new MySqlConnectionStringBuilder
    {
        Server = Host,
        Port = Port,
        Database = Database,
        UserID = User,
        Password = Password,
        ConnectionTimeout = 10,
        DefaultCommandTimeout = 30,
        // Read-only use: no local infile, and keep the pool tiny.
        AllowLoadLocalInfile = false,
        MaximumPoolSize = 2,
    }.ConnectionString;

    public string Serialize() => JsonSerializer.Serialize(this);

    public static ArchiveConnection? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ArchiveConnection>(json); }
        catch (JsonException) { return null; }
    }

    // Never print the password, even in a debugger or log.
    public override string ToString() => $"{User}@{Host}:{Port}/{Database}";
}

/// <summary>Runs a built query and returns rows. Abstracted so the service is testable without MySQL.</summary>
public interface IArchiveDatabase
{
    Task<IReadOnlyList<ArchiveRow>> QueryAsync(ArchiveConnection connection, SqlCommandText command, CancellationToken cancellationToken);
    Task TestAsync(ArchiveConnection connection, CancellationToken cancellationToken);
}

public sealed record ArchiveRow(ulong MessageId, ulong ChannelId, ulong AuthorId, string? AuthorUsername, string? Content, string? Json, string? ChannelName, ulong? ServerId);

public sealed class MySqlArchiveDatabase : IArchiveDatabase
{
    public async Task<IReadOnlyList<ArchiveRow>> QueryAsync(ArchiveConnection connection, SqlCommandText command, CancellationToken cancellationToken)
    {
        await using var conn = new MySqlConnection(connection.ToConnectionString());
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new MySqlCommand(command.Sql, conn);
        foreach (var (name, value) in command.Parameters) cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<ArchiveRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(new ArchiveRow(
                ULong(reader, "messageId") ?? 0,
                ULong(reader, "channelId") ?? 0,
                ULong(reader, "authorId") ?? 0,
                Text(reader, "authorUsername"),
                Text(reader, "content"),
                Text(reader, "json"),
                Text(reader, "channelName"),
                ULong(reader, "serverId")));
        }
        return rows;
    }

    public async Task TestAsync(ArchiveConnection connection, CancellationToken cancellationToken)
    {
        await using var conn = new MySqlConnection(connection.ToConnectionString());
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM messages LIMIT 1", conn);
        await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    // Column types vary with how the archiver's tables were created (BIGINT, BIGINT UNSIGNED, VARCHAR…).
    static ulong? ULong(DbDataReader r, string column)
    {
        var value = r[column];
        return value switch
        {
            DBNull or null => null,
            ulong u => u,
            long l when l >= 0 => (ulong)l,
            decimal d when d >= 0 => (ulong)d,
            string s when ulong.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    static string? Text(DbDataReader r, string column) => r[column] is string s ? s : r[column] is DBNull ? null : r[column]?.ToString();
}

/// <summary>
/// Search over the local archive the 30-minute archiver fills. The connection lives in secure storage; queries are
/// parameterized; one request per search or "load more" (no live polling of the database).
/// </summary>
public sealed class ArchiveSearchService(ISecretStore secrets, IArchiveDatabase database)
{
    public const string SecretKey = "disctalkclient.archive-connection";
    public const int PageSize = 50;

    ArchiveConnection? _connection;
    bool _loaded;

    public async Task<ArchiveConnection?> GetConnectionAsync()
    {
        if (_loaded) return _connection;
        _connection = ArchiveConnection.Deserialize(await secrets.GetAsync(SecretKey).ConfigureAwait(false)) ?? ArchiveConnection.FromEnvironment();
        _loaded = true;
        return _connection;
    }

    public async Task SaveConnectionAsync(ArchiveConnection? connection)
    {
        if (connection is null) await secrets.RemoveAsync(SecretKey).ConfigureAwait(false);
        else await secrets.SetAsync(SecretKey, connection.Serialize()).ConfigureAwait(false);
        _connection = connection;
        _loaded = true;
    }

    /// <returns>Null on success, else a message that's safe to show (no password).</returns>
    public async Task<string?> TestAsync(ArchiveConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.TestAsync(connection, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Describe(ex, connection);
        }
    }

    public async Task<ArchiveSearchPage> SearchAsync(string text, ulong? serverId = null, ulong? cursor = null, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync().ConfigureAwait(false)
            ?? throw new ArchiveSearchException("Archive search isn't set up. Add the database in Settings → Archive search.");
        var query = ArchiveQueryParser.Parse(text);
        if (query.IsEmpty) return new ArchiveSearchPage([], false);

        // Ask for one extra row to know whether there's another page.
        var command = ArchiveSql.Build(query, serverId, cursor, PageSize + 1);
        IReadOnlyList<ArchiveRow> rows;
        try
        {
            rows = await database.QueryAsync(connection, command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ArchiveSearchException(Describe(ex, connection), ex);
        }

        var hits = rows.Take(PageSize).Select(ToHit).ToList();
        return new ArchiveSearchPage(hits, rows.Count > PageSize);
    }

    internal static ArchiveHit ToHit(ArchiveRow row)
    {
        ChatMessage? message = null;
        if (!string.IsNullOrWhiteSpace(row.Json))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.Json);
                message = DiscordJson.ToMessage(doc.RootElement, row.ServerId);
            }
            catch (JsonException) { }
        }
        // The archiver stores Newtonsoft-serialized Discord JSON; if it doesn't parse, fall back to the plain columns.
        if (message is null || message.Id != row.MessageId)
            message = new ChatMessage
            {
                Id = row.MessageId,
                ChannelId = row.ChannelId,
                GuildId = row.ServerId,
                Author = new AuthorInfo { Id = row.AuthorId, Username = row.AuthorUsername ?? "unknown", AvatarUrl = DiscordJson.DefaultAvatarUrl(row.AuthorId, null) },
                Content = row.Content ?? "",
            };
        return new ArchiveHit(row.MessageId, row.ChannelId, row.ServerId, row.ChannelName, message with { ChannelId = row.ChannelId, GuildId = row.ServerId });
    }

    static string Describe(Exception ex, ArchiveConnection connection)
    {
        var text = ex switch
        {
            MySqlException { ErrorCode: MySqlErrorCode.UnableToConnectToHost } => $"Can't reach the database at {connection.Host}:{connection.Port}.",
            MySqlException { ErrorCode: MySqlErrorCode.AccessDenied } => "The database rejected the user name or password.",
            MySqlException { ErrorCode: MySqlErrorCode.UnknownDatabase } => $"There's no database named '{connection.Database}'.",
            _ => ex.Message,
        };
        return SecretRedactor.Redact(connection.Password.Length > 0 ? text.Replace(connection.Password, SecretRedactor.Mask) : text);
    }
}

public sealed class ArchiveSearchException(string message, Exception? inner = null) : Exception(message, inner);
