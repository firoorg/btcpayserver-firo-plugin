namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroLikePaymentData
    {
        public int Diversifier { get; set; }
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string TransactionId { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}
