using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class EstimateSmartFeeResponse
    {
        [JsonProperty("feerate")] public decimal? FeeRate { get; set; }
        [JsonProperty("errors")] public string[] Errors { get; set; }
        [JsonProperty("blocks")] public int Blocks { get; set; }
    }
}
