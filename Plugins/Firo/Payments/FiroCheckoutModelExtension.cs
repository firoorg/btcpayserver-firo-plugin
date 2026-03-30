using System.Collections.Generic;
using System.Linq;

using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Firo.Services;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroCheckoutModelExtension : ICheckoutModelExtension
    {
        private readonly BTCPayNetworkBase _network;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly IPaymentLinkExtension _paymentLinkExtension;

        public FiroCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions,
            BTCPayNetworkBase network,
            PaymentMethodHandlerDictionary handlers)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            _handlers = handlers;
            _paymentLinkExtension =
                paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
        }

        public PaymentMethodId PaymentMethodId { get; }

        public string Image => _network.CryptoImagePath;
        public string Badge => "";

        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            if (context is not { Handler: FiroLikePaymentMethodHandler handler })
            {
                return;
            }
            context.Model.CheckoutBodyComponentName =
                BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
            var allDetails = context.InvoiceEntity.GetPayments(true)
                .Select(p => p.GetDetails<FiroLikePaymentData>(handler))
                .Where(p => p is not null)
                .ToList();
            if (allDetails.Count > 0)
            {
                // Find the payment that is the bottleneck for settlement.
                // Locked payments (InstantSend at 0-conf, or ChainLocked) are
                // already settled, so sort them last.
                var leastSettled = allDetails
                    .OrderBy(d => FiroListener.IsSettled(d, context.InvoiceEntity.SpeedPolicy) ? 1 : 0)
                    .ThenBy(d => d.ConfirmationCount)
                    .First();

                if (FiroListener.IsSettled(leastSettled, context.InvoiceEntity.SpeedPolicy))
                {
                    // All payments settled (via lock or confirmations) - show as complete
                    context.Model.ReceivedConfirmations = 0;
                    context.Model.RequiredConfirmations = 0;
                }
                else
                {
                    var requiredConfs = FiroListener.ConfirmationsRequired(
                        leastSettled, context.InvoiceEntity.SpeedPolicy);
                    context.Model.ReceivedConfirmations = leastSettled.ConfirmationCount;
                    context.Model.RequiredConfirmations = (int)requiredConfs;
                }
            }

            context.Model.InvoiceBitcoinUrl =
                _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        }
    }
}
