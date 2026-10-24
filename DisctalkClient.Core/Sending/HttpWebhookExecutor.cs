using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Disctalk.Core.Http;
using DisctalkClient.Core.Abstractions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Sending;

/// <summary>
/// Executes webhooks over a plain HttpClient (documented endpoints: Execute / Edit / Delete Webhook Message).
/// The pipeline uses Disctalk.Core's <see cref="RateLimitHandler"/> for per-bucket rate limits and 429 retries.
/// </summary>
public sealed class HttpWebhookExecutor : IWebhookExecutor
{
    public const string ApiBase = "https://discord.com/api/v10";
    public const string UserAgent = "DiscordBot (https://github.com/adubry75/Disctalk, 1.0)";

    static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly HttpClient _http;

    public HttpWebhookExecutor(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    /// <summary>Default pipeline: RateLimitHandler (no extra spacing; Discord's buckets are enough for a bot) → sockets.</summary>
    public static HttpWebhookExecutor CreateDefault(HttpMessageHandler? innerHandler = null)
    {
        // No logger on purpose: RateLimitHandler logs the route on 429, and webhook routes contain the webhook token.
        var handler = new RateLimitHandler(new RateLimitOptions { MinimumInterval = TimeSpan.Zero })
        {
            InnerHandler = innerHandler ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
        };
        return new HttpWebhookExecutor(new HttpClient(handler));
    }

    public async Task<ulong> ExecuteAsync(WebhookInfo webhook, WebhookPayload payload, IReadOnlyList<OutgoingFile> files, CancellationToken cancellationToken = default)
    {
        var body = new ExecuteBody
        {
            Content = payload.Content,
            Username = payload.Username,
            AvatarUrl = payload.AvatarUrl,
            AllowedMentions = ToJson(payload.AllowedMentions),
            Attachments = files.Count == 0 ? null : files.Select((f, i) => new AttachmentBody(i, f.FileName)).ToList(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Url(webhook)}?wait=true")
        {
            Content = files.Count == 0 ? JsonContent.Create(body, options: Json) : Multipart(body, files),
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return ulong.Parse(doc.RootElement.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
    }

    public async Task EditAsync(WebhookInfo webhook, ulong messageId, string content, AllowedMentions allowedMentions, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Url(webhook)}/messages/{messageId}")
        {
            Content = JsonContent.Create(new EditBody { Content = content, AllowedMentions = ToJson(allowedMentions) }, options: Json),
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(WebhookInfo webhook, ulong messageId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Url(webhook)}/messages/{messageId}");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    static string Url(WebhookInfo webhook)
    {
        if (!webhook.CanExecute) throw new SendException("This webhook has no token; the bot can't post through it.");
        return $"{ApiBase}/webhooks/{webhook.Id}/{Uri.EscapeDataString(webhook.Token!)}";
    }

    static MultipartFormDataContent Multipart(ExecuteBody body, IReadOnlyList<OutgoingFile> files)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(JsonSerializer.Serialize(body, Json), System.Text.Encoding.UTF8, "application/json"), "payload_json" },
        };
        for (int i = 0; i < files.Count; i++)
        {
            var part = new ReadOnlyMemoryContent(files[i].Content);
            part.Headers.ContentType = new MediaTypeHeaderValue(files[i].ContentType ?? "application/octet-stream");
            form.Add(part, $"files[{i}]", files[i].FileName);
        }
        return form;
    }

    internal static AllowedMentionsBody ToJson(AllowedMentions m)
    {
        var parse = new List<string>();
        if (m.Everyone) parse.Add("everyone");
        if (m.AllRoles) parse.Add("roles");
        return new AllowedMentionsBody(parse, m.Users.Select(u => u.ToString(CultureInfo.InvariantCulture)).ToList(), m.RepliedUser);
    }

    // Error text from Discord is safe to surface; the request URL (which contains the token) is never included.
    static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = "";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (doc.RootElement.TryGetProperty("message", out var msg)) detail = ": " + msg.GetString();
        }
        catch (JsonException) { }
        throw new SendException($"Discord rejected the webhook request ({(int)response.StatusCode} {response.ReasonPhrase}){detail}");
    }

    internal sealed record ExecuteBody
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("username")] public string? Username { get; init; }
        [JsonPropertyName("avatar_url")] public string? AvatarUrl { get; init; }
        [JsonPropertyName("allowed_mentions")] public AllowedMentionsBody? AllowedMentions { get; init; }
        [JsonPropertyName("attachments")] public List<AttachmentBody>? Attachments { get; init; }
    }

    internal sealed record EditBody
    {
        [JsonPropertyName("content")] public string? Content { get; init; }
        [JsonPropertyName("allowed_mentions")] public AllowedMentionsBody? AllowedMentions { get; init; }
    }

    internal sealed record AttachmentBody([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("filename")] string FileName);

    internal sealed record AllowedMentionsBody(
        [property: JsonPropertyName("parse")] List<string> Parse,
        [property: JsonPropertyName("users")] List<string> Users,
        [property: JsonPropertyName("replied_user")] bool RepliedUser);
}
