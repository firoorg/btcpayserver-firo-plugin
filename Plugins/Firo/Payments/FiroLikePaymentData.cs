namespace BTCPayServer.Plugins.Firo.Payments
{
    public class FiroLikePaymentData
    {
        public string SparkAddress { get; set; }
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string TransactionId { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
        public bool InstantLocked { get; set; }
        public bool ChainLocked { get; set; }
    }
}
