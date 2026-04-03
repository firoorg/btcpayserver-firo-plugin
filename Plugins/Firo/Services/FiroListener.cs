using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Firo.Configuration;
using BTCPayServer.Plugins.Firo.Payments;
using BTCPayServer.Plugins.Firo.RPC;
using BTCPayServer.Plugins.Firo.RPC.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;

using Microsoft.Extensions.Logging;

using NBitcoin;

using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Firo.Services
{
    public class FiroListener : EventHostedServiceBase
    {
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EventAggregator _eventAggregator;
        private readonly FiroRpcProvider _firoRpcProvider;
        private readonly FiroLikeConfiguration _firoLikeConfiguration;
        private readonly BTCPayNetworkProvider _networkProvider;
        private readonly ILogger<FiroListener> _logger;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly InvoiceActivator _invoiceActivator;
        private readonly PaymentService _paymentService;

        public FiroListener(InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            FiroRpcProvider firoRpcProvider,
            FiroLikeConfiguration firoLikeConfiguration,
            BTCPayNetworkProvider networkProvider,
            ILogger<FiroListener> logger,
            PaymentMethodHandlerDictionary handlers,
            InvoiceActivator invoiceActivator,
            PaymentService paymentService) : base(eventAggregator, logger)
        {
            _invoiceRepository = invoiceRepository;
            _eventAggregator = eventAggregator;
            _firoRpcProvider = firoRpcProvider;
            _firoLikeConfiguration = firoLikeConfiguration;
            _networkProvider = networkProvider;
            _logger = logger;
            _handlers = handlers;
            _invoiceActivator = invoiceActivator;
            _paymentService = paymentService;
        }

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Subscribe<FiroEvent>();
            Subscribe<FiroRpcProvider.FiroDaemonStateChange>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is FiroRpcProvider.FiroDaemonStateChange stateChange)
            {
                if (_firoRpcProvider.IsAvailable(stateChange.CryptoCode))
                {
                    _logger.LogInformation($"{stateChange.CryptoCode} just became available");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await UpdateAnyPendingFiroPayment(stateChange.CryptoCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                $"Error updating pending payments after {stateChange.CryptoCode} became available");
                        }
                    });
                }
                else
                {
                    _logger.LogInformation($"{stateChange.CryptoCode} just became unavailable");
                }
            }
            else if (evt is FiroEvent firoEvent)
            {
                if (!_firoRpcProvider.IsAvailable(firoEvent.CryptoCode))
                {
                    return;
                }

                if (!string.IsNullOrEmpty(firoEvent.BlockHash))
                {
                    await OnNewBlock(firoEvent.CryptoCode);
                }

                if (!string.IsNullOrEmpty(firoEvent.TransactionHash))
                {
                    await OnTransactionUpdated(firoEvent.CryptoCode, firoEvent.TransactionHash);
                }
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            _logger.LogInformation(
                $"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);

            if (prompt != null &&
                prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                await _invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id,
                    payment.PaymentMethodId, true);
                invoice = await _invoiceRepository.GetInvoice(invoice.Id);
            }

            _eventAggregator.Publish(
                new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private async Task UpdatePaymentStates(string cryptoCode, InvoiceEntity[] invoices)
        {
            if (!invoices.Any())
            {
                return;
            }

            var rpcClient = _firoRpcProvider.RpcClients[cryptoCode];
            var network = _networkProvider.GetNetwork(cryptoCode);
            var paymentId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (FiroLikePaymentMethodHandler)_handlers[paymentId];

            // Build invoice lookup data
            var expandedInvoices = invoices.Select(entity => (
                    Invoice: entity,
                    ExistingPayments: GetAllFiroPayments(entity, cryptoCode),
                    Prompt: entity.GetPaymentPrompt(paymentId),
                    PaymentMethodDetails: handler.ParsePaymentPromptDetails(
                        entity.GetPaymentPrompt(paymentId).Details)))
                .Select(tuple => (
                    tuple.Invoice,
                    tuple.PaymentMethodDetails,
                    tuple.Prompt,
                    ExistingPayments: tuple.ExistingPayments.Select(entity =>
                        (Payment: entity,
                            PaymentData: handler.ParsePaymentDetails(entity.Details),
                            tuple.Invoice))
                )).ToList();

            // Build a set of tracked destination addresses for quick lookup
            var addressToInvoice = new Dictionary<string, (InvoiceEntity Invoice,
                FiroLikeOnChainPaymentMethodDetails Details)>();
            foreach (var ei in expandedInvoices)
            {
                if (ei.Prompt?.Destination != null)
                {
                    addressToInvoice[ei.Prompt.Destination] = (ei.Invoice, ei.PaymentMethodDetails);
                }
            }

            var existingPaymentData = expandedInvoices
                .SelectMany(tuple => tuple.ExistingPayments).ToList();

            // Get all spark mints from the wallet
            SparkMintInfo[] mints;
            try
            {
                mints = await rpcClient.SendCommandAsync<SparkMintInfo[]>("listsparkmints");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to list spark mints for {cryptoCode}");
                return;
            }

            if (mints == null || mints.Length == 0)
            {
                return;
            }

            // For each unique txid in mints, resolve Spark addresses via getsparkcoinaddr
            var txIds = mints.Select(m => m.TxId).Distinct().ToList();
            var txToAddressOutputs = new Dictionary<string, SparkCoinAddrInfo[]>();

            foreach (var txId in txIds)
            {
                try
                {
                    var coinAddrs = await rpcClient.SendCommandAsync<SparkCoinAddrInfo[]>(
                        "getsparkcoinaddr", new object[] { txId });
                    if (coinAddrs != null)
                    {
                        txToAddressOutputs[txId] = coinAddrs;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"Failed to resolve Spark addresses for tx {txId}");
                }
            }

            var paymentsToUpdate = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();

            // Match resolved addresses to tracked invoices
            foreach (var kvp in txToAddressOutputs)
            {
                var txId = kvp.Key;
                var outputs = kvp.Value;

                // Find the mint info for this txid (for block height)
                var mintInfo = mints.FirstOrDefault(m => m.TxId == txId);

                // Get transaction details including confirmations and lock states
                long confirmations = 0;
                long blockHeight = mintInfo?.Height ?? 0;
                bool instantLocked = false;
                bool chainLocked = false;
                try
                {
                    var txInfo = await rpcClient.SendCommandAsync<GetTransactionResponse>(
                        "gettransaction", new object[] { txId });
                    confirmations = txInfo.Confirmations;
                    instantLocked = txInfo.InstantLock;
                    chainLocked = txInfo.ChainLock;
                    if (txInfo.BlockHeight > 0)
                    {
                        blockHeight = txInfo.BlockHeight;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"Failed to get transaction info for {txId}, using mint data");
                }

                // Group outputs by destination address and match to invoices
                foreach (var output in outputs)
                {
                    if (output.Address == null)
                    {
                        continue;
                    }

                    // Check if this address matches a tracked invoice
                    if (!addressToInvoice.TryGetValue(output.Address, out var invoiceData))
                    {
                        // Also check via InvoiceRepository for existing payments
                        var invoice = await _invoiceRepository.GetInvoiceFromAddress(
                            paymentId, output.Address);
                        if (invoice == null)
                        {
                            continue;
                        }
                        var details = handler.ParsePaymentPromptDetails(
                            invoice.GetPaymentPrompt(paymentId).Details);
                        invoiceData = (invoice, details);
                    }

                    await HandlePaymentData(
                        cryptoCode,
                        output.Amount,
                        output.Address,
                        txId,
                        confirmations,
                        blockHeight,
                        instantLocked,
                        chainLocked,
                        invoiceData.Invoice,
                        invoiceData.Details,
                        paymentsToUpdate);
                }
            }

            if (paymentsToUpdate.Any())
            {
                await _paymentService.UpdatePayments(
                    paymentsToUpdate.Select(tuple => tuple.Payment).ToList());
                foreach (var group in paymentsToUpdate.GroupBy(entity => entity.invoice))
                {
                    _eventAggregator.Publish(new InvoiceNeedUpdateEvent(group.Key.Id));
                }
            }
        }

        private async Task OnNewBlock(string cryptoCode)
        {
            await UpdateAnyPendingFiroPayment(cryptoCode);
            _eventAggregator.Publish(new NewBlockEvent()
            {
                PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)
            });
        }

        private async Task OnTransactionUpdated(string cryptoCode, string transactionHash)
        {
            await UpdateAnyPendingFiroPayment(cryptoCode);
        }

        private async Task HandlePaymentData(string cryptoCode, decimal amount,
            string sparkAddress, string txId, long confirmations, long blockHeight,
            bool instantLocked, bool chainLocked,
            InvoiceEntity invoice, FiroLikeOnChainPaymentMethodDetails promptDetails,
            List<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (FiroLikePaymentMethodHandler)_handlers[pmi];

            var details = new FiroLikePaymentData()
            {
                SparkAddress = sparkAddress,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                BlockHeight = blockHeight,
                InstantLocked = instantLocked,
                ChainLocked = chainLocked,
                InvoiceSettledConfirmationThreshold =
                    promptDetails.InvoiceSettledConfirmationThreshold
            };

            var status = IsSettled(details, invoice.SpeedPolicy)
                ? PaymentStatus.Settled
                : PaymentStatus.Processing;

            var paymentData = new PaymentData()
            {
                Status = status,
                Amount = amount,
                Created = DateTimeOffset.UtcNow,
                Id = $"{txId}#{sparkAddress}",
                Currency = network.CryptoCode,
                InvoiceDataId = invoice.Id,
            }.Set(invoice, handler, details);

            // Check if this tx already exists as a payment to this invoice
            var alreadyExisting = GetAllFiroPayments(invoice, cryptoCode)
                .SingleOrDefault(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);

            if (alreadyExisting == null)
            {
                var payment = await _paymentService.AddPayment(paymentData, [txId]);
                if (payment != null)
                {
                    await ReceivedPayment(invoice, payment);
                }
            }
            else
            {
                // Update existing payment with new data
                alreadyExisting.Status = status;
                alreadyExisting.Details = JToken.FromObject(details, handler.Serializer);
                paymentsToUpdate.Add((alreadyExisting, invoice));
            }
        }

        public static bool IsSettled(FiroLikePaymentData details, SpeedPolicy speedPolicy)
        {
            // InstantSend lock secures the transaction before block inclusion
            // (0 confirmations). This is considered safe to accept as settled.
            if (details.InstantLocked && details.ConfirmationCount == 0)
            {
                return true;
            }

            // A ChainLocked block is finalized and immutable. Only 1 confirmation
            // is needed (proving inclusion in the chainlocked block).
            if (details.ChainLocked && details.ConfirmationCount >= 1)
            {
                return true;
            }

            // No lock — fall back to full confirmation-based settlement
            return ConfirmationsRequired(details, speedPolicy) <= details.ConfirmationCount;
        }

        public static long ConfirmationsRequired(FiroLikePaymentData details, SpeedPolicy speedPolicy)
        {
            // InstantSend locked at 0 confs — no confirmations needed
            if (details.InstantLocked && details.ConfirmationCount == 0)
            {
                return 0;
            }

            // ChainLocked block — only 1 confirmation needed
            if (details.ChainLocked)
            {
                return 1;
            }

            // No lock — use store speed policy or custom threshold
            return (details, speedPolicy) switch
            {
                ({ InvoiceSettledConfirmationThreshold: long v }, _) => v,
                (_, SpeedPolicy.HighSpeed) => 0,
                (_, SpeedPolicy.MediumSpeed) => 1,
                (_, SpeedPolicy.LowMediumSpeed) => 2,
                (_, SpeedPolicy.LowSpeed) => 6,
                _ => 6,
            };
        }

        /// <summary>
        /// Returns a human-readable description of the transaction's lock/confirmation state.
        /// </summary>
        public static string GetLockStatusDescription(FiroLikePaymentData details)
        {
            if (details.ConfirmationCount == 0 && details.InstantLocked)
            {
                return "InstantSend Locked";
            }

            if (details.ChainLocked)
            {
                return "ChainLocked";
            }

            if (details.ConfirmationCount > 0)
            {
                return "Confirmed";
            }

            return "Unconfirmed";
        }

        private async Task UpdateAnyPendingFiroPayment(string cryptoCode)
        {
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var invoices = await _invoiceRepository.GetMonitoredInvoices(paymentMethodId);
            if (!invoices.Any())
            {
                return;
            }
            invoices = invoices
                .Where(entity => entity.GetPaymentPrompt(paymentMethodId)?.Activated is true)
                .ToArray();
            await UpdatePaymentStates(cryptoCode, invoices);
        }

        private IEnumerable<PaymentEntity> GetAllFiroPayments(InvoiceEntity invoice,
            string cryptoCode)
        {
            return invoice.GetPayments(false)
                .Where(p => p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode));
        }
    }
}
