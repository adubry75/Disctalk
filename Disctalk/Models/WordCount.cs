using System;

namespace Disctalk.Models
{
    public class WordCount
    {
        public string word { get; set; }
        public long messageId { get; set; }
        public long channelId { get; set; }
        public long authorId { get; set; }
        public string authorUsername { get; set; }
        public DateTime timeStamp { get; set; }
        public long serverId { get; set; }

    }
}
