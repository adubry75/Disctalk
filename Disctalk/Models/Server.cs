using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class Server
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("icon")]
        public string Icon { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("home_header")]
        public object HomeHeader { get; set; }

        [JsonProperty("splash")]
        public string Splash { get; set; }

        [JsonProperty("discovery_splash")]
        public string DiscoverySplash { get; set; }

        [JsonProperty("features")]
        public string[] Features { get; set; }

        [JsonProperty("banner")]
        public object Banner { get; set; }

        [JsonProperty("owner_id")]
        public string OwnerId { get; set; }

        [JsonProperty("application_id")]
        public object ApplicationId { get; set; }

        [JsonProperty("region")]
        public string Region { get; set; }

        [JsonProperty("afk_channel_id")]
        public object AfkChannelId { get; set; }

        [JsonProperty("afk_timeout")]
        public int AfkTimeout { get; set; }

        [JsonProperty("system_channel_id")]
        public string SystemChannelId { get; set; }

        [JsonProperty("system_channel_flags")]
        public int SystemChannelFlags { get; set; }

        [JsonProperty("widget_enabled")]
        public bool WidgetEnabled { get; set; }

        [JsonProperty("widget_channel_id")]
        public object WidgetChannelId { get; set; }

        [JsonProperty("verification_level")]
        public int VerificationLevel { get; set; }

        [JsonProperty("roles")]
        public Role[] Roles { get; set; }

        [JsonProperty("default_message_notifications")]
        public int DefaultMessageNotifications { get; set; }

        [JsonProperty("mfa_level")]
        public int MfaLevel { get; set; }

        [JsonProperty("explicit_content_filter")]
        public int ExplicitContentFilter { get; set; }

        [JsonProperty("max_presences")]
        public object MaxPresences { get; set; }

        [JsonProperty("max_members")]
        public int MaxMembers { get; set; }

        [JsonProperty("max_stage_video_channel_users")]
        public int MaxStageVideoChannelUsers { get; set; }

        [JsonProperty("max_video_channel_users")]
        public int MaxVideoChannelUsers { get; set; }

        [JsonProperty("vanity_url_code")]
        public string VanityUrlCode { get; set; }

        [JsonProperty("premium_tier")]
        public int PremiumTier { get; set; }

        [JsonProperty("premium_subscription_count")]
        public int PremiumSubscriptionCount { get; set; }

        [JsonProperty("preferred_locale")]
        public string PreferredLocale { get; set; }

        [JsonProperty("rules_channel_id")]
        public string RulesChannelId { get; set; }

        [JsonProperty("safety_alerts_channel_id")]
        public string SafetyAlertsChannelId { get; set; }

        [JsonProperty("public_updates_channel_id")]
        public string PublicUpdatesChannelId { get; set; }

        [JsonProperty("hub_type")]
        public object HubType { get; set; }

        [JsonProperty("premium_progress_bar_enabled")]
        public bool PremiumProgressBarEnabled { get; set; }

        [JsonProperty("latest_onboarding_question_id")]
        public string LatestOnboardingQuestionId { get; set; }

        [JsonProperty("nsfw")]
        public bool Nsfw { get; set; }

        [JsonProperty("nsfw_level")]
        public int NsfwLevel { get; set; }

        [JsonProperty("emojis")]
        public Emoji[] Emojis { get; set; }

        [JsonProperty("stickers")]
        public Sticker[] Stickers { get; set; }

        [JsonProperty("incidents_data")]
        public IncidentsData IncidentsData { get; set; }

        [JsonProperty("inventory_settings")]
        public object InventorySettings { get; set; }

        [JsonProperty("embed_enabled")]
        public bool EmbedEnabled { get; set; }

        [JsonProperty("embed_channel_id")]
        public object EmbedChannelId { get; set; }


        public string rawJson { get; set; }

    }
}
