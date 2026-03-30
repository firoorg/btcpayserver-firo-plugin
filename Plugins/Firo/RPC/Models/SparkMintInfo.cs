using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class SparkMintInfo
    {
        [JsonProperty("txid")] public string TxId { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("nHeight")] public long Height { get; set; }
        [JsonProperty("nId")] public long MintId { get; set; }
        [JsonProperty("isUsed")] public bool IsUsed { get; set; }
        [JsonProperty("lTagHash")] public string LTagHash { get; set; }
        [JsonProperty("memo")] public string Memo { get; set; }
    }

    public class SparkCoinAddrInfo
    {
        [JsonProperty("address")] public string Address { get; set; }
        [JsonProperty("memo")] public string Memo { get; set; }
        [JsonProperty("amount")] public decimal Amount { get; set; }
    }
}
