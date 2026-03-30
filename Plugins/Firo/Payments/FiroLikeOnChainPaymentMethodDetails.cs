namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroLikeOnChainPaymentMethodDetails
    {
        public int Diversifier { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}
