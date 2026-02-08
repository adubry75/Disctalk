using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class Clan
    {
        [JsonProperty("identity_guild_id")]
        public string IdentityGuildId { get; set; }

        [JsonProperty("identity_enabled")]
        public bool? IdentityEnabled { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("badge")]
        public string Badge { get; set; }
    }
}
