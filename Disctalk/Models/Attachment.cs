namespace Disctalk.Models
{
    public class Attachment
    {
        public string id { get; set; }
        public string filename { get; set; }
        public long size { get; set; }
        public string url { get; set; }
        public string proxy_url { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public string content_type { get; set; }
        public int content_scan_version { get; set; }
        public string placeholder { get; set; }
        public int placeholder_version { get; set; }
    }
}
