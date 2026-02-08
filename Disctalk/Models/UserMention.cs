using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class UserMention
    {
        public long id { get; set; }
        public string username { get; set; }
        public string avatar { get; set; }
        public string discriminator { get; set; }
        public long public_flags { get; set; }
        public long flags { get; set; }
        public string banner { get; set; }
        public string accent_color { get; set; }
        public string global_name { get; set; }

        [JsonProperty("avatar_decoration_data")]
        public AvatarDecorationData AvatarDecorationData { get; set; } // Changed from string to AvatarDecorationData
        public string banner_color { get; set; }
        public Clan clan { get; set; }
    }
}
