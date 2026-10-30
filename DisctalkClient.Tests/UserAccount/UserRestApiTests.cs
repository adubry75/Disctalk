using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.UserAccount;
using static DisctalkClient.Tests.UserAccount.UserTestKit;

namespace DisctalkClient.Tests.UserAccount;

public class UserRestApiTests
{
    const string MessageJson = """{"id":"9","channel_id":"10","type":0,"author":{"id":"110000000000000100","username":"whiskers"},"content":"hi"}""";

    readonly FakeHttpHandler _network;
    readonly UserRestApi _api;

    public UserRestApiTests()
    {
        _network = new FakeHttpHandler((req, _) => req.Method == HttpMethod.Delete || req.RequestUri!.AbsolutePath.EndsWith("/typing") || req.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : FakeHttpHandler.Json(req.RequestUri!.AbsolutePath.EndsWith("/messages") && req.Method == HttpMethod.Get ? $"[{MessageJson}]" : MessageJson));
        _api = new UserRestApi(Http(_network));
    }

    [Fact]
    public async Task Requests_carry_the_archivers_identity_and_the_raw_user_token()
    {
        await _api.GetMessagesBeforeAsync(10, 5, 50);

        var sent = Assert.Single(_network.Requests);
        Assert.Equal("https://discord.com/api/v10/channels/10/messages?limit=50&before=5", sent.Uri.ToString());
        Assert.Equal(UserToken, sent.Headers["Authorization"]); // no "Bot " prefix
        Assert.Equal(Profile.UserAgent, sent.Headers["User-Agent"]);
        Assert.Equal(Profile.GetHeader("X-Discord-Timezone"), sent.Headers["X-Discord-Timezone"]);
        var superProps = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(sent.Headers["X-Super-Properties"])))!;
        Assert.Equal(Profile.FallbackBuildNumber, (int)superProps["client_build_number"]!);
    }

    [Fact]
    public async Task First_page_has_no_before_and_limit_is_clamped()
    {
        var page = await _api.GetMessagesBeforeAsync(10, null, 500);

        Assert.Equal("hi", Assert.Single(page).Content);
        Assert.Equal("https://discord.com/api/v10/channels/10/messages?limit=100", _network.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Sending_a_reply_posts_nonce_reference_and_allowed_mentions()
    {
        var sent = await _api.SendMessageAsync(new SendMessageRequest
        {
            ChannelId = 10,
            Content = "hey <@5>",
            ReplyToMessageId = 8,
            AllowedMentions = new AllowedMentions { Users = [5], AllRoles = true, RepliedUser = false },
        });

        Assert.Equal("hi", sent.Content);
        var req = Assert.Single(_network.Requests);
        Assert.Equal((HttpMethod.Post, "/api/v10/channels/10/messages"), (req.Method, req.Uri.AbsolutePath));
        var body = JsonNode.Parse(req.Body!)!;
        Assert.Equal("hey <@5>", (string)body["content"]!);
        Assert.True(ulong.Parse((string)body["nonce"]!) > 0);
        Assert.Equal("8", (string)body["message_reference"]!["message_id"]!);
        Assert.False((bool)body["message_reference"]!["fail_if_not_exists"]!);
        Assert.Equal("""{"parse":["roles"],"users":["5"],"replied_user":false}""", body["allowed_mentions"]!.ToJsonString());
    }

    [Fact]
    public async Task Files_are_sent_as_multipart_with_payload_json()
    {
        await _api.SendMessageAsync(new SendMessageRequest
        {
            ChannelId = 10,
            Content = "pic",
            Files = [new OutgoingFile("cat.png", new byte[] { 1, 2, 3 }, "image/png")],
        });

        var req = Assert.Single(_network.Requests);
        Assert.Equal("multipart/form-data", req.ContentType);
        Assert.Contains("name=payload_json", req.Body);
        Assert.Contains("name=\"files[0]\"; filename=cat.png", req.Body);
        Assert.Contains("\"attachments\":[{\"id\":0,\"filename\":\"cat.png\"}]", req.Body);
    }

    [Fact]
    public async Task Reactions_escape_the_emoji_and_typing_posts()
    {
        await _api.AddReactionAsync(10, 9, new EmojiRef(null, "🐱"));
        await _api.RemoveOwnReactionAsync(10, 9, new EmojiRef(77, "blobcat"));
        await _api.TriggerTypingAsync(10);
        await _api.DeleteMessageAsync(10, 9);

        Assert.Equal([
            "PUT /api/v10/channels/10/messages/9/reactions/%F0%9F%90%B1/@me",
            "DELETE /api/v10/channels/10/messages/9/reactions/blobcat%3A77/@me",
            "POST /api/v10/channels/10/typing",
            "DELETE /api/v10/channels/10/messages/9",
        ], _network.Requests.Select(r => $"{r.Method} {r.Uri.AbsolutePath}"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"message":"401: Unauthorized","code":0}""", "rejected the token")]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"Missing Permissions","code":50013}""", "403 Forbidden: Missing Permissions")]
    [InlineData(HttpStatusCode.BadRequest, """{"captcha_key":["captcha-required"],"captcha_sitekey":"x"}""", "captcha")]
    [InlineData(HttpStatusCode.BadGateway, "<html>cloudflare</html>", "502 BadGateway.")]
    public async Task Errors_become_readable_exceptions(HttpStatusCode status, string body, string expected)
    {
        var api = new UserRestApi(Http(new FakeHttpHandler((_, _) => FakeHttpHandler.Json(body, status))));

        var ex = await Assert.ThrowsAsync<DiscordApiException>(() => api.GetCurrentUserAsync());

        Assert.Equal(status, ex.Status);
        Assert.Contains(expected, ex.Message);
        Assert.DoesNotContain(UserToken, ex.Message);
    }

    [Fact]
    public async Task Rate_limits_are_honored_by_the_shared_handler()
    {
        int calls = 0;
        var api = new UserRestApi(Http(new FakeHttpHandler((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                var limited = FakeHttpHandler.Json("""{"message":"You are being rate limited.","retry_after":0.05,"global":false}""", HttpStatusCode.TooManyRequests);
                limited.Headers.Add("X-RateLimit-Bucket", "abc");
                return limited;
            }
            return FakeHttpHandler.Json("""{"id":"1","username":"me"}""");
        })));

        var me = await api.GetCurrentUserAsync();

        Assert.Equal("me", me.Username);
        Assert.Equal(2, calls); // waited out Retry-After once, then succeeded
    }
}
