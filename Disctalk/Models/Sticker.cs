using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class Sticker
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("tags")]
        public string Tags { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }

        [JsonProperty("format_type")]
        public int FormatType { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("asset")]
        public string Asset { get; set; }

        [JsonProperty("available")]
        public bool Available { get; set; }

        [JsonProperty("guild_id")]
        public string GuildId { get; set; }
    }
}
