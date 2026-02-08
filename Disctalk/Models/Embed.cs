namespace Disctalk.Models
{
    public class Embed
    {
        public string type { get; set; }
        public string url { get; set; }
        public EmbedProvider provider { get; set; }
        public EmbedThumbnail thumbnail { get; set; }
        public EmbedVideo video { get; set; }
        public int content_scan_version { get; set; }
    }
}
