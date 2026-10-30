using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Disctalk.Core.Http;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;
using DisctalkClient.Core.Sending;

namespace DisctalkClient.Core.UserAccount;

/// <summary>Discord answered with an error. <see cref="Message"/> is Discord's own text, safe to show (never contains the token).</summary>
public sealed class DiscordApiException(HttpStatusCode status, int? code, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    /// <summary>Discord's JSON error code (e.g. 50013 Missing Permissions), if any.</summary>
    public int? Code { get; } = code;
}

/// <summary>
/// The REST calls the client makes as a user account. Every request goes through the archiver's pipeline
/// (<see cref="DiscordHttp.CreateClient"/>): header profile, X-Super-Properties, raw user token, and
/// <see cref="RateLimitHandler"/> (per-bucket limits, 429 Retry-After, 250 ms minimum spacing).
/// </summary>
public sealed class UserRestApi(HttpClient http) : IDiscordRestApi
{
    public const string ApiBase = DiscordHttp.ApiBase;

    /// <summary>GET /users/@me — the account the token belongs to. Throws <see cref="DiscordApiException"/> (401) for a bad token.</summary>
    public async Task<AuthorInfo> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"{ApiBase}/users/@me", null, cancellationToken).ConfigureAwait(false);
        return DiscordJson.ToAuthor(doc!.RootElement);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesBeforeAsync(ulong channelId, ulong? beforeMessageId, int limit, CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}/channels/{channelId}/messages?limit={Math.Clamp(limit, 1, 100)}" + (beforeMessageId is ulong b ? $"&before={b}" : "");
        using var doc = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        return doc!.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().Select(m => DiscordJson.ToMessage(m)).ToList()
            : [];
    }

    public async Task<ChatMessage> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["content"] = request.Content,
            // The official client sends a nonce (a snowflake for "now"); Discord echoes it back and uses it to dedupe retries.
            ["nonce"] = Snowflakes.FromTimestamp(DateTimeOffset.UtcNow).ToString(CultureInfo.InvariantCulture),
            ["tts"] = false,
            ["flags"] = 0,
            ["allowed_mentions"] = AllowedMentionsJson(request.AllowedMentions),
        };
        if (request.ReplyToMessageId is ulong replyId)
            body["message_reference"] = new JsonObject
            {
                ["channel_id"] = request.ChannelId.ToString(CultureInfo.InvariantCulture),
                ["message_id"] = replyId.ToString(CultureInfo.InvariantCulture),
                ["fail_if_not_exists"] = false,
            };

        HttpContent content;
        if (request.Files.Count == 0) content = Json(body);
        else
        {
            body["attachments"] = new JsonArray(request.Files.Select((f, i) => (JsonNode)new JsonObject { ["id"] = i, ["filename"] = f.FileName }).ToArray());
            var form = new MultipartFormDataContent { { Json(body), "payload_json" } };
            for (int i = 0; i < request.Files.Count; i++)
            {
                var part = new ReadOnlyMemoryContent(request.Files[i].Content);
                part.Headers.ContentType = new MediaTypeHeaderValue(request.Files[i].ContentType ?? "application/octet-stream");
                form.Add(part, $"files[{i}]", request.Files[i].FileName);
            }
            content = form;
        }

        using var doc = await SendAsync(HttpMethod.Post, $"{ApiBase}/channels/{request.ChannelId}/messages", content, cancellationToken).ConfigureAwait(false);
        return DiscordJson.ToMessage(doc!.RootElement);
    }

    public async Task<ChatMessage> EditMessageAsync(ulong channelId, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["content"] = content, ["allowed_mentions"] = AllowedMentionsJson(allowedMentions) };
        using var doc = await SendAsync(HttpMethod.Patch, $"{ApiBase}/channels/{channelId}/messages/{messageId}", Json(body), cancellationToken).ConfigureAwait(false);
        return DiscordJson.ToMessage(doc!.RootElement);
    }

    public async Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken = default) =>
        (await SendAsync(HttpMethod.Delete, $"{ApiBase}/channels/{channelId}/messages/{messageId}", null, cancellationToken).ConfigureAwait(false))?.Dispose();

    public async Task AddReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default) =>
        (await SendAsync(HttpMethod.Put, ReactionUrl(channelId, messageId, emoji), null, cancellationToken).ConfigureAwait(false))?.Dispose();

    public async Task RemoveOwnReactionAsync(ulong channelId, ulong messageId, EmojiRef emoji, CancellationToken cancellationToken = default) =>
        (await SendAsync(HttpMethod.Delete, ReactionUrl(channelId, messageId, emoji), null, cancellationToken).ConfigureAwait(false))?.Dispose();

    public async Task TriggerTypingAsync(ulong channelId, CancellationToken cancellationToken = default) =>
        (await SendAsync(HttpMethod.Post, $"{ApiBase}/channels/{channelId}/typing", null, cancellationToken).ConfigureAwait(false))?.Dispose();

    public async Task<IReadOnlyList<WebhookInfo>> GetChannelWebhooksAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"{ApiBase}/channels/{channelId}/webhooks", null, cancellationToken).ConfigureAwait(false);
        return doc!.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().Select(h => ToWebhook(h, channelId)).ToList() : [];
    }

    public async Task<WebhookInfo> CreateWebhookAsync(ulong channelId, string name, CancellationToken cancellationToken = default)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"{ApiBase}/channels/{channelId}/webhooks", Json(new JsonObject { ["name"] = name }), cancellationToken).ConfigureAwait(false);
        return ToWebhook(doc!.RootElement, channelId);
    }

    static WebhookInfo ToWebhook(JsonElement h, ulong channelId) =>
        new(h.Id("id") ?? 0, h.Id("channel_id") ?? channelId, h.Str("name") ?? "", h.Str("token"), h.Id("application_id"));

    internal static string ReactionUrl(ulong channelId, ulong messageId, EmojiRef emoji)
    {
        var key = emoji.Id is ulong id ? $"{emoji.Name}:{id}" : emoji.Name;
        return $"{ApiBase}/channels/{channelId}/messages/{messageId}/reactions/{Uri.EscapeDataString(key)}/@me?location=Message&type=0";
    }

    internal static JsonObject AllowedMentionsJson(AllowedMentions m)
    {
        var parse = new JsonArray();
        if (m.Everyone) parse.Add("everyone");
        if (m.AllRoles) parse.Add("roles");
        return new JsonObject
        {
            ["parse"] = parse,
            ["users"] = new JsonArray(m.Users.Select(u => (JsonNode)u.ToString(CultureInfo.InvariantCulture)).ToArray()),
            ["replied_user"] = m.RepliedUser,
        };
    }

    static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    /// <returns>The parsed body, or null for 204 No Content.</returns>
    async Task<JsonDocument?> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw ToException(response.StatusCode, text);
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }

    internal static DiscordApiException ToException(HttpStatusCode status, string body)
    {
        string? message = null;
        int? code = null;
        bool captcha = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.Str("message");
            code = doc.RootElement.Int("code");
            captcha = doc.RootElement.TryGetProperty("captcha_key", out _);
        }
        catch (JsonException) { }

        var text = (status, captcha) switch
        {
            (_, true) => "Discord asked for a captcha. Do this action once in the official Discord app, then try again here.",
            (HttpStatusCode.Unauthorized, _) => "Discord rejected the token (401). It was probably reset; sign in again with a fresh token.",
            (HttpStatusCode.TooManyRequests, _) => "Discord is rate limiting this account. Wait a bit and try again.",
            _ => $"Discord returned {(int)status} {status}" + (message is null ? "." : $": {message}"),
        };
        return new DiscordApiException(status, code, text);
    }
}
