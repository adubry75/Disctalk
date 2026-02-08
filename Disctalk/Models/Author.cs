using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class Author
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("avatar")]
        public string Avatar { get; set; }

        [JsonProperty("discriminator")]
        public string Discriminator { get; set; }

        [JsonProperty("public_flags")]
        public int PublicFlags { get; set; }

        [JsonProperty("flags")]
        public int Flags { get; set; }

        [JsonProperty("banner")]
        public string Banner { get; set; }

        [JsonProperty("accent_color")]
        public string AccentColor { get; set; }

        [JsonProperty("global_name")]
        public string GlobalName { get; set; }

        [JsonProperty("avatar_decoration_data")]
        public AvatarDecorationData AvatarDecorationData { get; set; } // Changed from string to AvatarDecorationData

        [JsonProperty("banner_color")]
        public string BannerColor { get; set; }

        [JsonProperty("clan")]
        public Clan Clan { get; set; }

    }
}
