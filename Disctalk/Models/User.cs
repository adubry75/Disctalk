using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class User
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("username")]
        public string Username { get; set; }
        [JsonProperty("global_name")]
        public string GlobalName { get; set; }
        [JsonProperty("avatar")]
        public string Avatar { get; set; }
        [JsonProperty("avatar_decoration_data")]
        public AvatarDecorationData AvatarDecorationData { get; set; }

        [JsonProperty("discriminator")]
        public string Discriminator { get; set; }
        [JsonProperty("public_flags")]
        public int PublicFlags { get; set; }
        [JsonProperty("clan")]
        public object Clan { get; set; }
        [JsonProperty("flags")]
        public int Flags { get; set; }
        [JsonProperty("banner")]
        public object Banner { get; set; }
        [JsonProperty("banner_color")]
        public string BannerColor { get; set; }
        [JsonProperty("accent_color")]
        public int? AccentColor { get; set; }
        [JsonProperty("bio")]
        public string Bio { get; set; }
    }
}
