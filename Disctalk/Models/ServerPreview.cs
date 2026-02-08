using Newtonsoft.Json;

namespace Disctalk.Models
{
    // A slender version of the Server class. Contains member counts also, server class doesn't.
    public class ServerPreview
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("home_header")]
        public object HomeHeader { get; set; }

        [JsonProperty("splash")]
        public string Splash { get; set; }

        [JsonProperty("discovery_splash")]
        public string DiscoverySplash { get; set; }

        [JsonProperty("features")]
        public string[] Features { get; set; }

        [JsonProperty("approximate_member_count")]
        public int MemberCount { get; set; }

        [JsonProperty("approximate_presence_count")]
        public int PresenceCount { get; set; }

        [JsonProperty("emojis")]
        public Emoji[] Emojis { get; set; }

        [JsonProperty("stickers")]
        public Sticker[] Stickers { get; set; }

    }
}
