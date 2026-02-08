using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Disctalk.Models
{
    public class Channel
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }

        [JsonProperty("guild_id")]
        public string GuildId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("last_message_id")]
        public string LastMessageId { get; set; }

        [JsonProperty("flags")]
        public int Flags { get; set; }

        [JsonProperty("last_pin_timestamp")]
        public DateTime? LastPinTimestamp { get; set; }

        [JsonProperty("parent_id")]
        public string ParentId { get; set; }

        [JsonProperty("rate_limit_per_user")]
        public int? RateLimitPerUser { get; set; }

        [JsonProperty("topic")]
        public string Topic { get; set; }

        [JsonProperty("default_thread_rate_limit_per_user")]
        public int? DefaultThreadRateLimitPerUser { get; set; }

        [JsonProperty("position")]
        public int Position { get; set; }

        [JsonProperty("permission_overwrites")]
        public List<PermissionOverwrite> PermissionOverwrites { get; set; }

        [JsonProperty("nsfw")]
        public bool? Nsfw { get; set; }

        [JsonProperty("icon_emoji")]
        public Emoji IconEmoji { get; set; }

        [JsonProperty("theme_color")]
        public int? ThemeColor { get; set; }

        [JsonProperty("bitrate")]
        public int? Bitrate { get; set; }

        [JsonProperty("user_limit")]
        public int? UserLimit { get; set; }

        [JsonProperty("rtc_region")]
        public string RtcRegion { get; set; }

        public string rawJson { get; set; }

    }
}
