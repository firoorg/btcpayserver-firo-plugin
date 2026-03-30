using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Http;
using System.Threading.Tasks;

using BTCPayServer.Plugins.Firo.Configuration;
using BTCPayServer.Plugins.Firo.RPC;
using BTCPayServer.Plugins.Firo.RPC.Models;

using NBitcoin;

namespace BTCPayServer.Plugins.Firo.Services
{
    public class FiroRpcProvider
    {
        private readonly FiroLikeConfiguration _firoLikeConfiguration;
        private readonly EventAggregator _eventAggregator;
        public ImmutableDictionary<string, FiroRpcClient> RpcClients;

        public ConcurrentDictionary<string, FiroLikeSummary> Summaries { get; } = new();

        public FiroRpcProvider(FiroLikeConfiguration firoLikeConfiguration,
            EventAggregator eventAggregator,
            IHttpClientFactory httpClientFactory)
        {
            _firoLikeConfiguration = firoLikeConfiguration;
            _eventAggregator = eventAggregator;
            RpcClients = _firoLikeConfiguration.FiroLikeConfigurationItems.ToImmutableDictionary(
                pair => pair.Key,
                pair => new FiroRpcClient(
                    pair.Value.DaemonRpcUri,
                    pair.Value.Username,
                    pair.Value.Password,
                    httpClientFactory.CreateClient($"{pair.Key}client")));
        }

        public bool IsConfigured(string cryptoCode) =>
            RpcClients.ContainsKey(cryptoCode.ToUpperInvariant());

        public bool IsAvailable(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            return Summaries.ContainsKey(cryptoCode) && IsAvailable(Summaries[cryptoCode]);
        }

        private bool IsAvailable(FiroLikeSummary summary)
        {
            return summary.Synced && summary.WalletAvailable;
        }

        public async Task<FiroLikeSummary> UpdateSummary(string cryptoCode)
        {
            if (!RpcClients.TryGetValue(cryptoCode.ToUpperInvariant(), out var rpcClient))
            {
                return null;
            }

            var summary = new FiroLikeSummary();
            try
            {
                var blockchainInfo = await rpcClient.SendCommandAsync<BlockchainInfoResponse>(
                    "getblockchaininfo");
                summary.CurrentHeight = blockchainInfo.Blocks;
                summary.TargetHeight = blockchainInfo.Headers;
                summary.Synced = !blockchainInfo.InitialBlockDownload &&
                                 blockchainInfo.Blocks >= blockchainInfo.Headers - 1;
                summary.DaemonAvailable = true;
                summary.UpdatedAt = DateTime.UtcNow;
            }
            catch
            {
                summary.DaemonAvailable = false;
            }

            try
            {
                var networkInfo = await rpcClient.SendCommandAsync<NetworkInfoResponse>("getnetworkinfo");
                summary.DaemonVersion = networkInfo.SubVersion;
            }
            catch
            {
                // Version info is optional
            }

            try
            {
                // Verify wallet is available by calling getwalletinfo
                await rpcClient.SendCommandAsync<WalletInfoResponse>("getwalletinfo");
                summary.WalletAvailable = true;
                summary.WalletHeight = summary.CurrentHeight;
            }
            catch
            {
                summary.WalletAvailable = false;
            }

            var changed = !Summaries.ContainsKey(cryptoCode) ||
                          IsAvailable(cryptoCode) != IsAvailable(summary);

            Summaries.AddOrReplace(cryptoCode, summary);
            if (changed)
            {
                _eventAggregator.Publish(new FiroDaemonStateChange
                {
                    Summary = summary,
                    CryptoCode = cryptoCode
                });
            }

            return summary;
        }

        public class FiroDaemonStateChange
        {
            public string CryptoCode { get; set; }
            public FiroLikeSummary Summary { get; set; }
        }

        public class FiroLikeSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long WalletHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
            public string DaemonVersion { get; set; }
            public bool WalletAvailable { get; set; }
        }
    }
}
