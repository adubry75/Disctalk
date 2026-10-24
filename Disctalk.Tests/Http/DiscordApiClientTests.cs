using System.Text;
using System.Text.Json.Nodes;
using Disctalk;
using Disctalk.Core.Http;

namespace Disctalk.Tests.Http;

public class DiscordApiClientTests
{
    [Fact]
    public async Task Requests_use_v10_the_header_profile_and_the_token()
    {
        var profile = HeaderProfile.Parse(Fixtures.Read("headerprofile.json"));
        var noNetwork = new FakeHttpHandler((Func<HttpRequestMessage, int, HttpResponseMessage>)((_, _) => throw new HttpRequestException("offline")));
        var buildNumbers = new BuildNumberProvider(profile, noNetwork, cachePath: null);
        var discord = new FakeHttpHandler((_, _) => FakeHttpHandler.Json("""[{"id":"5","type":0,"name":"general","position":0}]"""));

        var client = new DiscordApiClient("user-token-123", profile, discord, buildNumbers);
        var channels = await client.GetChannelsAsync(42);

        Assert.Equal("general", Assert.Single(channels).Name);
        var request = Assert.Single(discord.Requests).Request;
        Assert.Equal("https://discord.com/api/v10/guilds/42/channels", request.RequestUri!.ToString());

        string Header(string name) => request.Headers.NonValidated[name].ToString(); // raw, as sent on the wire
        Assert.Equal("user-token-123", Header("Authorization"));
        Assert.Equal(profile.UserAgent, Header("User-Agent"));
        Assert.Equal("en-US", Header("X-Discord-Locale"));
        Assert.Equal("America/Los_Angeles", Header("X-Discord-Timezone"));
        Assert.Equal("\"Windows\"", Header("Sec-Ch-Ua-Platform"));
        Assert.DoesNotContain(request.Headers, h => h.Key.EndsWith(':'));

        var props = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Header("X-Super-Properties"))))!;
        Assert.Equal(649389, props["client_build_number"]!.GetValue<int>()); // offline → fallback
        Assert.Equal(profile.UserAgent, props["browser_user_agent"]!.GetValue<string>());
    }
}
