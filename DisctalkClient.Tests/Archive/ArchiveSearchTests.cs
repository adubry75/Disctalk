using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Archive;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Tests.Archive;

public class ArchiveQueryParserTests
{
    [Fact]
    public void Parses_terms_phrases_and_filters()
    {
        var q = ArchiveQueryParser.Parse("""cat "loaf mode" from:@ada in:#general after:2025-01-31 before:"2025-06-01" has:link""");

        Assert.Equal(["cat", "loaf mode"], q.Terms);
        Assert.Equal(("ada", "general"), (q.From, q.In));
        Assert.Equal(new DateTimeOffset(2025, 1, 31, 0, 0, 0, TimeSpan.Zero), q.After);
        Assert.Equal(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero), q.Before);
        Assert.True(q.HasLink);
    }

    [Fact]
    public void During_covers_one_utc_day()
    {
        var q = ArchiveQueryParser.Parse("during:2026-10-24");
        Assert.Equal((new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero)), (q.After!.Value, q.Before!.Value));
    }

    [Fact]
    public void Unknown_or_invalid_filters_are_searched_literally()
    {
        var q = ArchiveQueryParser.Parse("has:image after:notadate http://x.y Cat cat");
        Assert.Equal(["has:image", "after:notadate", "http://x.y", "Cat"], q.Terms); // duplicate terms collapse (case-insensitive)
        Assert.Null(q.After);
        Assert.True(ArchiveQueryParser.Parse("   ").IsEmpty);
    }
}

public class ArchiveSqlTests
{
    [Fact]
    public void Every_value_is_a_parameter_and_like_wildcards_are_escaped()
    {
        var cmd = ArchiveSql.Build(ArchiveQueryParser.Parse("100% off_topic' OR 1=1 --"), limit: 10);

        Assert.DoesNotContain("OR 1=1", cmd.Sql);
        Assert.DoesNotContain("100", cmd.Sql);
        Assert.Equal([@"%100\%%", @"%off\_topic'%", "%OR%", "%1=1%", "%--%"], cmd.Parameters.Select(p => (string)p.Value));
        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(cmd.Sql, @"m\.content LIKE @p\d+ ESCAPE '\\\\'").Count);
        Assert.EndsWith("ORDER BY m.messageId DESC\nLIMIT 10", cmd.Sql);
    }

    [Fact]
    public void Dates_become_snowflake_bounds_and_ids_match_exactly()
    {
        var after = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cmd = ArchiveSql.Build(new ArchiveQuery { From = "900000000000000101", In = "900000000000000010", After = after, Before = after.AddDays(1) },
            serverId: 900000000000000001, beforeMessageId: 12345, limit: 9999);

        Assert.Contains("m.authorId = @p1", cmd.Sql);
        Assert.Contains("m.channelId = @p0", cmd.Sql);
        Assert.Contains("c.serverId = @p2", cmd.Sql);
        Assert.Contains("m.messageId >= @p3", cmd.Sql);
        Assert.Contains("m.messageId < @p4", cmd.Sql);
        Assert.Contains("m.messageId < @p5", cmd.Sql);
        Assert.Equal(Snowflakes.FromTimestamp(after), cmd.Parameters[3].Value);
        Assert.Equal(12345UL, cmd.Parameters[5].Value);
        Assert.EndsWith($"LIMIT {ArchiveSql.MaxLimit}", cmd.Sql);
    }

    [Fact]
    public void Names_match_username_nickname_or_global_name_by_prefix()
    {
        var cmd = ArchiveSql.Build(new ArchiveQuery { From = "ada_l", In = "general" });

        Assert.Contains("c.name = @p0", cmd.Sql);
        Assert.Contains("m.authorUsername LIKE @p1", cmd.Sql);
        Assert.Contains("us.nick LIKE @p1", cmd.Sql);
        Assert.Contains("us.globalName LIKE @p1", cmd.Sql);
        Assert.Equal(@"ada\_l%", cmd.Parameters[1].Value);
    }
}

public class ArchiveSearchServiceTests
{
    readonly InMemorySecretStore _secrets = new();
    readonly FakeArchiveDatabase _db = new();
    readonly ArchiveSearchService _service;
    static readonly ArchiveConnection Connection = new("db.local", 3306, "discord", "reader", "hunter2");

    public ArchiveSearchServiceTests() => _service = new ArchiveSearchService(_secrets, _db);

    sealed class FakeArchiveDatabase : IArchiveDatabase
    {
        public List<SqlCommandText> Commands { get; } = new();
        public List<ArchiveRow> Rows { get; set; } = new();
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<ArchiveRow>> QueryAsync(ArchiveConnection connection, SqlCommandText command, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (Failure is not null) throw Failure;
            return Task.FromResult<IReadOnlyList<ArchiveRow>>(Rows);
        }

        public Task TestAsync(ArchiveConnection connection, CancellationToken cancellationToken) =>
            Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }

    static ArchiveRow Row(ulong id, string content, string? json = null) =>
        new(id, TestData.GeneralId, TestData.AdaId, "ada", content, json, "general", TestData.GuildId);

    [Fact]
    public async Task Without_a_connection_search_explains_how_to_set_it_up()
    {
        var ex = await Assert.ThrowsAsync<ArchiveSearchException>(() => _service.SearchAsync("cat"));
        Assert.Contains("Settings", ex.Message);
    }

    [Fact]
    public async Task The_connection_is_kept_in_the_secret_store_and_never_printed()
    {
        await _service.SaveConnectionAsync(Connection);

        Assert.Contains("hunter2", await _secrets.GetAsync(ArchiveSearchService.SecretKey));
        Assert.Equal(Connection, await new ArchiveSearchService(_secrets, _db).GetConnectionAsync());
        Assert.DoesNotContain("hunter2", Connection.ToString());
        Assert.Contains("Server=db.local", Connection.ToConnectionString());

        await _service.SaveConnectionAsync(null);
        Assert.Null(await _secrets.GetAsync(ArchiveSearchService.SecretKey));
    }

    [Fact]
    public async Task Pages_with_one_extra_row_and_continue_from_the_last_id()
    {
        await _service.SaveConnectionAsync(Connection);
        _db.Rows = Enumerable.Range(0, ArchiveSearchService.PageSize + 1).Select(i => Row((ulong)(1000 - i), $"cat {i}")).ToList();

        var page = await _service.SearchAsync("cat", serverId: TestData.GuildId);

        Assert.Equal(ArchiveSearchService.PageSize, page.Hits.Count);
        Assert.True(page.HasMore);
        Assert.Equal(951UL, page.NextCursor);
        Assert.Contains($"LIMIT {ArchiveSearchService.PageSize + 1}", _db.Commands[0].Sql);

        _db.Rows = [Row(10, "cat last")];
        var next = await _service.SearchAsync("cat", TestData.GuildId, page.NextCursor);
        Assert.False(next.HasMore);
        Assert.Contains(_db.Commands[1].Parameters, p => p.Value is ulong v && v == 951);
    }

    [Fact]
    public async Task Archived_json_is_rendered_as_a_full_message_with_a_plain_fallback()
    {
        await _service.SaveConnectionAsync(Connection);
        var json = """{"id":"1300000000000000001","channel_id":"900000000000000010","type":0,"content":"with picture","author":{"id":"900000000000000101","username":"ada","global_name":"Ada"},"attachments":[{"id":"5","filename":"cat.png","url":"u"}]}""";
        _db.Rows = [Row(1300000000000000001, "with picture", json), Row(1300000000000000000, "plain", "{not json")];

        var hits = (await _service.SearchAsync("picture")).Hits;

        Assert.Equal(("Ada", "cat.png"), (hits[0].Message.Author.DisplayName, Assert.Single(hits[0].Message.Attachments).FileName));
        Assert.Equal(TestData.GuildId, hits[0].Message.GuildId);
        Assert.Equal(("ada", "plain"), (hits[1].Message.Author.Username, hits[1].Message.Content));
        Assert.Equal(Snowflakes.ToTimestamp(1300000000000000000), hits[1].Timestamp);
    }

    [Fact]
    public async Task Database_errors_are_reported_without_the_password()
    {
        await _service.SaveConnectionAsync(Connection);
        _db.Failure = new InvalidOperationException("Access denied for 'reader' using password hunter2");

        var ex = await Assert.ThrowsAsync<ArchiveSearchException>(() => _service.SearchAsync("cat"));

        Assert.DoesNotContain("hunter2", ex.Message);
        Assert.DoesNotContain("hunter2", await _service.TestAsync(Connection));
        Assert.Null(await new ArchiveSearchService(_secrets, new FakeArchiveDatabase()).TestAsync(Connection));
    }

    [Fact]
    public async Task Empty_queries_do_not_touch_the_database()
    {
        await _service.SaveConnectionAsync(Connection);
        Assert.Empty((await _service.SearchAsync("  ")).Hits);
        Assert.Empty(_db.Commands);
    }
}
