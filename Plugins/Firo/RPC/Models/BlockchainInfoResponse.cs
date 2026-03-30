using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Firo.RPC.Models
{
    public class BlockchainInfoResponse
    {
        [JsonProperty("chain")] public string Chain { get; set; }
        [JsonProperty("blocks")] public long Blocks { get; set; }
        [JsonProperty("headers")] public long Headers { get; set; }
        [JsonProperty("verificationprogress")] public decimal VerificationProgress { get; set; }
        [JsonProperty("initialblockdownload")] public bool InitialBlockDownload { get; set; }
    }

    public class NetworkInfoResponse
    {
        [JsonProperty("version")] public long Version { get; set; }
        [JsonProperty("subversion")] public string SubVersion { get; set; }
        [JsonProperty("protocolversion")] public long ProtocolVersion { get; set; }
    }

    public class WalletInfoResponse
    {
        [JsonProperty("walletname")] public string WalletName { get; set; }
        [JsonProperty("walletversion")] public long WalletVersion { get; set; }
    }
}
