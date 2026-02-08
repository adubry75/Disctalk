using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Disctalk.Models
{
    // UserSERVERInfo
    public class UserServerInfo
    {
        public long serverId { get; set; }
        [JsonProperty("avatar")]
        public string Avatar { get; set; }
        [JsonProperty("communication_disabled_until")]
        public DateTime? CommunicationDisabledUntil { get; set; }
        [JsonProperty("flags")]
        public int Flags { get; set; }
        [JsonProperty("joined_at")]
        public DateTime JoinedAt { get; set; }
        [JsonProperty("nick")]
        public string Nick { get; set; }
        [JsonProperty("pending")]
        public bool Pending { get; set; }
        [JsonProperty("premium_since")]
        public DateTime? PremiumSince { get; set; }
        [JsonProperty("roles")]
        public List<string> Roles { get; set; }
        [JsonProperty("unusual_dm_activity_until")]
        public string UnusualDmActivityUntil { get; set; }
        [JsonProperty("user")]
        public User User { get; set; }
        [JsonProperty("mute")]
        public bool Mute { get; set; }
        [JsonProperty("deaf")]
        public bool Deaf { get; set; }

        public UserServerInfo()
        {
            Roles = new List<string>();
        }

        public string rawJson { get; set; }
    }
}
