using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class GetTransactionResponse
    {
        [JsonProperty("amount")] public decimal Amount { get; set; }
        [JsonProperty("confirmations")] public long Confirmations { get; set; }
        [JsonProperty("blockhash")] public string BlockHash { get; set; }
        [JsonProperty("blockheight")] public long BlockHeight { get; set; }
        [JsonProperty("blockindex")] public long BlockIndex { get; set; }
        [JsonProperty("blocktime")] public long BlockTime { get; set; }
        [JsonProperty("txid")] public string TxId { get; set; }
        [JsonProperty("time")] public long Time { get; set; }
        [JsonProperty("timereceived")] public long TimeReceived { get; set; }
    }
}
