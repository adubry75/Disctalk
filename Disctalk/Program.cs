﻿using Mono.Options;
//using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using System.Data;

namespace Disctalk
{

    internal class Program
    {

        static public string textToSend = null;
        static public string userId = null;
        static public string channelId = null;
        static public string claChannel = null; // cla = command line argument
        static public int messagesPerFetch = 100;
        static public long startingMsgId = -1;
        static public int totalMessageLimit = 100;
        static public bool boolViewMessages = false;
        static public bool boolViewServers = false;
        static public bool boolViewChannels = false;
        static public bool boolViewRoles = false;
        static public bool boolViewEmojis = false;
        static public bool boolViewEmojiReacts = false;
        static public bool boolViewStickers = false;
        static public bool boolServerPreview = false;
        static public bool boolUpdateUsers = false;
        static public bool boolUpdateUserServerInfo = false;
        static public bool boolUpdateAllMessages = false;
        static public bool boolReprocessJson = false;
        static public bool boolAddMentions = false;
        static public bool boolForceAll = false;
        static public bool boolWordCount = false;
        static public string claServerId = null;
        static public string orderBy = "asc"; // anything not "desc" will imply "asc"
        static public bool testMode = false;
        static public bool debugMode = false;
        static public string token = Environment.GetEnvironmentVariable("MY_BOT_TOKEN");
        static public bool tooManyRequests = false;

        static public bool KILLSWITCH = false;

        static public HttpClient httpClient = null;
        static public HttpRequestMessage httpRequest = null;
        static MySqlConnection dbConnection = null;

        // Channels I don't have access to or were discontinued.
        static public List<long> skipChannels = new List<long>
        {
            374631528275378176, // Voice Channels
            374631528275378177, // General (not general)
            374633617588224001, // Topics
            374677953004437514, // Gaming (not gaming)
            428448605762748426, // hall of justice
            628435158902636586, // craigslist
            747310713403342869, // movie night
            748691464481144952, // bot log
            798357201605099570, // pinterest
            882133723892568076, // Music
            882789707468136458, // theta
            882789786560102490, // coastal town
            964682118884110346, // the forgotten one eye
            991863219008307200, // Main
            991948107954786365, // Admin
            1021969695433314314, // void
            1187489188417917029, // meetups
            1228558055248232558,  // automod log
        };


        static async Task Main(string[] args)
        {
            try
            {

                if (!parseArgs(args)) { return; }

                bool dbRv = await connectToDB();
                if (!dbRv)
                {
                    string host = Environment.GetEnvironmentVariable("MYSQLHOST");
                    string user = Environment.GetEnvironmentVariable("MYSQLUSER");
                    string pass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
                    string database = Environment.GetEnvironmentVariable("MYSQLDATABASE");
                    Console.WriteLine($"FAILED TO CONNECT TO DATABASE! {host},{user},{pass},{database}");
                    Thread.Sleep(5000);
                    return;
                }

                if (textToSend != null)
                {
                    await sendText();
                    return;
                }

                if (boolReprocessJson)
                {
                    // This is really specific coding for stuff that I missed the first time grabbing the data, which is why
                    // I saved all the rawJson in the database, so I could reprocess it without regrabbing a million API calls again!
                    if (channelId != null)
                    {
                        await reprocessJson(long.Parse(channelId));
                    }
                    else
                    {
                        await reprocessJson();
                    }

                    return;
                }

                if (boolWordCount)
                {
                    await updateWordCounts();
                    return;
                }

                if (boolViewMessages)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must pass a serverID first.");
                        return;
                    }

                    List<Channel> channels = await getChannels(long.Parse(claServerId));
                    Channel channelMatch = channels.FirstOrDefault(channel => channel.Name == claChannel);
                    if (channelMatch == null)
                    {
                        Console.WriteLine($"Could not find channel '{claChannel}' for server '{claServerId}'. Pass in the channel NAME not Id.");
                        foreach (var c in channels)
                        {
                            Console.WriteLine($"{c.Id} - {c.Name}");
                        }
                        return;
                    }

                    List<Message> messages = await getMessages(channelMatch, totalMessageLimit, false);
                    foreach (Message message in messages)
                    {
                        DateTime date = message.Timestamp;
                        string dateNice = date.ToString("yyyy-MM-dd HH:mm:ss");
                        //TODO replace username with global_name if available. orr...??? is there a server-specific username available?! TODOODOO!
                        string msg = $"{message.Id}|{dateNice} {message.Author.Username} ({message.Author.Id}): {message.Content}";
                        Console.WriteLine(msg);
                        if (debugMode) { Console.WriteLine(message.json); }
                    }

                    return;
                }

                if (boolViewServers)
                {
                    await viewServers();
                    return;
                }

                if (boolViewChannels)
                {
                    List<Channel> channels = await getChannels(long.Parse(claServerId));
                    if (channels == null)
                    {
                        Console.WriteLine("Error getting channels, dying.");
                        return;
                    }

                    foreach (var channel in channels)
                    {
                        Console.WriteLine($"{channel.Id}: {channel.Name}. Rate: {channel.RateLimitPerUser}. ");
                        foreach (var overwrite in channel.PermissionOverwrites)
                        {
                            //Console.WriteLine($"   {overwrite.Type}: Allow {overwrite.Allow}, Deny {overwrite.Deny}");
                        }
                        channel.rawJson = JsonConvert.SerializeObject(channel);

                        await SaveChannel(channel);

                    }
                    return;
                }

                if (userId != null)
                {
                    if (claServerId != null)
                    {
                        await viewUserServerProfile(long.Parse(claServerId), long.Parse(userId));
                    }
                    else
                    {
                        await viewUserProfile(long.Parse(userId));
                    }

                    return;
                }

                if (boolUpdateUsers)
                {
                    await updateAllUsers();
                    return;
                }

                if (boolUpdateUserServerInfo)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine($"You need to pass in a serverId to update user Server Info.");
                        return;
                    }

                    await updateAllUserServerInfo(long.Parse(claServerId));
                    return;
                }

                if (boolUpdateAllMessages)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must pass a serverId to update messages on.");
                        return;
                    }

                    await updateAllMessages(long.Parse(claServerId));

                    return;
                }

                if (boolViewEmojis && claServerId == null)
                {
                    Console.WriteLine("You need to specify a serverId to view emojis, dork.");
                    return;
                }

                if (claServerId != null)
                {
                    dynamic server = await getServer(claServerId);
                    Console.WriteLine($"{server.Name}: {server.Description}");
                    if (boolServerPreview)
                    {
                        Console.WriteLine($"{server.MemberCount} members, {server.PresenceCount} presence.");
                    }
                    else
                    {
                        // Roles aren't available view Preview.
                        if (boolViewRoles)
                        {
                            foreach (var role in server.Roles)
                            {
                                Console.WriteLine($"{role.Name}: {role.Description}");
                            }
                        }
                    }

                    // Emojis and Stickers are available in verbose and preview modes.
                    if (boolViewEmojis)
                    {
                        foreach (Emoji emoji in server.Emojis)
                        {
                            emoji.rawJson = JsonConvert.SerializeObject(emoji);
                            Console.WriteLine($"{emoji.Name}");

                            await SaveEmoji(emoji);
                        }
                    }

                    if (boolViewStickers)
                    {
                        foreach (var sticker in server.Stickers)
                        {
                            Console.WriteLine($"{sticker.Name}");
                        }
                    }


                    return;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"main() died with unhandled exception: {ex.Message} {ex.StackTrace}");
                Console.WriteLine("Dying...");
                Thread.Sleep(5000);
                Environment.Exit(1);
            }
            finally
            {
                // Clean up resources, if necessary
                Console.WriteLine("Exiting...");
                Thread.Sleep(1000);
                //Console.ReadKey();
            }

        }

        async static public Task<bool> reprocessJson(long channelId = -1)
        {
            bool rv = true;

            bool boolAddServerGlobalName = false;

            if (boolViewEmojiReacts)
            {
                string selectQuery = "select m.json from messages m left outer join channels c on m.channelId = c.channelId where 1=1";
                if (channelId != -1)
                {
                    Console.WriteLine($"Only processing for channel {claChannel}");
                    selectQuery += $" and m.channelId = {channelId}";
                }

                if (claServerId != null)
                {
                    Console.WriteLine($"Only processing for server {claServerId}");
                    selectQuery += $" and c.serverId = {claServerId}";
                }
                selectQuery += " and JSON LIKE '%\"reactions\":[%'";
                //selectQuery += " order by m.channelId asc, m.timestamp desc ";

                List<string> jsons = new List<string>();

                Console.WriteLine($"Fetching JSON for " + (channelId == -1 ? "All Channels" : $"channelId {channelId}"));
                Console.WriteLine($"SQL: {selectQuery}");

                using (var command = new MySqlCommand(selectQuery, dbConnection))
                {
                    command.CommandTimeout = 180; // Timeout in seconds, adjust as needed
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string json = reader["json"].ToString();
                            jsons.Add(json);
                        }
                    }
                }

                // Clear out existing data to rewrite new data.
                if (channelId != -1)
                {
                    string delQuery = "DELETE from messagereacts where channelId = @id"; // THIS WILL BREAK! do a triple join like I do below for claServerId
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.Parameters.AddWithValue("@id", channelId);
                    command.ExecuteNonQuery();
                }
                else if (claServerId != null)
                {
                    string delQuery = $"DELETE mr FROM messagereacts mr JOIN messages m ON mr.messageId = m.messageId JOIN channels c ON m.channelId = c.channelId WHERE c.serverId = {claServerId}";
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.ExecuteNonQuery();
                }
                else
                {
                    Console.WriteLine($"No server or channel passed, truncating table, repopulating from scratch.");
                    string delQuery = $"truncate table messagereacts";
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.ExecuteNonQuery();
                }

                // Define dataTable we're going to write to in MEMORY, and bulk copy that table to MySql later.
                DataTable dt = new DataTable("messagereacts");
                dt.Columns.Add("messageId", typeof(long));
                dt.Columns.Add("emojiId", typeof(long));
                dt.Columns.Add("emojiName", typeof(string));
                dt.Columns.Add("emojiCount", typeof(int));
                dt.Columns.Add("rawJson", typeof(string));

                int debugCount = 0;
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
                            if (loopCount++ % 500 == 0)
                            {
                                Console.WriteLine($"   NOOP {message.Id}. {loopCount} / {jsonCount}");
                            }
                            //Console.WriteLine($"   There were no reactions on message {message.Id}! {loopCount++} / {jsonCount}");
                            continue;
                        }

                        (bool insertRv, int countInserted) = await SaveMessageReactions(message, dt);

                        if (!insertRv)
                        {
                            failCount++;
                            if (loopCount++ % 500 == 0)
                            {
                                Console.WriteLine($"   Failed to insert reactions for message {message.Id}. {loopCount} / {jsonCount}");
                            }
                        }
                        else
                        {
                            successCount++;
                            totalReactsCount += countInserted;
                            if (loopCount++ % 500 == 0)
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

                    //if (debugCount++ == 10) { break; }
                }

                Console.WriteLine($"{failCount} failed inserts, {successCount} good inserts ({totalReactsCount} reacts), {noReactionCount} NOOPS.");

                MySqlBulkCopy bulkCopy = new MySqlBulkCopy(dbConnection);
                bulkCopy.DestinationTableName = "messagereacts";
                var result = bulkCopy.WriteToServer(dt);
                Console.WriteLine($"BulkCopy Result: {result.RowsInserted}, {result.Warnings}");
            }
            else if (boolAddMentions)
            {
                Dictionary<long, string> allUserNames = await getUserIdNameMap();

                string selectQuery = "select m.json from messages m left outer join channels c on m.channelId = c.channelId where 1=1";
                if (channelId != -1)
                {
                    selectQuery += $" and m.channelId = {channelId}";
                }

                if (claServerId != null)
                {
                    selectQuery += $" and c.serverId = {claServerId}";
                }

                selectQuery += " and JSON LIKE '%\"mentions\":[{%' ORDER BY m.messageId asc";

                List<string> jsons = new List<string>();

                Console.WriteLine($"Fetching JSON for " + (channelId == -1 ? "All Channels" : $"channelId {channelId}"));
                Console.WriteLine($"SQL: {selectQuery}");

                using (var command = new MySqlCommand(selectQuery, dbConnection))
                {
                    command.CommandTimeout = 180; // Timeout in seconds, adjust as needed
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string json = reader["json"].ToString();
                            jsons.Add(json);
                        }
                    }
                }

                if (channelId != -1)
                {
                    string delQuery = "DELETE from messageMentions where channelId = @id";
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.Parameters.AddWithValue("@id", channelId);
                    command.ExecuteNonQuery();
                }
                else if (claServerId != null)
                {
                    string delQuery = $"DELETE from messageMentions where channelId in (select channelId from channels where serverId = {claServerId})";
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.ExecuteNonQuery();
                }
                else
                {
                    string delQuery = "truncate table messageMentions";
                    var command = new MySqlCommand(delQuery, dbConnection);
                    command.ExecuteNonQuery();
                }

                // Define dataTable we're going to write to in MEMORY, and bulk copy that table to MySql later.
                DataTable dt = new DataTable("messageMentions");
                dt.Columns.Add("messageId", typeof(long));
                dt.Columns.Add("channelId", typeof(long));
                dt.Columns.Add("userId", typeof(long));
                dt.Columns.Add("username", typeof(string));
                dt.Columns.Add("referencedMessage", typeof(long));

                int debugCount = 0;
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
                        
                        /*
                        foreach (var mention in message.Mentions)
                        {
                            Console.WriteLine($"MSG {message.Id} mentions {allUserNames[mention.id]}:  {loopCount} / {jsonCount}");
                        }
                        */

                        (bool insertRv, int countInserted) = await SaveMessageMentions(message, dt);

                        if (!insertRv)
                        {
                            failCount++;
                            if (loopCount++ % 500 == 0)
                            {
                                Console.WriteLine($"   Failed to insert mentions for message {message.Id}. {loopCount} / {jsonCount}");
                            }
                        }
                        else
                        {
                            successCount++;
                            totalMentionsCount += countInserted;
                            if (loopCount++ % 500 == 0)
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

                MySqlBulkCopy bulkCopy = new MySqlBulkCopy(dbConnection)
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
            }
            else if (boolAddServerGlobalName)
            {
                string selectQuery = "select us.rawjson from usersServerInfo us where 1=1";
                selectQuery += " and rawJSON LIKE '%\"global_name\":\"%'";

                List<string> jsons = new List<string>();

                Console.WriteLine($"SQL: {selectQuery}");

                using (var command = new MySqlCommand(selectQuery, dbConnection))
                {
                    command.CommandTimeout = 180; // Timeout in seconds, adjust as needed
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string json = reader["rawjson"].ToString();
                            jsons.Add(json);
                        }
                    }
                }

                int debugCount = 0;
                int loopCount = 0;
                int jsonCount = jsons.Count();
                int failCount = 0;
                int successCount = 0;
                foreach (var json in jsons)
                {
                    loopCount++;

                    try
                    {
                        UserServerInfo user = JsonConvert.DeserializeObject<UserServerInfo>(json);
                        Console.WriteLine($"{user.User.Username},{user.User.GlobalName} | {loopCount} / {jsonCount}");

                        string delQuery = "update UsersServerInfo set globalName = @name where userId = @id";
                        var command = new MySqlCommand(delQuery, dbConnection);
                        command.Parameters.AddWithValue("@name", user.User.GlobalName);
                        command.Parameters.AddWithValue("@id", user.User.Id);
                        command.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {

                    }

                }
            }
            else
            {
                Console.WriteLine($"You didn't specify what to reprocess. Pass -emojireacts to update reaction counts.");
                rv = false;
            }

            return rv;
        }




        async static public Task<(bool, int)> SaveMessageMentions(Message message, DataTable dt)
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
                        row["userId"] = men?.id == null ? DBNull.Value : (object)men?.id; // the userId mentioned
                        row["username"] = men?.username == null ? DBNull.Value : (object)men?.username; // the username. If the user has been deleted, this is the only spot you'll find their name.
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

        async static public Task<(bool, int)> SaveMessageReactions(Message message, DataTable dt)
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


        public class wordCount
        {
            public string word { get; set; }
            public long messageId { get; set; }
            public long channelId { get; set; }
            public long authorId { get; set; }
            public string authorUsername { get; set; }
            public DateTime timeStamp { get; set; }
            public long serverId { get; set; }

        }

        async static public Task<int> updateWordCounts()
        {
            int rv = 0;

            Console.WriteLine("Retrieving words for word count...");
            string selectQuery = @"SELECT m.MessageId, m.ChannelId, m.AuthorId, m.AuthorUsername, m.Content, m.Timestamp, c.serverId
                FROM Messages m
                join channels c on m.channelId = c.channelId";


            if (claChannel != null)
            {
                selectQuery += $" and c.name = {claChannel}";
                
            }

            if (claServerId != null)
            {
                selectQuery += $" and c.serverId = {claServerId}";

                Console.WriteLine($"Deleting word counts for server {claServerId}");
                string delQuery = $"delete from words where serverId = {claServerId}";
                var command = new MySqlCommand(delQuery, dbConnection);
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

            // Fetch messages
            using (var command = new MySqlCommand(selectQuery, dbConnection))
            {
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {

                        // Split content into words, considering spaces, tabs, and returns
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
            MySqlBulkCopy bulkCopy = new MySqlBulkCopy(dbConnection)
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


        async static public Task<bool> updateAllMessages(long serverId)
        {
            bool rv = false;

            List<Channel> channels = await getChannels(serverId);
            int channelCount = 1;
            int totalChannels = channels.Count;
            int totalMessagesUpdated = 0;
            foreach (Channel c in channels)
            {
                if (   (!string.IsNullOrEmpty(channelId) && c.Id != long.Parse(channelId))
                    || (!string.IsNullOrEmpty(claChannel) && c.Name != claChannel)  )
                {
                    // They passed a channelId on the command line (or channel name), so only update that one.
                    continue;
                }
                

                if (skipChannels.Contains(c.Id))
                {
                    Console.WriteLine($"Skipping unreachable channel {c.Name} ({c.Id})");
                    channelCount++;
                    continue;
                }

                Console.WriteLine($"\nUpdating Channel: {c.Name} {c.Id}... ({channelCount++} / {totalChannels})");
                List<Message> messages = await getMessages(c, 999999, boolForceAll ? false : true);
                Console.WriteLine($"Updated {messages.Count()} in channel {c.Name} ({c.Id})");
                totalMessagesUpdated += messages.Count();
            }

            Console.WriteLine($"\nALL CHANNELS UPDATED! {totalMessagesUpdated} total messages across all channels.");

            return (rv);
        }

        async static public Task<bool> updateAllUsers()
        {
            bool rv = false;

            string selectQuery = "SELECT distinct authorId as userId FROM messages m LEFT OUTER JOIN users u ON m.authorId=u.userId WHERE u.userId IS null";
            List<long> userIds = new List<long>();

            try
            {
                using (var command = new MySqlCommand(selectQuery, dbConnection))
                {
                    command.CommandTimeout = 180;
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
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
                return (false);
            }

            foreach (long id in userIds)
            {
                await viewUserProfile(id);
                Thread.Sleep(1000); // play nice, don't exceed requests limit.
                if (KILLSWITCH)
                {
                    Console.WriteLine("429, dying for now to play nice...");
                    break;
                }
            }

            return (rv);
        }

        async static public Task<Dictionary<long,string>> getUserIdNameMap()
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

            Dictionary<long,string> allUsers = new Dictionary<long,string>();
            using (var command = new MySqlCommand(selectQuery, dbConnection))
            {
                using (var reader = command.ExecuteReader())
                {
                    command.CommandTimeout = 180;
                    while (reader.Read())
                    {
                        long userId = long.Parse(reader["authorId"].ToString());
                        string username = reader["DisplayName"].ToString();
                        allUsers[userId] = username;
                    }
                }
            }

            return (allUsers);
        }

        async static public Task<bool> updateAllUserServerInfo(long serverId)
        {
            bool rv = false;

            // Get users that are still on the server, and we haven't got the profile yet.
            string selectQuery = "SELECT userId FROM users where username <> 'NULL' and userId not in (select userId from usersServerInfo)";
            List<long> userIds = new List<long>();

            using (var command = new MySqlCommand(selectQuery, dbConnection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string userId = reader["userId"].ToString();
                        userIds.Add(long.Parse(userId));
                    }
                }
            }

            foreach (long id in userIds)
            {
                await viewUserServerProfile(serverId, id);
                Thread.Sleep(1000); // play nice, don't exceed requests limit.
                if (KILLSWITCH)
                {
                    Console.WriteLine($"429, server {serverId}, user {id}. Dying for now...");
                    break;
                }
            }

            return (rv);
        }

        async static public Task<bool> viewUserServerProfile(long serverId, long userId)
        {
            bool rv = false;

            UserServerInfo user = await getUserServerInfo(serverId, userId);
            if (user == null)
            {
                Console.WriteLine($"Could not get info for userId {userId}, server {serverId}");
                // Mark them invalid so we don't keep checking next time.
                if (!tooManyRequests)
                {
                    // Don't mark them if the null is because of a 429; we'll retry later.
                    SaveUserServerInfo(null, serverId, userId);
                }

                return (false);
            }
            Console.WriteLine($"JSON: {user.rawJson}");
            Console.WriteLine($"User {userId}: {user.User.Username}\n{user.rawJson}");
            SaveUserServerInfo(user);


            return (rv);
        }

        async static public Task<bool> viewUserProfile(long userId)
        {
            bool rv = false;

            RootUserObject user = await getUser(userId);
            if (user == null)
            {
                Console.WriteLine($"Could not get info for userId {userId}");
                // Mark them invalid so we don't keep checking next time.
                if (!tooManyRequests)
                {
                    // Don't mark them if the null is because of a 429; we'll retry later.
                    SaveUser(null, userId);
                }

                return (false);
            }
            Console.WriteLine($"JSON: {user.rawJson}");
            Console.WriteLine($"User {userId}: {user.User.Username}\n{user.rawJson}");
            SaveUser(user);


            return (rv);
        }

        async static public Task<RootUserObject> getUser(long userId)
        {
            //    https://discord.com/api/v9/users/[userid]
            // or https://discord.com/api/v9/users/[userid]/profile?with_mutual_guilds=true&with_mutual_friends=true&with_mutual_friends_count=false
            // or https://discord.com/api/v9/guilds/[serverid]/members/[userId] for more server specific info

            RootUserObject user = null;

            string url = $"https://discord.com/api/v9/users/{userId}/profile";

            prepareClient(url, new HttpMethod("GET"));

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

            string responseBody = "";
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    user = JsonConvert.DeserializeObject<RootUserObject>(responseBody);
                    user.rawJson = responseBody;

                    tooManyRequests = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON for userId {userId}. {ex.Message} {ex.StackTrace} JSON:{responseBody}");
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine($"\nForbidden response, user probably no longer on server.");
                return (null);
            }
            else if ((int)response.StatusCode == 429)
            {
                Console.WriteLine("Too many requests, pausing for a few* seconds...");

                // Print all response headers
                foreach (var header in response.Headers)
                {
                    Console.WriteLine($"Header: {header.Key}: {string.Join(", ", header.Value)}");
                }

                // If there are any content headers, print them as well
                foreach (var contentHeader in response.Content.Headers)
                {
                    Console.WriteLine($"ContentHeader: {contentHeader.Key}: {string.Join(", ", contentHeader.Value)}");
                }

                int sleepTime = 90000; // Default sleep time in seconds if 'Retry-After' is not found
                if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string> values))
                {
                    var retryAfterValue = values.First();
                    if (int.TryParse(retryAfterValue, out int retrySeconds))
                    {
                        Console.WriteLine($"\nDELAY 429!!! Setting 429 delay time to Retry-After value of {retrySeconds} seconds");
                        sleepTime = retrySeconds + 2; // + 2 for margin...
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse 'Retry-After' header to an integer.");
                    }
                }
                else
                {
                    Console.WriteLine("'Retry-After' header not found. Using default sleep time.");
                }

                Thread.Sleep(sleepTime * 1000); // Multiply by 1000 to convert seconds to milliseconds



                tooManyRequests = true;

                // Temp HACK, just die after the Retry-After timeout to play nice while developing...
                //KILLSWITCH = true;

                return (null);
            }
            else
            {
                Console.WriteLine($"Error getting user {userId}! {response.StatusCode} ({(int)response.StatusCode})");
            }

            return user;
        }


        async static public Task<UserServerInfo> getUserServerInfo(long serverId, long userId)
        {
            // https://discord.com/api/v9/guilds/[serverId]/members/[userId] for more server specific info

            UserServerInfo user = null;

            string url = $"https://discord.com/api/v9/guilds/{serverId}/members/{userId}";

            prepareClient(url, new HttpMethod("GET"));

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

            string responseBody = "";
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    user = JsonConvert.DeserializeObject<UserServerInfo>(responseBody);
                    user.serverId = serverId;
                    user.rawJson = responseBody;


                    tooManyRequests = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON for userId {userId}, server {serverId}. {ex.Message} {ex.StackTrace} JSON:{responseBody}");
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine($"\nForbidden response, user probably no longer on server.");
                return (null);
            }
            else if ((int)response.StatusCode == 429)
            {
                Console.WriteLine("Too many requests, pausing for a few* seconds...");

                // Print all response headers
                foreach (var header in response.Headers)
                {
                    Console.WriteLine($"Header: {header.Key}: {string.Join(", ", header.Value)}");
                }

                // If there are any content headers, print them as well
                foreach (var contentHeader in response.Content.Headers)
                {
                    Console.WriteLine($"ContentHeader: {contentHeader.Key}: {string.Join(", ", contentHeader.Value)}");
                }

                int sleepTime = 90000; // Default sleep time in seconds if 'Retry-After' is not found
                if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string> values))
                {
                    var retryAfterValue = values.First();
                    if (int.TryParse(retryAfterValue, out int retrySeconds))
                    {
                        Console.WriteLine($"\nDELAY 429!!! Setting 429 delay time to Retry-After value of {retrySeconds} seconds");
                        sleepTime = retrySeconds + 2; // + 2 for margin...
                    }
                    else
                    {
                        Console.WriteLine("Failed to parse 'Retry-After' header to an integer.");
                    }
                }
                else
                {
                    Console.WriteLine("'Retry-After' header not found. Using default sleep time.");
                }

                Thread.Sleep(sleepTime * 1000); // Multiply by 1000 to convert seconds to milliseconds


                tooManyRequests = true;
                //KILLSWITCH = true;

                return (null);
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"404 Not Found, user must have left server recently. Skipping.");
                return (null);
            }
            else
            {
                Console.WriteLine($"Error getting user {userId}, server {serverId}! {response.StatusCode} ({(int)response.StatusCode}) JSON: {responseBody}");
            }

            return user;
        }

        async static public Task<dynamic> getServer(string guildId)
        {
            // https://discord.com/api/v9/guilds/[serverId]
            // https://discord.com/api/v9/guilds/[serverId]/preview
            Server server = new Server();

            string url = $"https://discord.com/api/v9/guilds/{guildId}" + (boolServerPreview ? "/preview" : "");

            prepareClient(url, new HttpMethod("GET"));

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                if (boolServerPreview)
                {
                    return JsonConvert.DeserializeObject<ServerPreview>(responseBody);
                }
                else
                {
                    return JsonConvert.DeserializeObject<Server>(responseBody);
                }
            }
            else
            {
                Console.WriteLine($"Error getting server {guildId}! {response.StatusCode} ({(int)response.StatusCode})");
            }

            return server;
        }

        async static public Task<bool> viewServers()
        {
            bool rv = false;

            // https://discord.com/api/v9/users/@me/guilds
            string url = $"https://discord.com/api/v9/users/@me/guilds";

            prepareClient(url, new HttpMethod("GET"));

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

            if (response.IsSuccessStatusCode)
            {

                string responseBody = await response.Content.ReadAsStringAsync();

                var servers = JsonConvert.DeserializeObject<ServerHeader[]>(responseBody);

                foreach (var server in servers)
                {
                    Console.WriteLine($"{server.Id}: {server.name}");
                }

                rv = true;
            }
            else
            {
                Console.WriteLine($"Error getting list of servers! {response.StatusCode} ({(int)response.StatusCode})");
                rv = false;
            }


            return (rv);
        }

        async static public Task<List<Channel>> getChannels(long serverId)
        {
            List<Channel> channels = new List<Channel>();

            if (claServerId == null)
            {
                Console.WriteLine("You need to pass a --server to view channels.");
                return (null);
            }


            // https://discord.com/api/v9/users/@me/guilds
            string url = $"https://discord.com/api/v9/guilds/{serverId}/channels";

            prepareClient(url, new HttpMethod("GET"));

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

            if (response.IsSuccessStatusCode)
            {

                string responseBody = await response.Content.ReadAsStringAsync();
                if (debugMode) { Console.WriteLine(responseBody); }
                channels = JsonConvert.DeserializeObject<Channel[]>(responseBody).ToList<Channel>();
            }
            else
            {
                Console.WriteLine($"Error getting list of channels! {response.StatusCode} ({(int)response.StatusCode})");
                return (null);
            }


            return (channels);
        }

        async static public Task<List<Message>> getMessages(Channel channel, int totalLimit = 100, bool updateNew = false)
        {
            //TODO this doesn't read inline image attachments like in #hall_of_fame. FIX!

            string lastMessageId = null;
            int messagesFetched = 0;
            List<Message> allMessages = new List<Message>();

            // updateNew bool means we find the most recent message in the database, and we fetch messages in reverse chronological order
            // until we've reached a message older than the newest database message, then we abort because now we're current.
            DateTime maxDateTime = DateTime.MinValue;
            if (updateNew)
            {
                string selectQuery = $"SELECT ifnull(max(TIMESTAMP),-1) as lastMsgDate FROM messages where channelId = {channel.Id}";
                using (var command = new MySqlCommand(selectQuery, dbConnection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string result = reader["lastMsgDate"].ToString();
                            if (result == "-1")
                            {
                                // we have NO messages for this channel, so let's get everything!
                                maxDateTime = DateTime.MinValue;
                            }
                            else
                            {
                                maxDateTime = DateTime.Parse(reader["lastMsgDate"].ToString());
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"Getting All New Messages after last date of {maxDateTime}");

            DateTime lastTimeStamp = DateTime.MinValue;
            int loop = 1;
            int remainingMessages = totalLimit - messagesFetched;

            bool breakLoop = false;
            while (remainingMessages > 0 && breakLoop == false)
            {
                
                int fetchCount = remainingMessages > messagesPerFetch ? messagesPerFetch : remainingMessages;

                string url = $"https://discord.com/api/v9/channels/{channel.Id}/messages?limit={fetchCount}";

                if (lastMessageId != null)
                {
                    url += $"&before={lastMessageId}";
                }
                else if (startingMsgId != -1)
                {
                    url += $"&before={startingMsgId}";
                }

                //Console.WriteLine(url);
                WriteMulticolorLine(new List<(string Text, ConsoleColor Color)> {
                    (channel.Name, ConsoleColor.Blue),
                    (" | ", ConsoleColor.White),
                    ($"{messagesFetched} / {totalLimit}", ConsoleColor.Yellow),
                    (" | ", ConsoleColor.White),
                    (lastTimeStamp.ToString("yyyy-MM-dd HH:mm:ss"), ConsoleColor.DarkMagenta)
                });
                //Console.WriteLine($"{channel.Name} | {messagesFetched} / {totalLimit}  {lastTimeStamp}");

                prepareClient(url, new HttpMethod("GET"));
                HttpResponseMessage response = await httpClient.SendAsync(httpRequest);

                if (response.IsSuccessStatusCode)
                {
                    List<Message> messages = new List<Message>();
                    string responseBody = "";
                    try
                    {
                        responseBody = await response.Content.ReadAsStringAsync();
                        SaveResponse(responseBody);
                        //Console.WriteLine($"{responseBody}");
                        messages = JsonConvert.DeserializeObject<Message[]>(responseBody).ToList<Message>();

                        foreach (var message in messages)
                        {
                            DateTime date = message.Timestamp;
                            lastTimeStamp = date;
                            message.json = JsonConvert.SerializeObject(message);
                            //Console.WriteLine($"JSON: {message.json}");
                            if (updateNew && (lastTimeStamp < maxDateTime))
                            {
                                // We've come to a message older than the newest message in the database, so we're all caught up!
                                Console.WriteLine($"{lastTimeStamp} < {maxDateTime}, all caught up, breaking out of loop!");
                                breakLoop = true;
                                break;
                            }
                            allMessages.Add(message);
                            SaveMessage(message);
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"FAILED parsing message! {responseBody}. {ex.Message} {ex.StackTrace}");
                    }

                    messagesFetched += messages.Count();
                    if (messages.Count() > 0)
                    {
                        lastMessageId = messages[messages.Count() - 1].Id;
                    }

                    remainingMessages = totalLimit - messagesFetched;

                    if (messages.Count() < messagesPerFetch)
                    {
                        remainingMessages = 0;
                    }

                }
                else
                {
                    Console.WriteLine($"ERROR happening getting messages! {url}. {response.StatusCode} ({(int)response.StatusCode})");
                    break;
                }

                //Console.WriteLine($"Response Status Code: {response.StatusCode} ({(int)response.StatusCode})");
                //Console.WriteLine($"Response Body: {responseBody}");

                loop++;
            }

            //TODO fix this, I don't understand what this does; it only affects the output. not the query, what was I trying to do with this?!
            if (orderBy == "desc")
            {
                allMessages.Reverse();
            }
            

            return (allMessages);
        }



        static public void SaveMessage(Message message)
        {
            string delQuery = "";
            string insertQuery = "";


            try
            {

                delQuery = "DELETE from messages where messageId = @id";
                using (var command = new MySqlCommand(delQuery, dbConnection))
                {
                    command.Parameters.AddWithValue("@id", message.Id);
                    command.ExecuteNonQuery();
                }

                //Console.WriteLine($"Inserting {message.Id}: {message.Author.Username} {message.Timestamp}");
                insertQuery = @"INSERT INTO messages (messageId, channelId, authorId, authorUsername, content, timestamp, json, lastUpdated) 
                        VALUES 
                        (@id, @channel, @author, @user, @content, @timestamp, @json, now())";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

        static public void SaveResponse(string response)
        {

            try
            {

                //Console.WriteLine($"Inserting {message.Id}: {message.Author.Username} {message.Timestamp}");
                string insertQuery = @"INSERT INTO rawresponses (rawResponse, lastUpdated) 
                        VALUES 
                        (@response, now())";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

        static public bool SaveUser(RootUserObject user, long userId = -1)
        {
            bool rv = false;

            string delQuery = "";
            string insertQuery = "";

            try
            {
                if (user != null)
                {
                    delQuery = "DELETE from users where userId = @id";
                    using (var command = new MySqlCommand(delQuery, dbConnection))
                    {
                        command.Parameters.AddWithValue("@id", user.User.Id);
                        command.ExecuteNonQuery();
                    }
                }

                if (user == null && userId != -1)
                {
                    // User is not on the server anymore, but let's mark them in the system so we don't keep trying to find them over and over again.
                    insertQuery = @"INSERT INTO users 
                    (userId, username, bio, legacyUsername, json) 
                        VALUES 
                    (@id, 'NULL', '', '', '')";
                    using (var command = new MySqlCommand(insertQuery, dbConnection))
                    {
                        command.Parameters.AddWithValue("@id", userId);
                        command.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Adding NULL entry for userId {userId}");
                    return (true);
                }



             

                insertQuery = @"INSERT INTO users 
                    (userId, username, bio, legacyUsername, json) 
                        VALUES 
                    (@id, @user, @bio, @legacy, @json)";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

            return (rv);
        }

        async static public Task<bool> SaveEmoji(Emoji emoji)
        {
            bool rv = false;

            string delQuery = "";
            string insertQuery = "";

            try
            {
                delQuery = "DELETE from emojis where id = @id";
                using (var command = new MySqlCommand(delQuery, dbConnection))
                {
                    command.Parameters.AddWithValue("@id", emoji.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO emojis
                    (id, name, rawJson) 
                        VALUES 
                    (@id, @name, @json)";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

            return (rv);
        }

        async static public Task<bool> SaveChannel(Channel channel)
        {
            bool rv = false;

            string delQuery = "";
            string insertQuery = "";

            try
            {

                delQuery = "DELETE from channels where channelId = @id";
                using (var command = new MySqlCommand(delQuery, dbConnection))
                {
                    command.Parameters.AddWithValue("@id", channel.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO channels
                    (serverId, channelId, type, lastMessageId, name, rateLimit, topic, position, rawJson) 
                        VALUES 
                    (@serverid, @channelid, @type, @lmi, @name, @rate, @topic, @pos, @json)";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

            return (rv);
        }

        static public bool SaveUserServerInfo(UserServerInfo user, long serverId = -1, long userId = -1)
        {
            bool rv = false;

            string delQuery = "";
            string insertQuery = "";

            if (user == null && serverId != -1 && userId != -1)
            {
                // User is not on the server anymore, but let's mark them in the system so we don't keep trying to find them over and over again.
                insertQuery = @"INSERT INTO usersServerInfo
                    (serverId, userId, username, joinDate, nick, lastModified, rawJson) 
                        VALUES 
                    (@serverid, @userid, 'NULL', null, '', now(), '')";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", serverId);
                    command.Parameters.AddWithValue("@userid", userId);
                    command.ExecuteNonQuery();
                }
                Console.WriteLine($"Adding NULL entry for userId {userId}, serverId {serverId}");
                return (true);
            }

            try
            {

                delQuery = "DELETE from usersServerInfo where serverId = @serverid and userId = @userid";
                using (var command = new MySqlCommand(delQuery, dbConnection))
                {
                    command.Parameters.AddWithValue("@serverid", user.serverId);
                    command.Parameters.AddWithValue("@userid", user.User.Id);
                    command.ExecuteNonQuery();
                }

                insertQuery = @"INSERT INTO usersServerInfo
                    (serverId, userId, username, joinDate, nick, lastModified, rawJson) 
                        VALUES 
                    (@serverid, @userid, @username, @join, @nick, now(), @json)";
                using (var command = new MySqlCommand(insertQuery, dbConnection))
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

            return (rv);
        }

        async static public Task<bool> splitWords()
        {
            bool rv = false;

            string selectQuery = $@"SELECT m.content, m.messageId, m.channelId, c.serverId 
                FROM messages m
                join channels c on m.channelId = c.channelId
                where 1=1";

            if (claChannel != null)
            {
                selectQuery += $" and c.name = {claChannel}";
            }

            if (claServerId != null)
            {
                selectQuery += $" and c.serverId = {claServerId}";
            }

            using (var command = new MySqlCommand(selectQuery, dbConnection))
            {
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
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

            return (rv);
        }

        async static public Task<bool> connectToDB()
        {
            bool rv = false;
            string host = Environment.GetEnvironmentVariable("MYSQLHOST");
            string user = Environment.GetEnvironmentVariable("MYSQLUSER");
            string pass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
            string database = Environment.GetEnvironmentVariable("MYSQLDATABASE");
            string connectionString = $"server={host};user={user};password={pass};database={database};AllowLoadLocalInfile=true;";

            try
            {
                dbConnection = new MySqlConnection(connectionString);
                dbConnection.Open();
                rv = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to database!! {ex.Message} {ex.StackTrace}");
                Thread.Sleep(5000);
                rv = false;
            }

            return (rv);
        }
        async static public Task<bool> sendText()
        {
            bool rv = false;

            string url = $"https://discord.com/api/v9/channels/{channelId}/messages";
            prepareClient(url, new HttpMethod("POST"));

            var obj = new { content = textToSend };
            string jsonContent = JsonConvert.SerializeObject(obj);

            httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.SendAsync(httpRequest);
            string responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Request: {jsonContent}");
            Console.WriteLine($"Response Status Code: {response.StatusCode} ({(int)response.StatusCode})");
            Console.WriteLine($"Response Body: {responseBody}");

            return (rv);
        }

        static public void prepareClient(string url, HttpMethod method)
        {

            var handler = new HttpClientHandler();
            handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                             System.Net.DecompressionMethods.Deflate;

            httpClient = new HttpClient(handler);
            httpRequest = new HttpRequestMessage(method, url);
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"{token}");
            httpRequest.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            httpRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9042 Chrome/120.0.6099.291 Electron/28.2.10 Safari/537.36");
            httpRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
            httpRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            httpRequest.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
            httpRequest.Headers.TryAddWithoutValidation("Origin", "https://discord.com");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\"");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            httpRequest.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            httpRequest.Headers.TryAddWithoutValidation("X-Debug-Options", "bugReporterEnabled");
            httpRequest.Headers.TryAddWithoutValidation("X-Discord-Locale:", "en-US");
            httpRequest.Headers.TryAddWithoutValidation("X-Discord-Timezone", "America/Los_Angeles");
        }


        public static void WriteMulticolorLine(List<(string Text, ConsoleColor Color)> parts)
        {
            foreach (var part in parts)
            {
                Console.ForegroundColor = part.Color;
                Console.Write(part.Text);
            }
            Console.ResetColor(); // Reset the color at the end
            Console.WriteLine();  // Move to the next line
        }

        public class Message
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("type")]
            public int Type { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("channel_id")]
            public string ChannelId { get; set; }

            [JsonProperty("author")]
            public Author Author { get; set; }

            [JsonProperty("attachments")]
            public List<Attachment> Attachments { get; set; }

            [JsonProperty("embeds")]
            public List<Embed> Embeds { get; set; }

            [JsonProperty("mentions")]
            public List<UserMention> Mentions { get; set; }

            [JsonProperty("mention_roles")]
            public List<string> MentionRoles { get; set; }

            [JsonProperty("pinned")]
            public bool Pinned { get; set; }

            [JsonProperty("mention_everyone")]
            public bool MentionEveryone { get; set; }

            [JsonProperty("tts")]
            public bool Tts { get; set; }

            [JsonProperty("message_reference")]
            public MsgReference messageReference { get; set; }
            
            [JsonProperty("referenced_message")]
            public Message referencedMessage { get; set; }

            [JsonProperty("sticker_items")]
            public List<MsgStickers> Stickers { get; set; } 

            [JsonProperty("timestamp")]
            public DateTime Timestamp { get; set; }

            [JsonProperty("edited_timestamp")]
            public DateTime? EditedTimestamp { get; set; }

            [JsonProperty("flags")]
            public int Flags { get; set; }

            [JsonProperty("components")]
            public List<Component> Components { get; set; }

            [JsonProperty("reactions")]
            public List<Reaction> Reactions { get; set; }



            public string json { get; set; }

        }

        public class MsgStickers 
        {
            public string id { get; set; }
            public string name { get; set; }
            public int format_type { get; set; }
        }

        public class MsgReference
        {
            public string channel_id { get; set; }
            public string message_id { get; set; }
            public string guild_id { get; set; }
        }

        public class Reaction
        {
            public Emoji Emoji { get; set; }
            public int Count { get; set; }
            public CountDetails CountDetails { get; set; }
            public List<object> BurstColors { get; set; } // Appears to be a list of objects (possibly empty)
            public bool MeBurst { get; set; }
            public bool BurstMe { get; set; }
            public bool Me { get; set; }
            public int BurstCount { get; set; }
        }

        public class CountDetails
        {
            public int Burst { get; set; }
            public int Normal { get; set; }
        }


        public class Author
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("username")]
            public string Username { get; set; }

            [JsonProperty("avatar")]
            public string Avatar { get; set; }

            [JsonProperty("discriminator")]
            public string Discriminator { get; set; }

            [JsonProperty("public_flags")]
            public int PublicFlags { get; set; }

            [JsonProperty("flags")]
            public int Flags { get; set; }

            [JsonProperty("banner")]
            public string Banner { get; set; }

            [JsonProperty("accent_color")]
            public string AccentColor { get; set; }

            [JsonProperty("global_name")]
            public string GlobalName { get; set; }

            [JsonProperty("avatar_decoration_data")]
            public AvatarDecorationData AvatarDecorationData { get; set; } // Changed from string to AvatarDecorationData

            [JsonProperty("banner_color")]
            public string BannerColor { get; set; }

            [JsonProperty("clan")]
            public string Clan { get; set; }

        }

        public class Attachment
        {
            public string id { get; set; }
            public string filename { get; set; }
            public long size { get; set; }
            public string url { get; set; }
            public string proxy_url { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public string content_type { get; set; }
            public int content_scan_version { get; set; }
            public string placeholder { get; set; }
            public int placeholder_version { get; set; }
        }

        public class Embed
        {
            public string type { get; set; }
            public string url { get; set; }
            public EmbedProvider provider { get; set; }
            public EmbedThumbnail thumbnail { get; set; }
            public EmbedVideo video { get; set; }
            public int content_scan_version { get; set; }
        }

        public class EmbedProvider
        {
            public string name { get; set; }
            public string url { get; set; }
        }

        public class EmbedVideo
        {
            public string url { get; set; }
            public string proxy_url { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public string placeholder { get; set; }
            public int placeholder_version { get; set; }
        }

        public class EmbedThumbnail
        {
            public string url { get; set; }
            public string proxy_url { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public string placeholder { get; set; }
            public int placeholder_version { get; set; }
        }

        public class UserMention
        {
            public long id { get; set; }
            public string username { get; set; }
            public string avatar { get; set; }
            public string discriminator { get; set; }
            public long public_flags { get; set; }
            public long flags { get; set; }
            public string banner { get; set; }
            public string accent_color { get; set; }
            public string global_name { get; set; }
            
            [JsonProperty("avatar_decoration_data")]
            public AvatarDecorationData AvatarDecorationData { get; set; } // Changed from string to AvatarDecorationData
            public string banner_color { get; set; }
            public string clan { get; set; }
        }

        public class Component
        {
            // Define component properties based on your API documentation
        }

        public class ServerHeader
        {
            public string Id { get; set; }
            public string name { get; set; }
        }

        public class Server
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("icon")]
            public string Icon { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("home_header")]
            public object HomeHeader { get; set; }

            [JsonProperty("splash")]
            public string Splash { get; set; }

            [JsonProperty("discovery_splash")]
            public string DiscoverySplash { get; set; }

            [JsonProperty("features")]
            public string[] Features { get; set; }

            [JsonProperty("banner")]
            public object Banner { get; set; }

            [JsonProperty("owner_id")]
            public string OwnerId { get; set; }

            [JsonProperty("application_id")]
            public object ApplicationId { get; set; }

            [JsonProperty("region")]
            public string Region { get; set; }

            [JsonProperty("afk_channel_id")]
            public object AfkChannelId { get; set; }

            [JsonProperty("afk_timeout")]
            public int AfkTimeout { get; set; }

            [JsonProperty("system_channel_id")]
            public string SystemChannelId { get; set; }

            [JsonProperty("system_channel_flags")]
            public int SystemChannelFlags { get; set; }

            [JsonProperty("widget_enabled")]
            public bool WidgetEnabled { get; set; }

            [JsonProperty("widget_channel_id")]
            public object WidgetChannelId { get; set; }

            [JsonProperty("verification_level")]
            public int VerificationLevel { get; set; }

            [JsonProperty("roles")]
            public Role[] Roles { get; set; }

            [JsonProperty("default_message_notifications")]
            public int DefaultMessageNotifications { get; set; }

            [JsonProperty("mfa_level")]
            public int MfaLevel { get; set; }

            [JsonProperty("explicit_content_filter")]
            public int ExplicitContentFilter { get; set; }

            [JsonProperty("max_presences")]
            public object MaxPresences { get; set; }

            [JsonProperty("max_members")]
            public int MaxMembers { get; set; }

            [JsonProperty("max_stage_video_channel_users")]
            public int MaxStageVideoChannelUsers { get; set; }

            [JsonProperty("max_video_channel_users")]
            public int MaxVideoChannelUsers { get; set; }

            [JsonProperty("vanity_url_code")]
            public string VanityUrlCode { get; set; }

            [JsonProperty("premium_tier")]
            public int PremiumTier { get; set; }

            [JsonProperty("premium_subscription_count")]
            public int PremiumSubscriptionCount { get; set; }

            [JsonProperty("preferred_locale")]
            public string PreferredLocale { get; set; }

            [JsonProperty("rules_channel_id")]
            public string RulesChannelId { get; set; }

            [JsonProperty("safety_alerts_channel_id")]
            public string SafetyAlertsChannelId { get; set; }

            [JsonProperty("public_updates_channel_id")]
            public string PublicUpdatesChannelId { get; set; }

            [JsonProperty("hub_type")]
            public object HubType { get; set; }

            [JsonProperty("premium_progress_bar_enabled")]
            public bool PremiumProgressBarEnabled { get; set; }

            [JsonProperty("latest_onboarding_question_id")]
            public string LatestOnboardingQuestionId { get; set; }

            [JsonProperty("nsfw")]
            public bool Nsfw { get; set; }

            [JsonProperty("nsfw_level")]
            public int NsfwLevel { get; set; }

            [JsonProperty("emojis")]
            public Emoji[] Emojis { get; set; }

            [JsonProperty("stickers")]
            public Sticker[] Stickers { get; set; }

            [JsonProperty("incidents_data")]
            public IncidentsData IncidentsData { get; set; }

            [JsonProperty("inventory_settings")]
            public object InventorySettings { get; set; }

            [JsonProperty("embed_enabled")]
            public bool EmbedEnabled { get; set; }

            [JsonProperty("embed_channel_id")]
            public object EmbedChannelId { get; set; }


            public string rawJson { get; set; }

        }

        // A slender version of the Server class. Contains member counts also, server class doesn't.
        public class ServerPreview
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("icon")]
            public string Icon { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("home_header")]
            public object HomeHeader { get; set; }

            [JsonProperty("splash")]
            public string Splash { get; set; }

            [JsonProperty("discovery_splash")]
            public string DiscoverySplash { get; set; }

            [JsonProperty("features")]
            public string[] Features { get; set; }

            [JsonProperty("approximate_member_count")]
            public int MemberCount { get; set; }

            [JsonProperty("approximate_presence_count")]
            public int PresenceCount { get; set; }

            [JsonProperty("emojis")]
            public Emoji[] Emojis { get; set; }

            [JsonProperty("stickers")]
            public Sticker[] Stickers { get; set; }

        }

        public class Role
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("permissions")]
            public string Permissions { get; set; }

            [JsonProperty("position")]
            public int Position { get; set; }

            [JsonProperty("color")]
            public int Color { get; set; }

            [JsonProperty("hoist")]
            public bool Hoist { get; set; }

            [JsonProperty("managed")]
            public bool Managed { get; set; }

            [JsonProperty("mentionable")]
            public bool Mentionable { get; set; }

            [JsonProperty("icon")]
            public object Icon { get; set; }

            [JsonProperty("unicode_emoji")]
            public object UnicodeEmoji { get; set; }

            [JsonProperty("flags")]
            public int Flags { get; set; }
        }

        public class Emoji
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("roles")]
            public string[] Roles { get; set; }

            [JsonProperty("require_colons")]
            public bool RequireColons { get; set; }

            [JsonProperty("managed")]
            public bool Managed { get; set; }

            [JsonProperty("animated")]
            public bool Animated { get; set; }

            [JsonProperty("available")]
            public bool Available { get; set; }


            public string rawJson { get; set; }
        }

        public class Sticker
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("tags")]
            public string Tags { get; set; }

            [JsonProperty("type")]
            public int Type { get; set; }

            [JsonProperty("format_type")]
            public int FormatType { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("asset")]
            public string Asset { get; set; }

            [JsonProperty("available")]
            public bool Available { get; set; }

            [JsonProperty("guild_id")]
            public string GuildId { get; set; }
        }

        public class IncidentsData
        {
            [JsonProperty("invites_disabled_until")]
            public string InvitesDisabledUntil { get; set; }

            [JsonProperty("dms_disabled_until")]
            public object DmsDisabledUntil { get; set; }
        }

        public class Channel
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("type")]
            public int Type { get; set; }

            [JsonProperty("guild_id")]
            public string GuildId { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("last_message_id")]
            public string LastMessageId { get; set; }

            [JsonProperty("flags")]
            public int Flags { get; set; }

            [JsonProperty("last_pin_timestamp")]
            public DateTime? LastPinTimestamp { get; set; }

            [JsonProperty("parent_id")]
            public string ParentId { get; set; }

            [JsonProperty("rate_limit_per_user")]
            public int? RateLimitPerUser { get; set; }

            [JsonProperty("topic")]
            public string Topic { get; set; }

            [JsonProperty("default_thread_rate_limit_per_user")]
            public int? DefaultThreadRateLimitPerUser { get; set; }

            [JsonProperty("position")]
            public int Position { get; set; }

            [JsonProperty("permission_overwrites")]
            public List<PermissionOverwrite> PermissionOverwrites { get; set; }

            [JsonProperty("nsfw")]
            public bool? Nsfw { get; set; }

            [JsonProperty("icon_emoji")]
            public Emoji IconEmoji { get; set; }

            [JsonProperty("theme_color")]
            public int? ThemeColor { get; set; }

            [JsonProperty("bitrate")]
            public int? Bitrate { get; set; }

            [JsonProperty("user_limit")]
            public int? UserLimit { get; set; }

            [JsonProperty("rtc_region")]
            public string RtcRegion { get; set; }

            public string rawJson { get; set; }

        }

        public class PermissionOverwrite
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("type")]
            public int Type { get; set; }

            [JsonProperty("allow")]
            public string Allow { get; set; }

            [JsonProperty("deny")]
            public string Deny { get; set; }

        }

        public class AvatarDecorationData
        {
            [JsonProperty("asset")]
            public string Asset { get; set; }

            [JsonProperty("sku_id")]
            public string SkuId { get; set; }
        }


        //////////////////////////////////////// USER OBJECTS ///////////////////////////////

        public class RootUserObject
        {
            [JsonProperty("user")]
            public User User { get; set; }

            [JsonProperty("connected_accounts")]
            public List<object> ConnectedAccounts { get; set; }

            [JsonProperty("premium_since")]
            public object PremiumSince { get; set; }

            [JsonProperty("premium_type")]
            public object PremiumType { get; set; }

            [JsonProperty("premium_guild_since")]
            public object PremiumGuildSince { get; set; }

            [JsonProperty("profile_themes_experiment_bucket")]
            public int ProfileThemesExperimentBucket { get; set; }

            [JsonProperty("user_profile")]
            public UserProfile UserProfile { get; set; }

            [JsonProperty("badges")]
            public List<Badge> Badges { get; set; }

            [JsonProperty("guild_badges")]
            public List<object> GuildBadges { get; set; }

            [JsonProperty("mutual_guilds")]
            public List<MutualGuild> MutualGuilds { get; set; }

            [JsonProperty("legacy_username")]
            public string LegacyUsername { get; set; }

            public string rawJson { get; set; }
        }

        public class User
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            [JsonProperty("username")]
            public string Username { get; set; }
            [JsonProperty("global_name")]
            public string GlobalName { get; set; }
            [JsonProperty("avatar")]
            public string Avatar { get; set; }
            [JsonProperty("avatar_decoration_data")]
            public AvatarDecorationData AvatarDecorationData { get; set; }

            [JsonProperty("discriminator")]
            public string Discriminator { get; set; }
            [JsonProperty("public_flags")]
            public int PublicFlags { get; set; }
            [JsonProperty("clan")]
            public object Clan { get; set; }
            [JsonProperty("flags")]
            public int Flags { get; set; }
            [JsonProperty("banner")]
            public object Banner { get; set; }
            [JsonProperty("banner_color")]
            public string BannerColor { get; set; }
            [JsonProperty("accent_color")]
            public int? AccentColor { get; set; }
            [JsonProperty("bio")]
            public string Bio { get; set; }
        }


        public class UserProfile
        {
            [JsonProperty("bio")]
            public string Bio { get; set; }

            [JsonProperty("accent_color")]
            public int? AccentColor { get; set; }

            [JsonProperty("pronouns")]
            public string Pronouns { get; set; }
        }

        public class Badge
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("icon")]
            public string Icon { get; set; }
        }

        public class MutualGuild
        {
            [JsonProperty("id")]
            public string Id { get; set; }

            [JsonProperty("nick")]
            public string Nick { get; set; }
        }


        // UserSERVERInfo
        public class UserServerInfo
        {
            public long serverId { get; set; }
            [JsonProperty("avatar")]
            public string Avatar { get; set; }
            [JsonProperty("communication_disabled_until")]
            public DateTime? CommunicationDisabledUntil { get; set; }
            [JsonProperty("flags")]
            public int Flags { get; set; }
            [JsonProperty("joined_at")]
            public DateTime JoinedAt { get; set; }
            [JsonProperty("nick")]
            public string Nick { get; set; }
            [JsonProperty("pending")]
            public bool Pending { get; set; }
            [JsonProperty("premium_since")]
            public DateTime? PremiumSince { get; set; }
            [JsonProperty("roles")]
            public List<string> Roles { get; set; }
            [JsonProperty("unusual_dm_activity_until")]
            public string UnusualDmActivityUntil { get; set; }
            [JsonProperty("user")]
            public User User { get; set; }
            [JsonProperty("mute")]
            public bool Mute { get; set; }
            [JsonProperty("deaf")]
            public bool Deaf { get; set; }

            public UserServerInfo()
            {
                Roles = new List<string>();
            }

            public string rawJson { get; set; }
        }




        


        static bool parseArgs(string[] args)
        {
            bool showHelp = false;
            bool showVer = false;



            var p = new OptionSet() {
                { "messages", "View messages in channel specified", v => boolViewMessages = true },
                { "servers", "View servers you are connected to.", v=> boolViewServers = true },
                { "channels", "View Channels available on a server.", v=> boolViewChannels = true },
                { "roles", "View Roles available on a server.", v=> boolViewRoles = true },
                { "emojis", "View Emojis available on a server.", v=> boolViewEmojis = true },
                { "emojireacts", "Look at emoji reactions.", v=> boolViewEmojiReacts = true },
                { "addmentions", "Process all Mentions of people.", v=> boolAddMentions = true },
                { "stickers", "View Stickers available on a server.", v=> boolViewStickers = true },
                { "server=", "Specify server, pass it's Id", v=> claServerId = v },
                { "preview", "Get minimal server info, but also gets Member Counts.", v=> boolServerPreview = true },
                { "msglimit=", "Total # of messages to retrieve. Default=100.", v=> totalMessageLimit = int.Parse(v) },
                { "beforemsg=", "Get messages prior to this messageId.", v=> startingMsgId = long.Parse(v) },
                { "msgpp=", "# of message to retrieve per request. Default 100.", v=> messagesPerFetch = int.Parse(v) },
                { "say=", "What text to send.",option => textToSend = option },
                { "saveusers", "Look up ALL user info and update the database.", v => boolUpdateUsers = true },
                { "saveuserserverinfo", "Look up ALL server specific user info and update the database.", v => boolUpdateUserServerInfo = true },
                { "updatemessages", "Update ALL channels with current messages newer than last fetched.", v => boolUpdateAllMessages = true },
                { "forceall", "Reset last updated and force update of ALL messages", v => boolForceAll = true },
                { "reprocessjson", "Run thru the already saved JSON in the Database and reprocess data we didn't the first time.", v => boolReprocessJson = true },
                { "wordcount", "Do a word count of all messages for the stats table.", v => boolWordCount = true },
                { "profile=", "View someone's profile", option => userId = option },
                { "channelId=", "Channel ID to view or send message to.",option => channelId = option },
                { "channel=", "Channel name to view or send message to.",option => claChannel = option },
                { "order=", "Date Order, 'desc' or 'asc'. UNIMPLEMENTED?",option => orderBy = option },
                { "t", "TEST MODE, don't update any database tables.", option => testMode = true },
                { "d", "DEBUG MODE, print out extra info.", option => debugMode = true },
                { "h|help",  "show this message and exit", v => showHelp = v != null },
                { "v|ver|version", "Display application version.", v=> showVer = true }
            };

            try
            {
                p.Parse(args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Invalid ARGS: " + e.Message);
                return (false);
            }

            if (args.Length == 0)
            {
                Console.WriteLine("You need to pass in an argument.");
                ShowHelp(p);
                return (false);
            }

            if (debugMode)
            {
                Console.WriteLine("DEBUG MODE ON!");
            }

            if (showVer)
            {
                Console.WriteLine("DiscTalk, Copyright (C) 2024 Version " + Assembly.GetExecutingAssembly().GetName().Version);
            }

            if (showHelp)
            {
                ShowHelp(p);
            }


            if (showVer || showHelp)
            {
                // Don't continue to execute the program.
                return (false);
            }

            return (true); // Successfully parsed, nothing requires program to stop.
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }


    }

}

