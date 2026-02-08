using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class Role
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("permissions")]
        public string Permissions { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        [JsonProperty("color")]
        public int Color { get; set; }

        [JsonProperty("hoist")]
        public bool Hoist { get; set; }

        [JsonProperty("managed")]
        public bool Managed { get; set; }

        [JsonProperty("mentionable")]
        public bool Mentionable { get; set; }

        [JsonProperty("icon")]
        public object Icon { get; set; }

        [JsonProperty("unicode_emoji")]
        public object UnicodeEmoji { get; set; }

        [JsonProperty("flags")]
        public int Flags { get; set; }
    }
}
