#nullable enable
using System.Globalization;

using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroPaymentLinkExtension : IPaymentLinkExtension
    {
        private readonly FiroLikeSpecificBtcPayNetwork _network;

        public FiroPaymentLinkExtension(PaymentMethodId paymentMethodId,
            FiroLikeSpecificBtcPayNetwork network)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
        }

        public PaymentMethodId PaymentMethodId { get; }

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            return $"{_network.UriScheme}:{prompt.Destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
