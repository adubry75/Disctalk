using MySqlConnector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Disctalk.Models;

namespace Disctalk
{
    public class DatabaseRepository
    {
        private readonly MySqlConnection _dbConnection;

        private DatabaseRepository(MySqlConnection connection)
        {
            _dbConnection = connection;
        }

        public static async Task<DatabaseRepository> ConnectAsync()
        {
            string host = Environment.GetEnvironmentVariable("MYSQLHOST");
            string user = Environment.GetEnvironmentVariable("MYSQLUSER");
            string pass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
            string database = Environment.GetEnvironmentVariable("MYSQLDATABASE");
            string connectionString = $"server={host};user={user};password={pass};database={database};AllowLoadLocalInfile=true;";

            try
            {
                var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                return new DatabaseRepository(connection);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to database!! {ex.Message} {ex.StackTrace}");
                await Task.Delay(5000);
                throw;
            }
        }

        public void SaveMessage(Message message)
        {
            string delQuery = "";
            string insertQuery = "";

            try
            {
                delQuery = "DELETE from messages where messageId = @id";
                using (var command = new MySqlCommand(delQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", message.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO messages (messageId, channelId, authorId, authorUsername, content, timestamp, json, lastUpdated)
                        VALUES
                        (@id, @channel, @author, @user, @content, @timestamp, @json, now())";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", message.Id);
                    command.Parameters.AddWithValue("@channel", message.ChannelId);
                    command.Parameters.AddWithValue("@author", message.Author.Id);
                    command.Parameters.AddWithValue("@user", message.Author.Username);
                    command.Parameters.AddWithValue("@content", message.Content);
                    command.Parameters.AddWithValue("@timestamp", message.Timestamp);
                    command.Parameters.AddWithValue("@json", message.json);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting message: del={delQuery}{message.Id}{message.json},ins={insertQuery}. {ex.Message} {ex.StackTrace}");
            }
        }

        public void SaveResponse(string response)
        {
            try
            {
                string insertQuery = @"INSERT INTO rawresponses (rawResponse, lastUpdated)
                        VALUES
                        (@response, now())";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@response", response);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting rawResponse {response}: {ex.Message} {ex.StackTrace}");
            }
        }

        public bool SaveUser(RootUserObject user, long userId = -1)
        {
            bool rv = false;
            string delQuery = "";
            string insertQuery = "";

            try
            {
                if (user != null)
                {
                    delQuery = "DELETE from users where userId = @id";
                    using (var command = new MySqlCommand(delQuery, _dbConnection))
                    {
                        command.Parameters.AddWithValue("@id", user.User.Id);
                        command.ExecuteNonQuery();
                    }
                }

                if (user == null && userId != -1)
                {
                    insertQuery = @"INSERT INTO users
                    (userId, username, bio, legacyUsername, json)
                        VALUES
                    (@id, 'NULL', '', '', '')";
                    using (var command = new MySqlCommand(insertQuery, _dbConnection))
                    {
                        command.Parameters.AddWithValue("@id", userId);
                        command.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Adding NULL entry for userId {userId}");
                    return true;
                }

                insertQuery = @"INSERT INTO users
                    (userId, username, bio, legacyUsername, json)
                        VALUES
                    (@id, @user, @bio, @legacy, @json)";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", user.User.Id);
                    command.Parameters.AddWithValue("@user", user.User.Username);
                    command.Parameters.AddWithValue("@bio", user.User.Bio);
                    command.Parameters.AddWithValue("@legacy", user.LegacyUsername);
                    command.Parameters.AddWithValue("@json", user.rawJson);

                    command.ExecuteNonQuery();
                }

                rv = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting User: del={delQuery}{user?.User?.Id}{user?.rawJson},ins={insertQuery}. {ex.Message} {ex.StackTrace}");
                rv = false;
            }

            return rv;
        }

        public async Task<bool> SaveEmoji(Emoji emoji)
        {
            bool rv = false;
            string delQuery = "";
            string insertQuery = "";

            try
            {
                delQuery = "DELETE from emojis where id = @id";
                using (var command = new MySqlCommand(delQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", emoji.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO emojis
                    (id, name, rawJson)
                        VALUES
                    (@id, @name, @json)";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", emoji.Id);
                    command.Parameters.AddWithValue("@name", emoji.Name);
                    command.Parameters.AddWithValue("@json", emoji.rawJson);

                    command.ExecuteNonQuery();
                }

                rv = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting Emoji: del={delQuery}{emoji.Id}{emoji.rawJson},ins={insertQuery}. {ex.Message} {ex.StackTrace}");
                rv = false;
            }

            return rv;
        }

        public async Task<bool> SaveChannel(Channel channel)
        {
            bool rv = false;
            string delQuery = "";
            string insertQuery = "";

            try
            {
                delQuery = "DELETE from channels where channelId = @id";
                using (var command = new MySqlCommand(delQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@id", channel.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO channels
                    (serverId, channelId, type, lastMessageId, name, rateLimit, topic, position, rawJson)
                        VALUES
                    (@serverid, @channelid, @type, @lmi, @name, @rate, @topic, @pos, @json)";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", channel.GuildId);
                    command.Parameters.AddWithValue("@channelid", channel.Id);
                    command.Parameters.AddWithValue("@type", channel.Type);
                    command.Parameters.AddWithValue("@lmi", channel.LastMessageId);
                    command.Parameters.AddWithValue("@name", channel.Name);
                    command.Parameters.AddWithValue("@rate", channel.RateLimitPerUser);
                    command.Parameters.AddWithValue("@topic", channel.Topic);
                    command.Parameters.AddWithValue("@pos", channel.Position);
                    command.Parameters.AddWithValue("@json", channel.rawJson);

                    command.ExecuteNonQuery();
                }

                rv = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting channel: del={delQuery}{channel.Name}{channel.rawJson},ins={insertQuery}. {ex.Message} {ex.StackTrace}\n{channel.Topic}");
                rv = false;
            }

            return rv;
        }

        public bool SaveUserServerInfo(UserServerInfo user, long serverId = -1, long userId = -1)
        {
            bool rv = false;
            string delQuery = "";
            string insertQuery = "";

            if (user == null && serverId != -1 && userId != -1)
            {
                insertQuery = @"INSERT INTO usersServerInfo
                    (serverId, userId, username, joinDate, nick, lastModified, rawJson)
                        VALUES
                    (@serverid, @userid, 'NULL', null, '', now(), '')";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", serverId);
                    command.Parameters.AddWithValue("@userid", userId);
                    command.ExecuteNonQuery();
                }
                Console.WriteLine($"Adding NULL entry for userId {userId}, serverId {serverId}");
                return true;
            }

            try
            {
                delQuery = "DELETE from usersServerInfo where serverId = @serverid and userId = @userid";
                using (var command = new MySqlCommand(delQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", user.serverId);
                    command.Parameters.AddWithValue("@userid", user.User.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO usersServerInfo
                    (serverId, userId, username, joinDate, nick, lastModified, rawJson)
                        VALUES
                    (@serverid, @userid, @username, @join, @nick, now(), @json)";
                using (var command = new MySqlCommand(insertQuery, _dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", user.serverId);
                    command.Parameters.AddWithValue("@userid", user.User.Id);
                    command.Parameters.AddWithValue("@username", user.User.Username);
                    command.Parameters.AddWithValue("@join", user.JoinedAt);
                    command.Parameters.AddWithValue("@nick", user.Nick);
                    command.Parameters.AddWithValue("@json", user.rawJson);

                    command.ExecuteNonQuery();
                }

                rv = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error inserting User {user.User.Id} server {user.serverId}: del={delQuery}{user.rawJson},ins={insertQuery}. {ex.Message} {ex.StackTrace}");
                rv = false;
            }

            return rv;
        }

        public (bool, int) SaveMessageMentions(Message message, DataTable dt)
        {
            bool rv = false;
            int countInserted = 0;

            try
            {
                if (message.Mentions != null)
                {
                    foreach (UserMention men in message.Mentions)
                    {
                        DataRow row = dt.NewRow();

                        row["messageId"] = message.Id;
                        row["channelId"] = message.ChannelId;
                        row["userId"] = men?.id == null ? DBNull.Value : (object)men?.id;
                        row["username"] = men?.username == null ? DBNull.Value : (object)men?.username;
                        row["referencedMessage"] = message.referencedMessage?.Id == null ? DBNull.Value : (object)message.referencedMessage?.Id;

                        dt.Rows.Add(row);
                        countInserted++;
                    }
                    rv = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed inserting Mention to message {message.Id} {ex.Message}");
                rv = false;
            }

            return (rv, countInserted);
        }

        public (bool, int) SaveMessageReactions(Message message, DataTable dt)
        {
            bool rv = false;
            int countInserted = 0;

            try
            {
                if (message.Reactions != null)
                {
                    foreach (Reaction r in message.Reactions)
                    {
                        DataRow row = dt.NewRow();

                        row["messageId"] = message.Id;
                        row["emojiId"] = r.Emoji.Id == null ? DBNull.Value : (object)r.Emoji.Id;
                        row["emojiName"] = r.Emoji.Name;
                        row["emojiCount"] = r.Count;
                        row["rawJson"] = r.Emoji.rawJson;

                        dt.Rows.Add(row);
                        countInserted++;
                    }
                    rv = true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed inserting reaction to message {message.Id} {ex.Message}");
                rv = false;
            }

            return (rv, countInserted);
        }

        public async Task<DateTime> GetMaxTimestampForChannelAsync(long channelId)
        {
            DateTime maxDateTime = DateTime.MinValue;
            string selectQuery = "SELECT ifnull(max(TIMESTAMP),-1) as lastMsgDate FROM messages where channelId = @channelId";

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                command.Parameters.AddWithValue("@channelId", channelId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string result = reader["lastMsgDate"].ToString();
                        if (result != "-1")
                        {
                            maxDateTime = DateTime.Parse(reader["lastMsgDate"].ToString());
                        }
                    }
                }
            }

            return maxDateTime;
        }

        public async Task<Dictionary<long, string>> GetUserIdNameMapAsync()
        {
            Console.WriteLine($"Creating userId,Name map");

            string selectQuery = @"
            SELECT distinct m.authorId,
            CASE
                WHEN us.nick IS NOT NULL AND us.nick <> '' THEN concat(us.nick,'')
                when us.globalName is NOT NULL and us.globalName <> '' then concat(us.globalName,'')
                WHEN us.username IS NOT NULL AND us.username <> '' THEN concat(us.username,'')
                when u.username is not null and u.username <> '' AND u.username <> 'NULL' then concat(u.username,'')
                ELSE concat(m.authorUsername,'')
            END AS DisplayName
            FROM messages m
            left outer join users u on m.authorId = u.userId
            left outer join usersServerInfo us on m.authorId = us.userId
            ";

            Dictionary<long, string> allUsers = new Dictionary<long, string>();
            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                command.CommandTimeout = 180;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        long userId = long.Parse(reader["authorId"].ToString());
                        string username = reader["DisplayName"].ToString();
                        allUsers[userId] = username;
                    }
                }
            }

            return allUsers;
        }

        public async Task<bool> ReprocessJsonAsync(long channelId, string claServerId, bool boolViewEmojiReacts, bool boolAddMentions, string claChannel = null)
        {
            if (boolViewEmojiReacts)
            {
                return await ReprocessEmojiReactsAsync(channelId, claServerId, claChannel);
            }
            else if (boolAddMentions)
            {
                return await ReprocessMentionsAsync(channelId, claServerId, claChannel);
            }
            else
            {
                Console.WriteLine($"You didn't specify what to reprocess. Pass -emojireacts to update reaction counts.");
                return false;
            }
        }

        private async Task<bool> ReprocessEmojiReactsAsync(long channelId, string claServerId, string claChannel)
        {
            bool rv = true;

            string selectQuery = "select m.json from messages m left outer join channels c on m.channelId = c.channelId where 1=1";
                if (channelId != -1)
                {
                    Console.WriteLine($"Only processing for channel {claChannel}");
                    selectQuery += " and m.channelId = @channelId";
                }

                if (claServerId != null)
                {
                    Console.WriteLine($"Only processing for server {claServerId}");
                    selectQuery += " and c.serverId = @serverId";
                }
                selectQuery += " and JSON LIKE '%\"reactions\":[%'";

                List<string> jsons = new List<string>();

                Console.WriteLine($"Fetching JSON for " + (channelId == -1 ? "All Channels" : $"channelId {channelId}"));
                Console.WriteLine($"SQL: {selectQuery}");

                using (var command = new MySqlCommand(selectQuery, _dbConnection))
                {
                    command.CommandTimeout = 180;
                    if (channelId != -1)
                    {
                        command.Parameters.AddWithValue("@channelId", channelId);
                    }
                    if (claServerId != null)
                    {
                        command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                    }
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string json = reader["json"].ToString();
                            jsons.Add(json);
                        }
                    }
                }

                // Clear out existing data to rewrite new data.
                if (channelId != -1)
                {
                    string delQuery = "DELETE from messagereacts where channelId = @id";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.Parameters.AddWithValue("@id", channelId);
                    command.ExecuteNonQuery();
                }
                else if (claServerId != null)
                {
                    string delQuery = "DELETE mr FROM messagereacts mr JOIN messages m ON mr.messageId = m.messageId JOIN channels c ON m.channelId = c.channelId WHERE c.serverId = @serverId";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                    command.ExecuteNonQuery();
                }
                else
                {
                    Console.WriteLine($"No server or channel passed, truncating table, repopulating from scratch.");
                    string delQuery = "truncate table messagereacts";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.ExecuteNonQuery();
                }

                DataTable dt = new DataTable("messagereacts");
                dt.Columns.Add("messageId", typeof(long));
                dt.Columns.Add("emojiId", typeof(long));
                dt.Columns.Add("emojiName", typeof(string));
                dt.Columns.Add("emojiCount", typeof(int));
                dt.Columns.Add("rawJson", typeof(string));

                int loopCount = 0;
                int jsonCount = jsons.Count();
                int failCount = 0;
                int successCount = 0;
                int totalReactsCount = 0;
                int noReactionCount = 0;
                foreach (var json in jsons)
                {
                    try
                    {
                        Message message = JsonConvert.DeserializeObject<Message>(json);
                        if (message.Reactions == null)
                        {
                            noReactionCount++;
                            loopCount++;
                            if (loopCount % 500 == 0)
                            {
                                Console.WriteLine($"   NOOP {message.Id}. {loopCount} / {jsonCount}");
                            }
                            continue;
                        }

                        (bool insertRv, int countInserted) = SaveMessageReactions(message, dt);

                        if (!insertRv)
                        {
                            failCount++;
                            if (loopCount % 500 == 0)
                            {
                                Console.WriteLine($"   Failed to insert reactions for message {message.Id}. {loopCount} / {jsonCount}");
                            }
                        }
                        else
                        {
                            successCount++;
                            totalReactsCount += countInserted;
                            if (loopCount % 500 == 0)
                            {
                                Console.WriteLine($"   SUCCESS: Reactions inserted for message {message.Id}! {loopCount} / {jsonCount}");
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"   Failed to parse JSON: {ex.Message}");
                        rv = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   Error processing message: {ex.Message}\nJSON: {json}");
                        rv = false;
                    }

                    loopCount++;
                }

                Console.WriteLine($"{failCount} failed inserts, {successCount} good inserts ({totalReactsCount} reacts), {noReactionCount} NOOPS.");

                MySqlBulkCopy bulkCopy = new MySqlBulkCopy(_dbConnection);
                bulkCopy.DestinationTableName = "messagereacts";
                var result = bulkCopy.WriteToServer(dt);
                Console.WriteLine($"BulkCopy Result: {result.RowsInserted}, {result.Warnings}");

            return rv;
        }

        private async Task<bool> ReprocessMentionsAsync(long channelId, string claServerId, string claChannel)
        {
            bool rv = true;

            Dictionary<long, string> allUserNames = await GetUserIdNameMapAsync();

                string selectQuery = "select m.json from messages m left outer join channels c on m.channelId = c.channelId where 1=1";
                if (channelId != -1)
                {
                    selectQuery += " and m.channelId = @channelId";
                }

                if (claServerId != null)
                {
                    selectQuery += " and c.serverId = @serverId";
                }

                selectQuery += " and JSON LIKE '%\"mentions\":[{%' ORDER BY m.messageId asc";

                List<string> jsons = new List<string>();

                Console.WriteLine($"Fetching JSON for " + (channelId == -1 ? "All Channels" : $"channelId {channelId}"));
                Console.WriteLine($"SQL: {selectQuery}");

                using (var command = new MySqlCommand(selectQuery, _dbConnection))
                {
                    command.CommandTimeout = 180;
                    if (channelId != -1)
                    {
                        command.Parameters.AddWithValue("@channelId", channelId);
                    }
                    if (claServerId != null)
                    {
                        command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                    }
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string json = reader["json"].ToString();
                            jsons.Add(json);
                        }
                    }
                }

                if (channelId != -1)
                {
                    string delQuery = "DELETE from messageMentions where channelId = @id";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.Parameters.AddWithValue("@id", channelId);
                    command.ExecuteNonQuery();
                }
                else if (claServerId != null)
                {
                    string delQuery = "DELETE from messageMentions where channelId in (select channelId from channels where serverId = @serverId)";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                    command.ExecuteNonQuery();
                }
                else
                {
                    string delQuery = "truncate table messageMentions";
                    var command = new MySqlCommand(delQuery, _dbConnection);
                    command.ExecuteNonQuery();
                }

                DataTable dt = new DataTable("messageMentions");
                dt.Columns.Add("messageId", typeof(long));
                dt.Columns.Add("channelId", typeof(long));
                dt.Columns.Add("userId", typeof(long));
                dt.Columns.Add("username", typeof(string));
                dt.Columns.Add("referencedMessage", typeof(long));

                int loopCount = 0;
                int jsonCount = jsons.Count();
                int totalMentionsCount = 0;
                int noMentionsCount = 0;
                int failCount = 0;
                int successCount = 0;
                foreach (var json in jsons)
                {
                    loopCount++;

                    try
                    {
                        Message message = JsonConvert.DeserializeObject<Message>(json);

                        (bool insertRv, int countInserted) = SaveMessageMentions(message, dt);

                        if (!insertRv)
                        {
                            failCount++;
                            if (loopCount % 500 == 0)
                            {
                                Console.WriteLine($"   Failed to insert mentions for message {message.Id}. {loopCount} / {jsonCount}");
                            }
                        }
                        else
                        {
                            successCount++;
                            totalMentionsCount += countInserted;
                            if (loopCount % 500 == 0)
                            {
                                Console.WriteLine($"   SUCCESS: Mentions inserted for message {message.Id}! {loopCount} / {jsonCount}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR FAILED SOMETHING: {ex.Message}");
                    }
                }

                Console.WriteLine($"{failCount} failed inserts, {successCount} good inserts ({totalMentionsCount} mentions), {noMentionsCount} NOOPS.");

                MySqlBulkCopy bulkCopy = new MySqlBulkCopy(_dbConnection)
                {
                    DestinationTableName = "messagementions",
                    ColumnMappings =
                    {
                        new MySqlBulkCopyColumnMapping (0, "messageId"),
                        new MySqlBulkCopyColumnMapping (1, "channelId"),
                        new MySqlBulkCopyColumnMapping (2, "userId"),
                        new MySqlBulkCopyColumnMapping (3, "username"),
                        new MySqlBulkCopyColumnMapping (4, "referencedMessage")
                    }
                };

                var result = bulkCopy.WriteToServer(dt);
                Console.WriteLine($"BulkCopy Result: {result.RowsInserted}, {result.Warnings}");

            return rv;
        }

        public async Task<int> UpdateWordCountsAsync(string claChannel, string claServerId)
        {
            int rv = 0;

            Console.WriteLine("Retrieving words for word count...");
            string selectQuery = @"SELECT m.MessageId, m.ChannelId, m.AuthorId, m.AuthorUsername, m.Content, m.Timestamp, c.serverId
                FROM Messages m
                join channels c on m.channelId = c.channelId
                where 1=1";

            if (claChannel != null)
            {
                selectQuery += " and c.name = @channelName";
            }

            if (claServerId != null)
            {
                selectQuery += " and c.serverId = @serverId";

                Console.WriteLine($"Deleting word counts for server {claServerId}");
                string delQuery = "delete from words where serverId = @serverId";
                var command = new MySqlCommand(delQuery, _dbConnection);
                command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                command.ExecuteNonQuery();
            }

            DataTable dt = new DataTable("words");
            dt.Columns.Add("word", typeof(string));
            dt.Columns.Add("messageId", typeof(long));
            dt.Columns.Add("channelId", typeof(long));
            dt.Columns.Add("authorId", typeof(long));
            dt.Columns.Add("authorUsername", typeof(string));
            dt.Columns.Add("timestamp", typeof(DateTime));
            dt.Columns.Add("serverId", typeof(long));

            int wordcount = 0;
            int linecount = 0;

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                if (claChannel != null)
                {
                    command.Parameters.AddWithValue("@channelName", claChannel);
                }
                if (claServerId != null)
                {
                    command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string[] delimiters = new string[] { " ", "\t", "\n", "\r", "\r\n" };
                        string content = reader.GetString(4);
                        List<string> words = content.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).ToList<string>();
                        words = words.Distinct().ToList();

                        if (linecount++ % 5000 == 0)
                        {
                            Console.WriteLine($"Progress: {linecount} ({wordcount} words)");
                        }

                        foreach (var word in words)
                        {
                            DataRow row = dt.NewRow();

                            row["word"] = word;
                            row["messageId"] = reader.GetInt64(0);
                            row["channelId"] = reader.GetInt64(1);
                            row["authorId"] = reader.GetInt64(2);
                            row["authorUsername"] = reader.GetString(3);
                            row["timestamp"] = reader.GetDateTime(5);
                            row["serverId"] = reader.GetInt64(6);

                            dt.Rows.Add(row);

                            wordcount++;
                        }
                    }
                }
            }

            Console.WriteLine($"Bulk writing words table now... {wordcount} rows");
            MySqlBulkCopy bulkCopy = new MySqlBulkCopy(_dbConnection)
            {
                DestinationTableName = "words",
                ColumnMappings =
                {
                    new MySqlBulkCopyColumnMapping (0, "word"),
                    new MySqlBulkCopyColumnMapping (1, "messageId"),
                    new MySqlBulkCopyColumnMapping (2, "channelId"),
                    new MySqlBulkCopyColumnMapping (3, "authorId"),
                    new MySqlBulkCopyColumnMapping (4, "authorUsername"),
                    new MySqlBulkCopyColumnMapping (5, "timestamp"),
                    new MySqlBulkCopyColumnMapping (6, "serverId")
                }
            };

            var result = bulkCopy.WriteToServer(dt);
            Console.WriteLine($"BulkCopy Result: {result.RowsInserted}, {result.Warnings}");

            return rv;
        }

        public async Task<bool> SplitWordsAsync(string claChannel, string claServerId)
        {
            bool rv = false;

            string selectQuery = @"SELECT m.content, m.messageId, m.channelId, c.serverId
                FROM messages m
                join channels c on m.channelId = c.channelId
                where 1=1";

            if (claChannel != null)
            {
                selectQuery += " and c.name = @channelName";
            }

            if (claServerId != null)
            {
                selectQuery += " and c.serverId = @serverId";
            }

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                if (claChannel != null)
                {
                    command.Parameters.AddWithValue("@channelName", claChannel);
                }
                if (claServerId != null)
                {
                    command.Parameters.AddWithValue("@serverId", long.Parse(claServerId));
                }

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string message = reader["content"].ToString();
                        string messageId = reader["messageId"].ToString();
                        string channelId = reader["channelId"].ToString();
                        string serverId = reader["serverId"].ToString();

                        List<string> words = message.Split(' ').ToList<string>();
                        words = words.Distinct().ToList();

                        foreach (string word in words)
                        {
                            Console.WriteLine($"{serverId},{channelId},{messageId}: {word}");
                        }
                    }
                }
            }

            return rv;
        }

        public async Task<bool> ViewUserProfileAsync(long userId, DiscordApiClient apiClient)
        {
            bool rv = false;

            RootUserObject user = await apiClient.GetUserAsync(userId);
            if (user == null)
            {
                Console.WriteLine($"Could not get info for userId {userId}");
                if (!apiClient.TooManyRequests)
                {
                    SaveUser(null, userId);
                }
                return false;
            }

            Console.WriteLine($"JSON: {user.rawJson}");
            Console.WriteLine($"User {userId}: {user.User.Username}\n{user.rawJson}");
            SaveUser(user);

            return rv;
        }

        public async Task<bool> ViewUserServerProfileAsync(long serverId, long userId, DiscordApiClient apiClient)
        {
            bool rv = false;

            UserServerInfo user = await apiClient.GetUserServerInfoAsync(serverId, userId);
            if (user == null)
            {
                Console.WriteLine($"Could not get info for userId {userId}, server {serverId}");
                if (!apiClient.TooManyRequests)
                {
                    SaveUserServerInfo(null, serverId, userId);
                }
                return false;
            }

            Console.WriteLine($"JSON: {user.rawJson}");
            Console.WriteLine($"User {userId}: {user.User.Username}\n{user.rawJson}");
            SaveUserServerInfo(user);

            return rv;
        }

        public async Task<bool> UpdateAllUsersAsync(DiscordApiClient apiClient)
        {
            bool rv = false;

            string selectQuery = @"SELECT distinct authorId as userId
                FROM messages m
                LEFT OUTER JOIN users u
                ON m.authorId=u.userId
                left outer join channels
                WHERE u.userId IS null";

            List<long> userIds = new List<long>();

            try
            {
                using (var command = new MySqlCommand(selectQuery, _dbConnection))
                {
                    command.CommandTimeout = 180;
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            string userId = reader["userId"].ToString();
                            userIds.Add(long.Parse(userId));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error selecting unknown users to populate: {e.Message}");
                return false;
            }

            Console.WriteLine($"Found {userIds.Count} new users to fetch data on...");

            foreach (long id in userIds)
            {
                await ViewUserProfileAsync(id, apiClient);
                await Task.Delay(1000);
                if (apiClient.KillSwitch)
                {
                    Console.WriteLine("429, dying for now to play nice...");
                    break;
                }
            }

            return rv;
        }

        public async Task<bool> UpdateAllUserServerInfoAsync(long serverId, DiscordApiClient apiClient)
        {
            bool rv = false;

            string selectQuery = "SELECT userId FROM users where username <> 'NULL' and userId not in (select userId from usersServerInfo)";
            List<long> userIds = new List<long>();

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        string userId = reader["userId"].ToString();
                        userIds.Add(long.Parse(userId));
                    }
                }
            }

            foreach (long id in userIds)
            {
                await ViewUserServerProfileAsync(serverId, id, apiClient);
                await Task.Delay(1000);
                if (apiClient.KillSwitch)
                {
                    Console.WriteLine($"429, server {serverId}, user {id}. Dying for now...");
                    break;
                }
            }

            return rv;
        }

        public async Task<List<long>> GetSkipChannelsAsync(long serverId)
        {
            List<long> skipChannels = new List<long>();
            string selectQuery = "SELECT channelId FROM skip_channels WHERE serverId = @serverId";

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                command.Parameters.AddWithValue("@serverId", serverId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        skipChannels.Add(reader.GetInt64(0));
                    }
                }
            }

            return skipChannels;
        }

        public async Task AddSkipChannelAsync(long serverId, long channelId, string reason)
        {
            string insertQuery = @"INSERT INTO skip_channels (serverId, channelId, reason)
                VALUES (@serverId, @channelId, @reason)
                ON DUPLICATE KEY UPDATE reason = @reason";

            using (var command = new MySqlCommand(insertQuery, _dbConnection))
            {
                command.Parameters.AddWithValue("@serverId", serverId);
                command.Parameters.AddWithValue("@channelId", channelId);
                command.Parameters.AddWithValue("@reason", reason ?? "");
                await command.ExecuteNonQueryAsync();
            }

            Console.WriteLine($"Added channel {channelId} to skip list for server {serverId}: {reason}");
        }

        public async Task RemoveSkipChannelAsync(long serverId, long channelId)
        {
            string deleteQuery = "DELETE FROM skip_channels WHERE serverId = @serverId AND channelId = @channelId";

            using (var command = new MySqlCommand(deleteQuery, _dbConnection))
            {
                command.Parameters.AddWithValue("@serverId", serverId);
                command.Parameters.AddWithValue("@channelId", channelId);
                int rowsAffected = await command.ExecuteNonQueryAsync();

                if (rowsAffected > 0)
                {
                    Console.WriteLine($"Removed channel {channelId} from skip list for server {serverId}");
                }
                else
                {
                    Console.WriteLine($"Channel {channelId} was not in skip list for server {serverId}");
                }
            }
        }

        public async Task<List<(long channelId, string reason)>> GetSkipChannelsWithReasonsAsync(long serverId)
        {
            List<(long, string)> skipChannels = new List<(long, string)>();
            string selectQuery = "SELECT channelId, reason FROM skip_channels WHERE serverId = @serverId ORDER BY channelId";

            using (var command = new MySqlCommand(selectQuery, _dbConnection))
            {
                command.Parameters.AddWithValue("@serverId", serverId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        long channelId = reader.GetInt64(0);
                        string reason = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        skipChannels.Add((channelId, reason));
                    }
                }
            }

            return skipChannels;
        }
    }
}
