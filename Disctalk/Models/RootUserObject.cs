using Newtonsoft.Json;
using System.Collections.Generic;

namespace Disctalk.Models
{
    public class RootUserObject
    {
        [JsonProperty("user")]
        public User User { get; set; }

        [JsonProperty("connected_accounts")]
        public List<object> ConnectedAccounts { get; set; }

        [JsonProperty("premium_since")]
        public object PremiumSince { get; set; }

        [JsonProperty("premium_type")]
        public object PremiumType { get; set; }

        [JsonProperty("premium_guild_since")]
        public object PremiumGuildSince { get; set; }

        [JsonProperty("profile_themes_experiment_bucket")]
        public int ProfileThemesExperimentBucket { get; set; }

        [JsonProperty("user_profile")]
        public UserProfile UserProfile { get; set; }

        [JsonProperty("badges")]
        public List<Badge> Badges { get; set; }

        [JsonProperty("guild_badges")]
        public List<object> GuildBadges { get; set; }

        [JsonProperty("mutual_guilds")]
        public List<MutualGuild> MutualGuilds { get; set; }

        [JsonProperty("legacy_username")]
        public string LegacyUsername { get; set; }

        public string rawJson { get; set; }
    }
}
