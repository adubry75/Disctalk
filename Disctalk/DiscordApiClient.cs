using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Disctalk.Models;

namespace Disctalk
{
    public class DiscordApiClient
    {
        private readonly HttpClient _httpClient;

        public int MessagesPerFetch { get; set; } = 100;
        public long StartingMessageId { get; set; } = -1;
        public string OrderBy { get; set; } = "asc";

        public bool TooManyRequests { get; private set; }
        public bool KillSwitch { get; set; }

        public DiscordApiClient(string token)
        {
            var handler = new HttpClientHandler();
            handler.AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate;

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9042 Chrome/120.0.6099.291 Electron/28.2.10 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://discord.com");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua", "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\"");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Debug-Options", "bugReporterEnabled");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Discord-Locale:", "en-US");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Discord-Timezone", "America/Los_Angeles");
        }

        private async Task HandleRateLimitAsync(HttpResponseMessage response)
        {
            Console.WriteLine("Too many requests...");

            foreach (var header in response.Headers)
            {
                Console.WriteLine($"Header: {header.Key}: {string.Join(", ", header.Value)}");
            }

            foreach (var contentHeader in response.Content.Headers)
            {
                Console.WriteLine($"ContentHeader: {contentHeader.Key}: {string.Join(", ", contentHeader.Value)}");
            }

            int sleepSeconds = 90;
            if (response.Headers.TryGetValues("Retry-After", out IEnumerable<string> values))
            {
                var retryAfterValue = values.First();
                if (int.TryParse(retryAfterValue, out int retrySeconds))
                {
                    Console.WriteLine($"\nDELAY 429!!! Setting 429 delay time to Retry-After value of {retrySeconds} seconds");
                    sleepSeconds = retrySeconds + 2;
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

            await Task.Delay(sleepSeconds * 1000);
            TooManyRequests = true;
        }

        public async Task<dynamic> GetServerAsync(string guildId, bool preview = false)
        {
            Server server = new Server();

            string url = $"https://discord.com/api/v9/guilds/{guildId}" + (preview ? "/preview" : "");
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                if (preview)
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

        public async Task<bool> ViewServersAsync()
        {
            bool rv = false;

            string url = "https://discord.com/api/v9/users/@me/guilds";
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

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

            return rv;
        }

        public async Task<List<Channel>> GetChannelsAsync(long serverId)
        {
            List<Channel> channels = new List<Channel>();

            string url = $"https://discord.com/api/v9/guilds/{serverId}/channels";
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                channels = JsonConvert.DeserializeObject<Channel[]>(responseBody).ToList<Channel>();
            }
            else
            {
                Console.WriteLine($"Error getting list of channels! {response.StatusCode} ({(int)response.StatusCode})");
                return null;
            }

            return channels;
        }

        public async Task<List<Message>> GetMessagesAsync(Channel channel, int totalLimit, bool updateNew, DateTime maxDateTime, Action<Message> onMessageFetched, Action<string> onResponseFetched)
        {
            string lastMessageId = null;
            int messagesFetched = 0;
            List<Message> allMessages = new List<Message>();

            Console.WriteLine($"Getting All New Messages after last date of {maxDateTime}");

            DateTime lastTimeStamp = DateTime.MinValue;
            int loop = 1;
            int remainingMessages = totalLimit - messagesFetched;

            bool breakLoop = false;
            while (remainingMessages > 0 && !breakLoop)
            {
                int fetchCount = remainingMessages > MessagesPerFetch ? MessagesPerFetch : remainingMessages;

                string url = $"https://discord.com/api/v9/channels/{channel.Id}/messages?limit={fetchCount}";

                if (lastMessageId != null)
                {
                    url += $"&before={lastMessageId}";
                }
                else if (StartingMessageId != -1)
                {
                    Console.WriteLine($"  and before messageId {StartingMessageId}");
                    url += $"&before={StartingMessageId}";
                }

                Program.WriteMulticolorLine(new List<(string Text, ConsoleColor Color)> {
                    (channel.Name, ConsoleColor.Blue),
                    (" | ", ConsoleColor.White),
                    ($"{messagesFetched} / {totalLimit}", ConsoleColor.Yellow),
                    (" | ", ConsoleColor.White),
                    (lastTimeStamp.ToString("yyyy-MM-dd HH:mm:ss"), ConsoleColor.DarkMagenta)
                });

                var request = new HttpRequestMessage(new HttpMethod("GET"), url);
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    List<Message> messages = new List<Message>();
                    string responseBody = "";
                    try
                    {
                        responseBody = await response.Content.ReadAsStringAsync();
                        onResponseFetched?.Invoke(responseBody);
                        messages = JsonConvert.DeserializeObject<Message[]>(responseBody).ToList<Message>();

                        foreach (var message in messages)
                        {
                            DateTime date = message.Timestamp;
                            lastTimeStamp = date;
                            message.json = JsonConvert.SerializeObject(message);
                            if (updateNew && (lastTimeStamp < maxDateTime) && StartingMessageId == -1)
                            {
                                Console.WriteLine($"{lastTimeStamp} < {maxDateTime}, all caught up, breaking out of loop!");
                                breakLoop = true;
                                break;
                            }
                            allMessages.Add(message);
                            onMessageFetched?.Invoke(message);
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

                    if (messages.Count() < MessagesPerFetch)
                    {
                        remainingMessages = 0;
                    }
                }
                else
                {
                    Console.WriteLine($"ERROR happening getting messages! {url}. {response.StatusCode} ({(int)response.StatusCode})");
                    break;
                }

                loop++;
            }

            if (OrderBy == "desc")
            {
                allMessages.Reverse();
            }

            return allMessages;
        }

        public async Task<RootUserObject> GetUserAsync(long userId)
        {
            RootUserObject user = null;

            string url = $"https://discord.com/api/v9/users/{userId}/profile";
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            string responseBody = "";
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    user = JsonConvert.DeserializeObject<RootUserObject>(responseBody);
                    user.rawJson = responseBody;
                    TooManyRequests = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON for userId {userId}. {ex.Message} {ex.StackTrace} JSON:{responseBody}");
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine($"\nForbidden response, user probably no longer on server.");
                return null;
            }
            else if ((int)response.StatusCode == 429)
            {
                await HandleRateLimitAsync(response);
                return null;
            }
            else
            {
                Console.WriteLine($"Error getting user {userId}! {response.StatusCode} ({(int)response.StatusCode})");
            }

            return user;
        }

        public async Task<UserServerInfo> GetUserServerInfoAsync(long serverId, long userId)
        {
            UserServerInfo user = null;

            string url = $"https://discord.com/api/v9/guilds/{serverId}/members/{userId}";
            var request = new HttpRequestMessage(new HttpMethod("GET"), url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            string responseBody = "";
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    user = JsonConvert.DeserializeObject<UserServerInfo>(responseBody);
                    user.serverId = serverId;
                    user.rawJson = responseBody;
                    TooManyRequests = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing JSON for userId {userId}, server {serverId}. {ex.Message} {ex.StackTrace} JSON:{responseBody}");
                }
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                Console.WriteLine($"\nForbidden response, user probably no longer on server.");
                return null;
            }
            else if ((int)response.StatusCode == 429)
            {
                await HandleRateLimitAsync(response);
                return null;
            }
            else if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Console.WriteLine($"404 Not Found, user must have left server recently. Skipping.");
                return null;
            }
            else
            {
                Console.WriteLine($"Error getting user {userId}, server {serverId}! {response.StatusCode} ({(int)response.StatusCode}) JSON: {responseBody}");
            }

            return user;
        }

        public async Task<bool> SendTextAsync(string channelId, string text)
        {
            bool rv = false;

            string url = $"https://discord.com/api/v9/channels/{channelId}/messages";
            var obj = new { content = text };
            string jsonContent = JsonConvert.SerializeObject(obj);

            var request = new HttpRequestMessage(new HttpMethod("POST"), url);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Request: {jsonContent}");
            Console.WriteLine($"Response Status Code: {response.StatusCode} ({(int)response.StatusCode})");
            Console.WriteLine($"Response Body: {responseBody}");

            return rv;
        }
    }
}
