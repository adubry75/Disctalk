using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Disctalk.Models
{
    public class Message
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public int Type { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("channel_id")]
        public string ChannelId { get; set; }

        [JsonProperty("author")]
        public Author Author { get; set; }

        [JsonProperty("attachments")]
        public List<Attachment> Attachments { get; set; }

        [JsonProperty("embeds")]
        public List<Embed> Embeds { get; set; }

        [JsonProperty("mentions")]
        public List<UserMention> Mentions { get; set; }

        [JsonProperty("mention_roles")]
        public List<string> MentionRoles { get; set; }

        [JsonProperty("pinned")]
        public bool Pinned { get; set; }

        [JsonProperty("mention_everyone")]
        public bool MentionEveryone { get; set; }

        [JsonProperty("tts")]
        public bool Tts { get; set; }

        [JsonProperty("message_reference")]
        public MsgReference messageReference { get; set; }

        [JsonProperty("referenced_message")]
        public Message referencedMessage { get; set; }

        [JsonProperty("sticker_items")]
        public List<MsgStickers> Stickers { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("edited_timestamp")]
        public DateTime? EditedTimestamp { get; set; }

        [JsonProperty("flags")]
        public int Flags { get; set; }

        [JsonProperty("components")]
        public List<Component> Components { get; set; }

        [JsonProperty("reactions")]
        public List<Reaction> Reactions { get; set; }



        public string json { get; set; }

    }
}
