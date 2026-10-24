using System.Net;
using Disctalk.Core.Http;
using Microsoft.Extensions.Time.Testing;

namespace Disctalk.Tests.Http;

public class BuildNumberProviderTests : IDisposable
{
    readonly string _cacheDir = Directory.CreateTempSubdirectory("disctalk-bn-").FullName;
    string CachePath => Path.Combine(_cacheDir, "build-number.json");
    static HeaderProfile Profile => HeaderProfile.Parse(Fixtures.Read("headerprofile.json"));

    public void Dispose() => Directory.Delete(_cacheDir, recursive: true);

    static FakeHttpHandler FakeDiscord() => new((req, _) => req.RequestUri!.AbsolutePath switch
    {
        "/app" => FakeHttpHandler.Text(Fixtures.Read("buildnumber/app.html")),
        "/assets/sentry.9f8e7d6c.js" => FakeHttpHandler.Text(Fixtures.Read("buildnumber/sentry.js"), "text/javascript"),
        "/assets/web.1a2b3c4d5e6f.js" => FakeHttpHandler.Text(Fixtures.Read("buildnumber/web.js"), "text/javascript"),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    });

    [Fact]
    public async Task Scans_app_assets_and_caches_the_result()
    {
        var http = FakeDiscord();
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
        var provider = new BuildNumberProvider(Profile, http, CachePath, time);

        Assert.Equal(651234, await provider.GetBuildNumberAsync());
        Assert.Equal("network", provider.LastSource);
        // Preferred bundles (web., sentry.) are fetched before the anonymous chunk, and scanning stops at the first hit.
        Assert.Equal(new[] { "/app", "/assets/sentry.9f8e7d6c.js" }, http.Requests.Select(r => r.Request.RequestUri!.AbsolutePath));
        Assert.Equal(Profile.UserAgent, http.Requests[0].Request.Headers.UserAgent.ToString());
        Assert.False(http.Requests[0].Request.Headers.Contains("Authorization"));

        // A second provider (next Disctalk run) reads the disk cache instead of hitting discord.com again.
        var http2 = FakeDiscord();
        var provider2 = new BuildNumberProvider(Profile, http2, CachePath, time);
        Assert.Equal(651234, await provider2.GetBuildNumberAsync());
        Assert.Equal("cache", provider2.LastSource);
        Assert.Empty(http2.Requests);
    }

    [Fact]
    public async Task Expired_cache_triggers_a_refetch()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
        await new BuildNumberProvider(Profile, FakeDiscord(), CachePath, time).GetBuildNumberAsync();

        time.Advance(TimeSpan.FromHours(13));
        var http = FakeDiscord();
        var provider = new BuildNumberProvider(Profile, http, CachePath, time);
        Assert.Equal(651234, await provider.GetBuildNumberAsync());
        Assert.Equal("network", provider.LastSource);
        Assert.NotEmpty(http.Requests);
    }

    [Fact]
    public async Task Network_failure_uses_the_profile_fallback()
    {
        var http = new FakeHttpHandler((Func<HttpRequestMessage, int, HttpResponseMessage>)((_, _) => throw new HttpRequestException("no network")));
        var provider = new BuildNumberProvider(Profile, http, CachePath);
        Assert.Equal(649389, await provider.GetBuildNumberAsync());
        Assert.Equal("fallback", provider.LastSource);
        Assert.False(File.Exists(CachePath), "the fallback must not be cached as if it were fresh");
    }

    [Fact]
    public async Task No_build_number_in_any_asset_uses_the_fallback()
    {
        var http = new FakeHttpHandler((req, _) => FakeHttpHandler.Text(req.RequestUri!.AbsolutePath == "/app"
            ? Fixtures.Read("buildnumber/app.html") : "console.log('nothing here')"));
        var provider = new BuildNumberProvider(Profile, http, CachePath) { MaxAssetsToScan = 2 };
        Assert.Equal(649389, await provider.GetBuildNumberAsync());
        Assert.Equal(3, http.Requests.Count); // app + MaxAssetsToScan bundles, no more
    }

    [Theory]
    [InlineData("buildNumber:\"651234\"", 651234)]
    [InlineData("build_number:651234,", 651234)]
    [InlineData("\"BUILD_NUMBER\":\"651234\"", 651234)]
    [InlineData("e.set(\"buildNumber\",\"651234\")", 651234)]
    [InlineData("var buildNumber = 651234;", 651234)]
    [InlineData("nothing", null)]
    public void FindBuildNumber_handles_bundle_variants(string text, int? expected) =>
        Assert.Equal(expected, BuildNumberProvider.FindBuildNumber(text));
}
