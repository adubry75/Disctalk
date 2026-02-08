using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class AvatarDecorationData
    {
        [JsonProperty("asset")]
        public string Asset { get; set; }

        [JsonProperty("sku_id")]
        public string SkuId { get; set; }
    }
}
