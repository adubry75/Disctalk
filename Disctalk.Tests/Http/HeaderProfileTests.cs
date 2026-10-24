using System.Text;
using System.Text.Json.Nodes;
using Disctalk.Core.Http;

namespace Disctalk.Tests.Http;

public class HeaderProfileTests
{
    static HeaderProfile Captured() => HeaderProfile.Parse(Fixtures.Read("headerprofile.json"), "fixture");

    [Fact]
    public void Captured_profile_is_internally_consistent()
    {
        var problems = HeaderProfileValidator.Validate(Captured());
        Assert.Empty(problems);
    }

    [Fact]
    public void Embedded_profile_matches_repo_file_and_is_consistent()
    {
        var embedded = HeaderProfile.LoadEmbedded();
        Assert.Empty(HeaderProfileValidator.Validate(embedded));
        Assert.Equal(Captured().UserAgent, embedded.UserAgent);
    }

    [Fact]
    public void Parse_drops_comments_and_never_keeps_authorization()
    {
        var p = HeaderProfile.Parse("""
            { "headers": { "_comment": "x", "User-Agent": "UA", "Authorization": "secret" },
              "superProperties": { "_comment": "y", "os": "Windows" }, "buildNumber": { "fallback": 5 } }
            """);
        Assert.Equal(new[] { "User-Agent" }, p.Headers.Select(h => h.Key));
        Assert.Null(p.GetHeader("Authorization"));
        Assert.False(p.BuildSuperProperties(1).ContainsKey("_comment"));
        Assert.Equal(5, p.FallbackBuildNumber);
    }

    [Fact]
    public void Super_properties_keep_capture_key_order_and_fill_runtime_values()
    {
        var profile = Captured();
        var props = profile.BuildSuperProperties(123456);

        var expectedOrder = ((JsonObject)JsonNode.Parse(Fixtures.Read("headerprofile.json"))!["superProperties"]!)
            .Select(p => p.Key).Where(k => !k.StartsWith('_'));
        Assert.Equal(expectedOrder, props.Select(p => p.Key));

        Assert.Equal(123456, props["client_build_number"]!.GetValue<int>());
        var launchId = props["client_launch_id"]!.GetValue<string>();
        var signature = props["launch_signature"]!.GetValue<string>();
        Assert.True(Guid.TryParse(launchId, out var g) && g.Version == 4);
        Assert.True(Guid.TryParse(signature, out _));
        Assert.NotEqual(launchId, signature);

        // Stable for the life of the process, so REST and gateway agree.
        Assert.Equal(launchId, profile.BuildSuperProperties(1)["client_launch_id"]!.GetValue<string>());
    }

    [Fact]
    public void X_Super_Properties_is_base64_of_compact_json()
    {
        var props = Captured().BuildSuperProperties(649389);
        var header = HeaderProfile.EncodeSuperProperties(props);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));

        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain(": ", json);
        Assert.StartsWith("{\"os\":\"Windows\",\"browser\":\"Chrome\",", json);
        Assert.Contains("\"client_event_source\":null", json);
        Assert.Contains("\"has_client_mods\":false", json);
        Assert.True(JsonNode.DeepEquals(props, JsonNode.Parse(json)));
    }

    [Theory]
    [InlineData("Sec-Ch-Ua", "\"Google Chrome\";v=\"120\", \"Chromium\";v=\"120\", \"Not(A:Brand\";v=\"24\"", "Chromium version")]
    [InlineData("Sec-Ch-Ua-Platform", "\"macOS\"", "Sec-Ch-Ua-Platform")]
    [InlineData("Sec-Ch-Ua-Mobile", "?1", "Sec-Ch-Ua-Mobile")]
    [InlineData("X-Discord-Locale", "en-GB", "X-Discord-Locale")]
    [InlineData("Accept-Language", "de-DE,de;q=0.9", "Accept-Language")]
    [InlineData("X-Discord-Timezone", "Mars/Olympus_Mons", "time zone")]
    public void Validator_catches_mismatched_header(string header, string value, string expectedProblem)
    {
        var root = JsonNode.Parse(Fixtures.Read("headerprofile.json"))!.AsObject();
        root["headers"]![header] = value;
        var problems = HeaderProfileValidator.Validate(HeaderProfile.Parse(root.ToJsonString()));
        Assert.Contains(problems, p => p.Contains(expectedProblem));
    }

    [Theory]
    [InlineData("browser_version", "120.0.0.0", "browser_version")]
    [InlineData("browser", "Firefox", "superProperties.browser")]
    [InlineData("os", "Linux", "superProperties.os")]
    [InlineData("system_locale", "fr-FR", "system_locale")]
    [InlineData("browser_user_agent", "Mozilla/5.0 something else", "browser_user_agent")]
    public void Validator_catches_mismatched_super_property(string key, string value, string expectedProblem)
    {
        var root = JsonNode.Parse(Fixtures.Read("headerprofile.json"))!.AsObject();
        root["superProperties"]![key] = value;
        var problems = HeaderProfileValidator.Validate(HeaderProfile.Parse(root.ToJsonString()));
        Assert.Contains(problems, p => p.Contains(expectedProblem));
    }

    [Fact]
    public void Validator_rejects_the_old_malformed_locale_header_name()
    {
        var root = JsonNode.Parse(Fixtures.Read("headerprofile.json"))!.AsObject();
        root["headers"]!["X-Discord-Locale:"] = "en-US";
        var problems = HeaderProfileValidator.Validate(HeaderProfile.Parse(root.ToJsonString()));
        Assert.Contains(problems, p => p.Contains("'X-Discord-Locale:' is malformed"));
    }

    [Fact]
    public void Validator_accepts_a_consistent_desktop_client_profile()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9042 Chrome/120.0.6099.291 Electron/28.2.10 Safari/537.36";
        var profile = HeaderProfile.Parse($$"""
            { "headers": { "User-Agent": "{{ua}}", "Accept-Language": "en-US", "Sec-Ch-Ua": "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\"",
                           "Sec-Ch-Ua-Mobile": "?0", "Sec-Ch-Ua-Platform": "\"Windows\"", "X-Discord-Locale": "en-US", "X-Discord-Timezone": "America/Los_Angeles" },
              "superProperties": { "os": "Windows", "browser": "Discord Client", "release_channel": "stable", "client_version": "1.0.9042",
                                   "os_arch": "x64", "app_arch": "ia32", "system_locale": "en-US", "browser_user_agent": "{{ua}}", "browser_version": "28.2.10",
                                   "client_build_number": 280000 } }
            """);
        Assert.Empty(HeaderProfileValidator.Validate(profile));
    }
}
