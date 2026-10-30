using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Disctalk.Core.Http;
using DisctalkClient.Core.UserAccount;
using static DisctalkClient.Tests.UserAccount.UserTestKit;

namespace DisctalkClient.Tests.UserAccount;

public class GatewayProtocolTests
{
    [Fact]
    public void Identify_uses_the_archiver_profile_super_properties_and_keeps_your_status()
    {
        var identify = GatewayProtocol.Identify(UserToken, Profile, 123456);
        var d = identify["d"]!;

        Assert.Equal(2, (int)identify["op"]!);
        Assert.Equal(UserToken, (string)d["token"]!);
        Assert.Equal(GatewayProtocol.DefaultCapabilities, (int)d["capabilities"]!);
        Assert.Equal("unknown", (string)d["presence"]!["status"]!);
        var props = d["properties"]!.AsObject();
        Assert.Equal(123456, (int)props["client_build_number"]!);
        Assert.Equal(Profile.UserAgent, (string)props["browser_user_agent"]!);
        Assert.DoesNotContain("<runtime", props.ToJsonString());
        // Same per-launch values as the X-Super-Properties header the REST calls send.
        Assert.Equal(HeaderProfile.EncodeSuperProperties(Profile.BuildSuperProperties(123456)), HeaderProfile.EncodeSuperProperties(props));
    }

    [Fact]
    public void Profile_can_override_capabilities()
    {
        var profile = HeaderProfile.Parse("""{"headers":{"User-Agent":"x"},"superProperties":{"os":"Windows"},"gateway":{"capabilities":30717}}""");
        Assert.Equal(30717, (int)GatewayProtocol.Identify("t", profile, 1)["d"]!["capabilities"]!);
    }

    [Fact]
    public void Resume_heartbeat_and_subscribe_payloads()
    {
        Assert.Equal("""{"op":6,"d":{"token":"t","session_id":"s","seq":42}}""", GatewayProtocol.Resume("t", "s", 42).ToJsonString());
        Assert.Equal("""{"op":1,"d":7}""", GatewayProtocol.Heartbeat(7).ToJsonString());
        Assert.Equal("""{"op":1,"d":null}""", GatewayProtocol.Heartbeat(null).ToJsonString());

        var sub = GatewayProtocol.SubscribeGuild(1, 10);
        Assert.Equal(37, (int)sub["op"]!);
        var g = sub["d"]!["subscriptions"]!["1"]!;
        Assert.True((bool)g["typing"]!);
        Assert.False((bool)g["activities"]!);
        Assert.Equal("[[0,99]]", g["channels"]!["10"]!.ToJsonString());
    }

    [Fact]
    public void Gateway_url_matches_the_web_client_and_prefers_the_resume_url()
    {
        Assert.Equal("wss://gateway.discord.gg/?encoding=json&v=9&compress=zlib-stream", GatewayProtocol.GatewayUri(null).ToString());
        Assert.Equal("wss://gateway-us-east1-b.discord.gg/?encoding=json&v=9&compress=zlib-stream", GatewayProtocol.GatewayUri("wss://gateway-us-east1-b.discord.gg/").ToString());
    }

    [Theory]
    [InlineData(4004, ReconnectAction.Stop)]
    [InlineData(4013, ReconnectAction.Stop)]
    [InlineData(4014, ReconnectAction.Stop)]
    [InlineData(4007, ReconnectAction.Identify)]
    [InlineData(4009, ReconnectAction.Identify)]
    [InlineData(4000, ReconnectAction.Resume)]
    [InlineData(1006, ReconnectAction.Resume)]
    [InlineData(null, ReconnectAction.Resume)]
    public void Close_codes_decide_how_to_reconnect(int? code, ReconnectAction action) => Assert.Equal(action, GatewayProtocol.OnClose(code));

    [Fact]
    public void Backoff_grows_exponentially_with_jitter_and_caps_at_a_minute()
    {
        Assert.Equal(TimeSpan.FromSeconds(0.5), GatewayProtocol.Backoff(0, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(1), GatewayProtocol.Backoff(1, 0));
        Assert.Equal(TimeSpan.FromSeconds(8.25), GatewayProtocol.Backoff(4, 0.25));
        Assert.Equal(TimeSpan.FromSeconds(60), GatewayProtocol.Backoff(50, 0.99));
        Assert.InRange(GatewayProtocol.InvalidSessionDelay(0).TotalSeconds, 1, 5);
        Assert.InRange(GatewayProtocol.InvalidSessionDelay(0.999).TotalSeconds, 1, 5);
    }
}

public class ZlibStreamDecoderTests
{
    /// <summary>Compresses payloads like Discord: one zlib context, Z_SYNC_FLUSH after each.</summary>
    static List<byte[]> Compress(params string[] payloads)
    {
        var output = new MemoryStream();
        using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
        var frames = new List<byte[]>();
        foreach (var p in payloads)
        {
            long start = output.Length;
            zlib.Write(Encoding.UTF8.GetBytes(p));
            zlib.Flush();
            frames.Add(output.ToArray()[(int)start..]);
        }
        return frames;
    }

    [Fact]
    public void Decodes_consecutive_payloads_sharing_one_context()
    {
        var big = "{\"op\":0,\"d\":\"" + new string('x', 200_000) + "\"}";
        var frames = Compress("""{"op":10}""", big, """{"op":11}""");
        using var decoder = new ZlibStreamDecoder();

        Assert.Equal("""{"op":10}""", Encoding.UTF8.GetString(decoder.Push(frames[0])!));
        Assert.Equal(big, Encoding.UTF8.GetString(decoder.Push(frames[1])!));
        Assert.Equal("""{"op":11}""", Encoding.UTF8.GetString(decoder.Push(frames[2])!));
    }

    [Fact]
    public void Waits_for_the_flush_marker_when_a_payload_spans_several_frames()
    {
        var frame = Compress("""{"op":0,"t":"READY"}""")[0];
        using var decoder = new ZlibStreamDecoder();

        Assert.Null(decoder.Push(frame.AsSpan(0, 5)));
        Assert.Null(decoder.Push(frame.AsSpan(5, frame.Length - 7)));
        Assert.Equal("""{"op":0,"t":"READY"}""", Encoding.UTF8.GetString(decoder.Push(frame.AsSpan(frame.Length - 2))!));
    }
}
