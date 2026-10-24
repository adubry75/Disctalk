using System.Net;
using Disctalk.Core.Http;
using Microsoft.Extensions.Time.Testing;

namespace Disctalk.Tests.Http;

public class RateLimitHandlerTests
{
    const string Messages = "https://discord.com/api/v10/channels/111/messages";

    static (HttpClient Client, RateLimitHandler Limiter, List<TimeSpan> Waits) Create(FakeHttpHandler fake, FakeTimeProvider time, RateLimitOptions? options = null)
    {
        var limiter = new RateLimitHandler(options ?? new RateLimitOptions { MinimumInterval = TimeSpan.Zero, SafetyMargin = TimeSpan.Zero }, time)
        {
            InnerHandler = fake,
        };
        var waits = new List<TimeSpan>();
        limiter.Waiting += (route, wait) => { lock (waits) waits.Add(wait); };
        // Fake time only moves when told to: advance it by exactly what the limiter asks to wait.
        limiter.DelayOverride = (wait, ct) => { time.Advance(wait); return Task.CompletedTask; };
        return (new HttpClient(limiter), limiter, waits);
    }

    static HttpResponseMessage Ok(int remaining, double resetAfter, string bucket = "abc")
    {
        var r = FakeHttpHandler.Json("[]");
        r.Headers.Add("X-RateLimit-Bucket", bucket);
        r.Headers.Add("X-RateLimit-Limit", "5");
        r.Headers.Add("X-RateLimit-Remaining", remaining.ToString());
        r.Headers.Add("X-RateLimit-Reset-After", resetAfter.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return r;
    }

    static HttpResponseMessage TooMany(double retryAfter, bool global = false, string? retryAfterHeader = null)
    {
        var r = FakeHttpHandler.Json($$"""{"message":"You are being rate limited.","retry_after":{{retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"global":{{(global ? "true" : "false")}}}""", HttpStatusCode.TooManyRequests);
        if (retryAfterHeader != null) r.Headers.Add("Retry-After", retryAfterHeader);
        if (global) r.Headers.Add("X-RateLimit-Global", "true");
        return r;
    }

    [Fact]
    public async Task Waits_for_bucket_reset_when_remaining_hits_zero()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, i) => i == 0 ? Ok(remaining: 0, resetAfter: 2.5) : Ok(4, 1));
        var (client, _, waits) = Create(fake, time);

        var start = time.GetUtcNow();
        (await client.GetAsync(Messages)).EnsureSuccessStatusCode();
        Assert.Empty(waits);
        (await client.GetAsync(Messages)).EnsureSuccessStatusCode();

        Assert.Equal(2, fake.Requests.Count);
        Assert.Contains(TimeSpan.FromSeconds(2.5), waits);
        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public async Task Retries_429_after_retry_after_from_body()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, i) => i == 0 ? TooMany(1.75, retryAfterHeader: "1") : Ok(4, 1));
        var (client, _, waits) = Create(fake, time);

        var response = await client.PostAsync(Messages, new StringContent("{\"content\":\"hi\"}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("{\"content\":\"hi\"}", fake.Requests[1].Body); // body survives the resend
        Assert.Contains(TimeSpan.FromSeconds(1.75), waits);
    }

    [Fact]
    public async Task Retry_After_header_wins_when_it_is_longer()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, i) => i == 0 ? TooMany(0.5, retryAfterHeader: "3") : Ok(4, 1));
        var (client, _, waits) = Create(fake, time);

        await client.GetAsync(Messages);
        Assert.Contains(TimeSpan.FromSeconds(3), waits);
    }

    [Fact]
    public async Task Global_429_pauses_every_route()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, i) => i == 0 ? TooMany(4, global: true) : Ok(4, 1));
        var (client, _, waits) = Create(fake, time);

        var start = time.GetUtcNow();
        await client.GetAsync(Messages);
        await client.GetAsync("https://discord.com/api/v10/guilds/222/channels");

        Assert.Equal(3, fake.Requests.Count);
        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_returns_the_429()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, _) => TooMany(1));
        var (client, _, _) = Create(fake, time, new RateLimitOptions { MaxRetries = 2, MinimumInterval = TimeSpan.Zero, SafetyMargin = TimeSpan.Zero });

        var response = await client.GetAsync(Messages);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(3, fake.Requests.Count);
        Assert.Contains("retry_after", await response.Content.ReadAsStringAsync()); // body still readable by the caller
    }

    [Fact]
    public async Task Does_not_wait_out_absurd_retry_after()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, _) => TooMany(3600));
        var (client, _, waits) = Create(fake, time);

        var response = await client.GetAsync(Messages);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(fake.Requests);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Spaces_consecutive_requests_by_the_minimum_interval()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, _) => Ok(10, 1));
        var (client, _, _) = Create(fake, time, new RateLimitOptions { MinimumInterval = TimeSpan.FromMilliseconds(250), SafetyMargin = TimeSpan.Zero });

        var start = time.GetUtcNow();
        for (int i = 0; i < 4; i++) await client.GetAsync($"https://discord.com/api/v10/channels/{i + 1}/messages");
        Assert.True(time.GetUtcNow() - start >= TimeSpan.FromMilliseconds(750));
    }

    [Fact]
    public async Task Buckets_are_separate_per_major_parameter()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((req, _) => Ok(remaining: 0, resetAfter: 10));
        var (client, _, waits) = Create(fake, time);

        await client.GetAsync("https://discord.com/api/v10/channels/1/messages");
        await client.GetAsync("https://discord.com/api/v10/channels/2/messages"); // different channel: not blocked
        Assert.DoesNotContain(TimeSpan.FromSeconds(10), waits);

        await client.GetAsync("https://discord.com/api/v10/channels/1/messages"); // same channel: must wait
        Assert.Contains(TimeSpan.FromSeconds(10), waits);
    }

    [Fact]
    public async Task Non_discord_hosts_are_not_rate_limited()
    {
        var time = new FakeTimeProvider();
        var fake = new FakeHttpHandler((_, _) => TooMany(5));
        var (client, _, waits) = Create(fake, time);
        var response = await client.GetAsync("https://cdn.discordapp.com/avatars/1/a.png");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(fake.Requests);
        Assert.Empty(waits);
    }

    [Theory]
    [InlineData("GET", "https://discord.com/api/v10/channels/111/messages?limit=50", "GET /channels/111/messages", "111")]
    [InlineData("PATCH", "https://discord.com/api/v10/channels/111/messages/999", "PATCH /channels/111/messages/{id}", "111")]
    [InlineData("PUT", "https://discord.com/api/v10/channels/111/messages/999/reactions/%F0%9F%90%B1/@me", "PUT /channels/111/messages/{id}/reactions/*", "111")]
    [InlineData("GET", "https://discord.com/api/v10/guilds/222/members/333", "GET /guilds/222/members/{id}", "222")]
    [InlineData("GET", "https://discord.com/api/v10/users/333/profile", "GET /users/{id}/profile", "")]
    public void Route_normalization(string method, string url, string route, string major)
    {
        var (r, m) = RateLimitHandler.GetRoute(new HttpMethod(method), new Uri(url));
        Assert.Equal(route, r);
        Assert.Equal(major, m);
    }
}
