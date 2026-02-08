using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class PermissionOverwrite
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }

        [JsonProperty("allow")]
        public string Allow { get; set; }

        [JsonProperty("deny")]
        public string Deny { get; set; }

    }
}
