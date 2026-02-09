using Mono.Options;
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
using System.Runtime.CompilerServices;
using Disctalk.Models;

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
        static public bool boolListSkipChannels = false;
        static public string addSkipChannel = null;
        static public string removeSkipChannel = null;
        static public string claServerId = null;
        static public string orderBy = "asc"; // anything not "desc" will imply "asc"
        static public bool testMode = false;
        static public bool debugMode = false;
        static DiscordApiClient _apiClient;
        static DatabaseRepository _db;


        static async Task Main(string[] args)
        {
            try
            {

                if (!ParseArgs(args)) { return; }

                _apiClient = new DiscordApiClient(Environment.GetEnvironmentVariable("MY_BOT_TOKEN"));
                _apiClient.MessagesPerFetch = messagesPerFetch;
                _apiClient.StartingMessageId = startingMsgId;
                _apiClient.OrderBy = orderBy;

                try
                {
                    _db = await DatabaseRepository.ConnectAsync();
                }
                catch (Exception)
                {
                    string host = Environment.GetEnvironmentVariable("MYSQLHOST");
                    string user = Environment.GetEnvironmentVariable("MYSQLUSER");
                    string pass = Environment.GetEnvironmentVariable("MYSQLPASSWORD");
                    string database = Environment.GetEnvironmentVariable("MYSQLDATABASE");
                    Console.WriteLine($"FAILED TO CONNECT TO DATABASE! {host},{user},{pass},{database}");
                    await Task.Delay(5000);
                    return;
                }

                if (textToSend != null)
                {
                    await _apiClient.SendTextAsync(channelId, textToSend);
                    return;
                }

                if (boolReprocessJson)
                {
                    // This is really specific coding for stuff that I missed the first time grabbing the data, which is why
                    // I saved all the rawJson in the database, so I could reprocess it without regrabbing a million API calls again!
                    long channelIdValue = channelId != null ? long.Parse(channelId) : -1;
                    await _db.ReprocessJsonAsync(channelIdValue, claServerId, boolViewEmojiReacts, boolAddMentions, claChannel);
                    return;
                }

                if (boolWordCount)
                {
                    await _db.UpdateWordCountsAsync(claChannel, claServerId);
                    return;
                }

                if (boolListSkipChannels)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must specify --server to list skip channels.");
                        return;
                    }

                    var skipChannels = await _db.GetSkipChannelsWithReasonsAsync(long.Parse(claServerId));
                    if (skipChannels.Count == 0)
                    {
                        Console.WriteLine($"No skip channels configured for server {claServerId}");
                    }
                    else
                    {
                        Console.WriteLine($"Skip channels for server {claServerId}:");
                        foreach (var (channelId, reason) in skipChannels)
                        {
                            Console.WriteLine($"  {channelId}: {reason}");
                        }
                    }
                    return;
                }

                if (addSkipChannel != null)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must specify --server to add a skip channel.");
                        return;
                    }

                    long channelIdToAdd = long.Parse(addSkipChannel);
                    Console.Write("Enter reason (optional): ");
                    string reason = Console.ReadLine();
                    await _db.AddSkipChannelAsync(long.Parse(claServerId), channelIdToAdd, reason);
                    return;
                }

                if (removeSkipChannel != null)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must specify --server to remove a skip channel.");
                        return;
                    }

                    long channelIdToRemove = long.Parse(removeSkipChannel);
                    await _db.RemoveSkipChannelAsync(long.Parse(claServerId), channelIdToRemove);
                    return;
                }

                if (boolViewMessages)
                {
                    await RunViewMessagesAsync();
                    return;
                }

                if (boolViewServers)
                {
                    await _apiClient.ViewServersAsync();
                    return;
                }

                if (boolViewChannels)
                {
                    await RunViewChannelsAsync();
                    return;
                }

                if (userId != null)
                {
                    if (claServerId != null)
                    {
                        await _db.ViewUserServerProfileAsync(long.Parse(claServerId), long.Parse(userId), _apiClient);
                    }
                    else
                    {
                        await _db.ViewUserProfileAsync(long.Parse(userId), _apiClient);
                    }

                    return;
                }

                if (boolUpdateUsers)
                {
                    await _db.UpdateAllUsersAsync(_apiClient);
                    return;
                }

                if (boolUpdateUserServerInfo)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine($"You need to pass in a serverId to update user Server Info.");
                        return;
                    }

                    await _db.UpdateAllUserServerInfoAsync(long.Parse(claServerId), _apiClient);
                    return;
                }

                if (boolUpdateAllMessages)
                {
                    if (claServerId == null)
                    {
                        Console.WriteLine("You must pass a serverId to update messages on.");
                        return;
                    }

                    await UpdateAllMessagesAsync(long.Parse(claServerId));

                    return;
                }

                if (claServerId != null)
                {
                    await RunViewServerAsync();
                    return;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"main() died with unhandled exception: {ex.Message} {ex.StackTrace}");
                Console.WriteLine("Dying...");
                await Task.Delay(5000);
                Environment.Exit(1);
            }
            finally
            {
                Console.WriteLine("Exiting...");
                await Task.Delay(1000);
            }

        }


        async static public Task<bool> UpdateAllMessagesAsync(long serverId)
        {
            bool rv = false;

            // Load skip channels from database
            List<long> skipChannels = await _db.GetSkipChannelsAsync(serverId);

            List<Channel> channels = await _apiClient.GetChannelsAsync(serverId);
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
                List<Message> messages = await _apiClient.GetMessagesAsync(c, 9999999, !boolForceAll, await _db.GetMaxTimestampForChannelAsync(c.Id), _db.SaveMessage, _db.SaveResponse); //TODO what about channels with over 9999999 messages?!
                Console.WriteLine($"Updated {messages.Count()} in channel {c.Name} ({c.Id})");
                totalMessagesUpdated += messages.Count();
            }

            Console.WriteLine($"\nALL CHANNELS UPDATED! {totalMessagesUpdated} total messages across all channels.");

            return (rv);
        }

        async static Task RunViewMessagesAsync()
        {
            if (claServerId == null)
            {
                Console.WriteLine("You must pass a serverID first.");
                return;
            }

            List<Channel> channels = await _apiClient.GetChannelsAsync(long.Parse(claServerId));
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

            List<Message> messages = await _apiClient.GetMessagesAsync(channelMatch, totalMessageLimit, false, DateTime.MinValue, _db.SaveMessage, _db.SaveResponse);
            foreach (Message message in messages)
            {
                DateTime date = message.Timestamp;
                string dateNice = date.ToString("yyyy-MM-dd HH:mm:ss");
                string msg = $"{message.Id}|{dateNice} {message.Author.Username} ({message.Author.Id}): {message.Content}";
                Console.WriteLine(msg);
                if (debugMode) { Console.WriteLine(message.json); }
            }
        }

        async static Task RunViewChannelsAsync()
        {
            List<Channel> channels = await _apiClient.GetChannelsAsync(long.Parse(claServerId));
            if (channels == null)
            {
                Console.WriteLine("Error getting channels, dying.");
                return;
            }

            foreach (var channel in channels)
            {
                Console.WriteLine($"{channel.Id}: {channel.Name}. Rate: {channel.RateLimitPerUser}. ");
                channel.rawJson = JsonConvert.SerializeObject(channel);
                await _db.SaveChannel(channel);
            }
        }

        async static Task RunViewServerAsync()
        {
            if (boolViewEmojis && claServerId == null)
            {
                Console.WriteLine("You need to specify a serverId to view emojis, dork.");
                return;
            }

            dynamic server = await _apiClient.GetServerAsync(claServerId, boolServerPreview);
            Console.WriteLine($"{server.Name}: {server.Description}");

            if (boolServerPreview)
            {
                Console.WriteLine($"{server.MemberCount} members, {server.PresenceCount} presence.");
            }
            else
            {
                if (boolViewRoles)
                {
                    foreach (var role in server.Roles)
                    {
                        Console.WriteLine($"{role.Name}: {role.Description}");
                    }
                }
            }

            if (boolViewEmojis)
            {
                foreach (Emoji emoji in server.Emojis)
                {
                    emoji.rawJson = JsonConvert.SerializeObject(emoji);
                    Console.WriteLine($"{emoji.Name}");
                    await _db.SaveEmoji(emoji);
                }
            }

            if (boolViewStickers)
            {
                foreach (var sticker in server.Stickers)
                {
                    Console.WriteLine($"{sticker.Name}");
                }
            }
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





        


        static bool ParseArgs(string[] args)
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
                { "aftermsg=", "TESTETESTTESTSE Get messages AFTER to this messageId.", v=> startingMsgId = long.Parse(v) },
                { "msgpp=", "# of message to retrieve per request. Default 100.", v=> messagesPerFetch = int.Parse(v) },
                { "say=", "What text to send.",option => textToSend = option },
                { "saveusers", "Look up ALL user info and update the database.", v => boolUpdateUsers = true },
                { "saveuserserverinfo", "Look up ALL server specific user info and update the database.", v => boolUpdateUserServerInfo = true },
                { "updatemessages", "Update ALL channels with current messages newer than last fetched.", v => boolUpdateAllMessages = true },
                { "forceall", "Reset last updated and force update of ALL messages", v => boolForceAll = true },
                { "reprocessjson", "Run thru the already saved JSON in the Database and reprocess data we didn't the first time.", v => boolReprocessJson = true },
                { "wordcount", "Do a word count of all messages for the stats table.", v => boolWordCount = true },
                { "skipchannels", "List skip channels for a server (requires --server).", v => boolListSkipChannels = true },
                { "addskip=", "Add a channel to skip list (requires --server). Will prompt for reason.", option => addSkipChannel = option },
                { "removeskip=", "Remove a channel from skip list (requires --server).", option => removeSkipChannel = option },
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

