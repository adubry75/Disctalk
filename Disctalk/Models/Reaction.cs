using System.Collections.Generic;

namespace Disctalk.Models
{
    public class Reaction
    {
        public Emoji Emoji { get; set; }
        public int Count { get; set; }
        public CountDetails CountDetails { get; set; }
        public List<object> BurstColors { get; set; } // Appears to be a list of objects (possibly empty)
        public bool MeBurst { get; set; }
        public bool BurstMe { get; set; }
        public bool Me { get; set; }
        public int BurstCount { get; set; }
    }
}
