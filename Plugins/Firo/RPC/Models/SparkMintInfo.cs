using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class SparkMintInfo
    {
        [JsonProperty("txid")] public string TxId { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("diversifier")] public int Diversifier { get; set; }
        [JsonProperty("height")] public long Height { get; set; }
        [JsonProperty("isUsed")] public bool IsUsed { get; set; }
        [JsonProperty("serialHash")] public string SerialHash { get; set; }
    }
}
