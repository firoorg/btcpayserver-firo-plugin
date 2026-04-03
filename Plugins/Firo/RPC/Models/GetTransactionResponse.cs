using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class GetTransactionResponse
    {
        [JsonProperty("amount")]
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("confirmations")]
        [JsonPropertyName("confirmations")]
        public long Confirmations { get; set; }

        [JsonProperty("blockhash")]
        [JsonPropertyName("blockhash")]
        public string BlockHash { get; set; }

        [JsonProperty("blockheight")]
        [JsonPropertyName("blockheight")]
        public long BlockHeight { get; set; }

        [JsonProperty("blockindex")]
        [JsonPropertyName("blockindex")]
        public long BlockIndex { get; set; }

        [JsonProperty("blocktime")]
        [JsonPropertyName("blocktime")]
        public long BlockTime { get; set; }

        [JsonProperty("txid")]
        [JsonPropertyName("txid")]
        public string TxId { get; set; }

        [JsonProperty("time")]
        [JsonPropertyName("time")]
        public long Time { get; set; }

        [JsonProperty("timereceived")]
        [JsonPropertyName("timereceived")]
        public long TimeReceived { get; set; }

        [JsonProperty("instantlock")]
        [JsonPropertyName("instantlock")]
        public bool InstantLock { get; set; }

        [JsonProperty("chainlock")]
        [JsonPropertyName("chainlock")]
        public bool ChainLock { get; set; }
    }
}
