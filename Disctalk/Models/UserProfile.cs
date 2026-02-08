using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class UserProfile
    {
        [JsonProperty("bio")]
        public string Bio { get; set; }

        [JsonProperty("accent_color")]
        public int? AccentColor { get; set; }

        [JsonProperty("pronouns")]
        public string Pronouns { get; set; }
    }
}
