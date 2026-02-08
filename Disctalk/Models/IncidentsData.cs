using Newtonsoft.Json;

namespace Disctalk.Models
{
    public class IncidentsData
    {
        [JsonProperty("invites_disabled_until")]
        public string InvitesDisabledUntil { get; set; }

        [JsonProperty("dms_disabled_until")]
        public object DmsDisabledUntil { get; set; }
    }
}
