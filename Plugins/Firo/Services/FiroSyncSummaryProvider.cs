using System.Collections.Generic;
using System.Linq;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.Firo.Services
{
    public class FiroSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly FiroRpcProvider _firoRpcProvider;

        public FiroSyncSummaryProvider(FiroRpcProvider firoRpcProvider)
        {
            _firoRpcProvider = firoRpcProvider;
        }

        public bool AllAvailable()
        {
            return _firoRpcProvider.Summaries.All(pair => pair.Value.DaemonAvailable);
        }

        public string Partial { get; } = "/Views/Firo/FiroSyncSummary.cshtml";

        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _firoRpcProvider.Summaries.Select(pair => new FiroSyncStatus()
            {
                Summary = pair.Value,
                PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(pair.Key).ToString()
            });
        }
    }

    public class FiroSyncStatus : SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get { return Summary?.WalletAvailable ?? false; }
        }

        public FiroRpcProvider.FiroLikeSummary Summary { get; set; }
    }
}
