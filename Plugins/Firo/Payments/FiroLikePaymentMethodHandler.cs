using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Firo.RPC.Models;
using BTCPayServer.Plugins.Firo.Services;

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

        public PaymentMethodId PaymentMethodId { get; }

        public FiroLikePaymentMethodHandler(FiroLikeSpecificBtcPayNetwork network,
            FiroRpcProvider firoRpcProvider)
        {
            PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            _network = network;
            Serializer = BlobSerializer.CreateSerializer().Serializer;
            _firoRpcProvider = firoRpcProvider;
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
                        GetFeeRate = rpcClient.SendCommandAsync<EstimateSmartFeeResponse>(
                            "estimatesmartfee", new object[] { 2 }),
                        ReserveAddress = async s =>
                        {
                            var address = await rpcClient.SendCommandAsync<string>(
                                "getnewsparkaddress");

                            // Get all addresses to find the diversifier for this address
                            var allAddresses = await rpcClient.SendCommandAsync<Dictionary<string, string>>(
                                "getallsparkaddresses");

                            int diversifier = -1;
                            if (allAddresses != null)
                            {
                                foreach (var kvp in allAddresses)
                                {
                                    if (kvp.Value == address && int.TryParse(kvp.Key, out var div))
                                    {
                                        diversifier = div;
                                        break;
                                    }
                                }
                            }

                            return new SparkAddressReservation
                            {
                                Address = address,
                                Diversifier = diversifier
                            };
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

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            if (!_firoRpcProvider.IsConfigured(_network.CryptoCode))
            {
                throw new PaymentMethodUnavailableException(
                    $"BTCPAY_FIRO_DAEMON_URI isn't configured");
            }

            if (!_firoRpcProvider.IsAvailable(_network.CryptoCode) ||
                context.State is not Prepare firoPrepare)
            {
                throw new PaymentMethodUnavailableException($"Node or wallet not available");
            }

            var invoice = context.InvoiceEntity;
            var feeEstimate = await firoPrepare.GetFeeRate;
            var reservation = await firoPrepare.ReserveAddress(invoice.Id);

            var feeRatePerKb = feeEstimate?.FeeRate ?? 0.00001m;

            var details = new FiroLikeOnChainPaymentMethodDetails()
            {
                Diversifier = reservation.Diversifier,
                InvoiceSettledConfirmationThreshold = firoPrepare.InvoiceSettledConfirmationThreshold
            };
            context.Prompt.Destination = reservation.Address;
            context.Prompt.PaymentMethodFee = feeRatePerKb;
            context.Prompt.Details = JObject.FromObject(details, Serializer);
            context.TrackedDestinations.Add(reservation.Address);
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
            public Task<EstimateSmartFeeResponse> GetFeeRate;
            public Func<string, Task<SparkAddressReservation>> ReserveAddress;
            public long? InvoiceSettledConfirmationThreshold { get; set; }
        }

        public class SparkAddressReservation
        {
            public string Address { get; set; }
            public int Diversifier { get; set; }
        }
    }
}
