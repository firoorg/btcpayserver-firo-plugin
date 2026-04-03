using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Firo.Services;
using BTCPayServer.Services;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroLikePaymentMethodHandler : IPaymentMethodHandler
    {
        private readonly FiroLikeSpecificBtcPayNetwork _network;
        public FiroLikeSpecificBtcPayNetwork Network => _network;
        public JsonSerializer Serializer { get; }
        private readonly FiroRpcProvider _firoRpcProvider;
        private readonly WalletRepository _walletRepository;

        public PaymentMethodId PaymentMethodId { get; }

        public FiroLikePaymentMethodHandler(FiroLikeSpecificBtcPayNetwork network,
            FiroRpcProvider firoRpcProvider,
            WalletRepository walletRepository)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _firoRpcProvider = firoRpcProvider;
            _walletRepository = walletRepository;
        }

        bool IsReady() => _firoRpcProvider.IsConfigured(_network.CryptoCode) &&
                          _firoRpcProvider.IsAvailable(_network.CryptoCode);

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = _network.CryptoCode;
            context.Prompt.Divisibility = _network.Divisibility;
            if (context.Prompt.Activated && IsReady())
            {
                var supportedPaymentMethod = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                var rpcClient = _firoRpcProvider.RpcClients[_network.CryptoCode];
                try
                {
                    context.State = new Prepare()
                    {
                        // estimatefee <nblocks> returns a decimal fee rate per kB, or -1
                        GetFeeRate = rpcClient.SendCommandAsync<decimal>(
                            "estimatefee", new object[] { 2 }),
                        ReserveAddress = async s =>
                        {
                            // getnewsparkaddress returns a VARR with one string element
                            var result = await rpcClient.SendCommandAsync<string[]>(
                                "getnewsparkaddress");
                            if (result == null || result.Length == 0)
                            {
                                throw new InvalidOperationException(
                                    "getnewsparkaddress returned empty result");
                            }
                            return result[0];
                        },
                        InvoiceSettledConfirmationThreshold =
                            supportedPaymentMethod.InvoiceSettledConfirmationThreshold
                    };
                }
                catch (Exception ex)
                {
                    context.Logs.Write($"Error in BeforeFetchingRates: {ex.Message}",
                        InvoiceEventData.EventSeverity.Error);
                }
            }
            return Task.CompletedTask;
        }

        public async Task AfterSavingInvoice(PaymentMethodContext paymentMethodContext)
        {
            var paymentPrompt = paymentMethodContext.Prompt;
            var store = paymentMethodContext.Store;
            var entity = paymentMethodContext.InvoiceEntity;
            var links = new List<WalletObjectLinkData>();
            var walletId = new WalletId(store.Id, _network.CryptoCode);
            await _walletRepository.EnsureWalletObject(new WalletObjectId(
                walletId,
                WalletObjectData.Types.Invoice,
                entity.Id));
            if (paymentPrompt.Destination is string destination)
            {
                links.Add(WalletRepository.NewWalletObjectLinkData(
                    new WalletObjectId(
                        walletId,
                        WalletObjectData.Types.Address,
                        destination),
                    new WalletObjectId(
                        walletId,
                        WalletObjectData.Types.Invoice,
                        entity.Id)));
            }

            await _walletRepository.EnsureCreated(null, links);
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_firoRpcProvider.IsConfigured(_network.CryptoCode))
            {
                throw new PaymentMethodUnavailableException(
                    "BTCPAY_FIRO_DAEMON_URI isn't configured");
            }

            if (!_firoRpcProvider.IsAvailable(_network.CryptoCode) ||
                context.State is not Prepare firoPrepare)
            {
                throw new PaymentMethodUnavailableException("Node or wallet not available");
            }

            var invoice = context.InvoiceEntity;
            var feeRatePerKb = await firoPrepare.GetFeeRate;
            var address = await firoPrepare.ReserveAddress(invoice.Id);

            // estimatefee returns -1 if estimation is not possible
            if (feeRatePerKb <= 0)
            {
                feeRatePerKb = 0.00001m;
            }

            var details = new FiroLikeOnChainPaymentMethodDetails()
            {
                InvoiceSettledConfirmationThreshold = firoPrepare.InvoiceSettledConfirmationThreshold
            };
            context.Prompt.Destination = address;
            context.Prompt.PaymentMethodFee = feeRatePerKb;
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(address);
        }

        private FiroPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<FiroPaymentPromptDetails>(Serializer) ??
                   throw new FormatException($"Invalid {nameof(FiroLikePaymentMethodHandler)}");
        }

        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        public FiroLikeOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<FiroLikeOnChainPaymentMethodDetails>(Serializer);
        }

        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }

        public FiroLikePaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<FiroLikePaymentData>(Serializer) ??
                   throw new FormatException($"Invalid {nameof(FiroLikePaymentMethodHandler)}");
        }

        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }

        class Prepare
        {
            public Task<decimal> GetFeeRate;
            public Func<string, Task<string>> ReserveAddress;
            public long? InvoiceSettledConfirmationThreshold { get; set; }
        }
    }
}
