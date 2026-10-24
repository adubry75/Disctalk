using System.Net;
using System.Text;
using System.Text.Json;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;

namespace DisctalkClient.Tests.Sending;

/// <summary>Wire-level checks of the webhook HTTP calls against a fake Discord (no network).</summary>
public class HttpWebhookExecutorTests
{
    const string Secret = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_-abcd";
    static readonly WebhookInfo Hook = new(900000000000000700, TestData.GeneralId, "Disctalk Client", Secret, TestData.BotId);

    sealed class FakeDiscord : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, Uri Uri, string? ContentType, string Body)> Requests = new();
        public Queue<HttpResponseMessage> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, request.Content?.Headers.ContentType?.MediaType, body));
            return Responses.Count > 0 ? Responses.Dequeue() : Json(HttpStatusCode.OK, """{"id":"900000000000000999"}""");
        }

        public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
            new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    readonly FakeDiscord _discord = new();
    readonly HttpWebhookExecutor _executor;

    public HttpWebhookExecutorTests() => _executor = HttpWebhookExecutor.CreateDefault(_discord);

    [Fact]
    public async Task Execute_posts_json_with_persona_and_allowed_mentions()
    {
        var id = await _executor.ExecuteAsync(Hook, new WebhookPayload
        {
            Content = "hi <@900000000000000101>",
            Username = "Andre",
            AvatarUrl = "https://example.com/a.png",
            AllowedMentions = new AllowedMentions { Users = [900000000000000101], AllRoles = true },
        }, []);

        Assert.Equal(900000000000000999UL, id);
        var (method, uri, contentType, body) = Assert.Single(_discord.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal($"https://discord.com/api/v10/webhooks/{Hook.Id}/{Secret}?wait=true", uri.ToString());
        Assert.Equal("application/json", contentType);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("hi <@900000000000000101>", root.GetProperty("content").GetString());
        Assert.Equal("Andre", root.GetProperty("username").GetString());
        Assert.Equal("https://example.com/a.png", root.GetProperty("avatar_url").GetString());
        var mentions = root.GetProperty("allowed_mentions");
        Assert.Equal(["roles"], mentions.GetProperty("parse").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["900000000000000101"], mentions.GetProperty("users").EnumerateArray().Select(x => x.GetString()));
        Assert.False(mentions.GetProperty("replied_user").GetBoolean());
        Assert.False(root.TryGetProperty("attachments", out _));
    }

    [Fact]
    public async Task Execute_omits_persona_fields_when_not_set()
    {
        await _executor.ExecuteAsync(Hook, new WebhookPayload { Content = "x" }, []);
        using var doc = JsonDocument.Parse(_discord.Requests[0].Body);
        Assert.False(doc.RootElement.TryGetProperty("username", out _));
        Assert.False(doc.RootElement.TryGetProperty("avatar_url", out _));
        Assert.Empty(doc.RootElement.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }

    [Fact]
    public async Task Execute_with_files_sends_multipart_payload_json_and_files()
    {
        await _executor.ExecuteAsync(Hook, new WebhookPayload { Content = "pics" },
            [new OutgoingFile("cat.png", Encoding.ASCII.GetBytes("PNGDATA"), "image/png"), new OutgoingFile("notes.txt", Encoding.ASCII.GetBytes("meow"))]);

        var request = Assert.Single(_discord.Requests);
        Assert.Equal("multipart/form-data", request.ContentType);
        Assert.Contains("name=payload_json", request.Body);
        Assert.Contains("\"attachments\":[{\"id\":0,\"filename\":\"cat.png\"},{\"id\":1,\"filename\":\"notes.txt\"}]", request.Body);
        Assert.Contains("name=\"files[0]\"; filename=cat.png", request.Body);
        Assert.Contains("PNGDATA", request.Body);
        Assert.Contains("name=\"files[1]\"; filename=notes.txt", request.Body);
    }

    [Fact]
    public async Task Edit_and_delete_hit_the_webhook_message_routes()
    {
        _discord.Responses.Enqueue(FakeDiscord.Json(HttpStatusCode.OK, "{}"));
        _discord.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.NoContent));

        await _executor.EditAsync(Hook, 42, "fixed typo", AllowedMentions.None);
        await _executor.DeleteAsync(Hook, 42);

        Assert.Equal(HttpMethod.Patch, _discord.Requests[0].Method);
        Assert.Equal($"https://discord.com/api/v10/webhooks/{Hook.Id}/{Secret}/messages/42", _discord.Requests[0].Uri.ToString());
        Assert.Contains("\"content\":\"fixed typo\"", _discord.Requests[0].Body);
        Assert.Equal(HttpMethod.Delete, _discord.Requests[1].Method);
        Assert.EndsWith("/messages/42", _discord.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Discord_errors_surface_without_leaking_the_webhook_token()
    {
        _discord.Responses.Enqueue(FakeDiscord.Json(HttpStatusCode.NotFound, """{"message":"Unknown Webhook","code":10015}"""));

        var ex = await Assert.ThrowsAsync<SendException>(() => _executor.ExecuteAsync(Hook, new WebhookPayload { Content = "x" }, []));

        Assert.Contains("Unknown Webhook", ex.Message);
        Assert.Contains("404", ex.Message);
        Assert.DoesNotContain(Secret, ex.ToString());
    }

    [Fact]
    public async Task Rate_limits_are_retried_by_the_reused_RateLimitHandler()
    {
        var limited = FakeDiscord.Json(HttpStatusCode.TooManyRequests, """{"message":"You are being rate limited.","retry_after":0.01,"global":false}""");
        limited.Headers.Add("X-RateLimit-Bucket", "abc");
        _discord.Responses.Enqueue(limited);

        var id = await _executor.ExecuteAsync(Hook, new WebhookPayload { Content = "x" }, []);

        Assert.Equal(900000000000000999UL, id);
        Assert.Equal(2, _discord.Requests.Count);
        Assert.Equal(_discord.Requests[0].Body, _discord.Requests[1].Body);
    }

    [Fact]
    public async Task Webhook_without_token_cannot_execute()
    {
        await Assert.ThrowsAsync<SendException>(() => _executor.ExecuteAsync(Hook with { Token = null }, new WebhookPayload { Content = "x" }, []));
        Assert.Empty(_discord.Requests);
    }
}
