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
            var details = context.InvoiceEntity.GetPayments(true)
                .Select(p => p.GetDetails<FiroLikePaymentData>(handler))
                .Where(p => p is not null)
                .FirstOrDefault();
            if (details is not null)
            {
                context.Model.ReceivedConfirmations = details.ConfirmationCount;
                context.Model.RequiredConfirmations =
                    (int)FiroListener.ConfirmationsRequired(details,
                        context.InvoiceEntity.SpeedPolicy);
            }

            context.Model.InvoiceBitcoinUrl =
                _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        }
    }
}
