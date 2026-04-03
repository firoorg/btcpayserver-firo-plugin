using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class BlockchainInfoResponse
    {
        [JsonProperty("chain")]
        [JsonPropertyName("chain")]
        public string Chain { get; set; }

        [JsonProperty("blocks")]
        [JsonPropertyName("blocks")]
        public long Blocks { get; set; }

        [JsonProperty("headers")]
        [JsonPropertyName("headers")]
        public long Headers { get; set; }

        [JsonProperty("verificationprogress")]
        [JsonPropertyName("verificationprogress")]
        public decimal VerificationProgress { get; set; }
    }

    public class NetworkInfoResponse
    {
        [JsonProperty("version")]
        [JsonPropertyName("version")]
        public long Version { get; set; }

        [JsonProperty("subversion")]
        [JsonPropertyName("subversion")]
        public string SubVersion { get; set; }

        [JsonProperty("protocolversion")]
        [JsonPropertyName("protocolversion")]
        public long ProtocolVersion { get; set; }
    }

    public class WalletInfoResponse
    {
        [JsonProperty("walletname")]
        [JsonPropertyName("walletname")]
        public string WalletName { get; set; }

        [JsonProperty("walletversion")]
        [JsonPropertyName("walletversion")]
        public long WalletVersion { get; set; }
    }
}
