using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class SparkMintInfo
    {
        [JsonProperty("txid")]
        [JsonPropertyName("txid")]
        public string TxId { get; set; }

        [JsonProperty("amount")]
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonProperty("nHeight")]
        [JsonPropertyName("nHeight")]
        public long Height { get; set; }

        [JsonProperty("nId")]
        [JsonPropertyName("nId")]
        public long MintId { get; set; }

        [JsonProperty("isUsed")]
        [JsonPropertyName("isUsed")]
        public bool IsUsed { get; set; }

        [JsonProperty("lTagHash")]
        [JsonPropertyName("lTagHash")]
        public string LTagHash { get; set; }

        [JsonProperty("memo")]
        [JsonPropertyName("memo")]
        public string Memo { get; set; }

        [JsonProperty("scriptPubKey")]
        [JsonPropertyName("scriptPubKey")]
        public string ScriptPubKey { get; set; }
    }

    public class SparkCoinAddrInfo
    {
        [JsonProperty("address")]
        [JsonPropertyName("address")]
        public string Address { get; set; }

        [JsonProperty("memo")]
        [JsonPropertyName("memo")]
        public string Memo { get; set; }

        [JsonProperty("amount")]
        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }
    }
}
