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
                    _ = UpdateAnyPendingFiroPayment(stateChange.CryptoCode);
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

            // Get all the required data in one list
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

            var existingPaymentData = expandedInvoices
                .SelectMany(tuple => tuple.ExistingPayments).ToList();

            // Collect all diversifiers we need to track
            var trackedDiversifiers = new HashSet<int>();
            foreach (var expandedInvoice in expandedInvoices)
            {
                trackedDiversifiers.Add(expandedInvoice.PaymentMethodDetails.Diversifier);
                foreach (var ep in expandedInvoice.ExistingPayments)
                {
                    trackedDiversifiers.Add(ep.PaymentData.Diversifier);
                }
            }

            // Get all spark mints from the wallet
            SparkMintInfo[] mints;
            try
            {
                mints = await rpcClient.SendCommandAsync<SparkMintInfo[]>(
                    "listsparkmints", new object[] { true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to list spark mints for {cryptoCode}");
                return;
            }

            if (mints == null)
            {
                return;
            }

            // Filter mints that match our tracked diversifiers
            var relevantMints = mints.Where(m => trackedDiversifiers.Contains(m.Diversifier)).ToList();

            var paymentsToUpdate = new List<(PaymentEntity Payment, InvoiceEntity invoice)>();
            var processingTasks = new List<Task>();

            foreach (var mint in relevantMints)
            {
                // Find which invoice this mint belongs to
                var matchingInvoice = expandedInvoices.FirstOrDefault(ei =>
                    ei.PaymentMethodDetails.Diversifier == mint.Diversifier);

                if (matchingInvoice.Invoice == null)
                {
                    // Check existing payments
                    var existingMatch = existingPaymentData.FirstOrDefault(ep =>
                        ep.PaymentData.Diversifier == mint.Diversifier &&
                        ep.PaymentData.TransactionId == mint.TxId);
                    if (existingMatch.Invoice != null)
                    {
                        matchingInvoice = expandedInvoices.First(ei =>
                            ei.Invoice.Id == existingMatch.Invoice.Id);
                    }
                }

                if (matchingInvoice.Invoice == null)
                {
                    continue;
                }

                // Get confirmation count for this transaction
                long confirmations = 0;
                long blockHeight = mint.Height;
                try
                {
                    var txInfo = await rpcClient.SendCommandAsync<GetTransactionResponse>(
                        "gettransaction", new object[] { mint.TxId });
                    confirmations = txInfo.Confirmations;
                    if (txInfo.BlockHeight > 0)
                    {
                        blockHeight = txInfo.BlockHeight;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        $"Failed to get transaction info for {mint.TxId}, using mint data");
                }

                processingTasks.Add(HandlePaymentData(
                    cryptoCode,
                    mint.Amount,
                    mint.Diversifier,
                    mint.TxId,
                    confirmations,
                    blockHeight,
                    matchingInvoice.Invoice,
                    paymentsToUpdate));
            }

            await Task.WhenAll(processingTasks);

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
            // When we get a tx notification, we update all pending invoices
            // because we can't easily map a single txid to a specific Spark address
            // without querying listsparkmints anyway
            await UpdateAnyPendingFiroPayment(cryptoCode);
        }

        private async Task HandlePaymentData(string cryptoCode, decimal amount, int diversifier,
            string txId, long confirmations, long blockHeight, InvoiceEntity invoice,
            List<(PaymentEntity Payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            var network = _networkProvider.GetNetwork(cryptoCode);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            var handler = (FiroLikePaymentMethodHandler)_handlers[pmi];
            var promptDetails = handler.ParsePaymentPromptDetails(
                invoice.GetPaymentPrompt(pmi).Details);

            var details = new FiroLikePaymentData()
            {
                Diversifier = diversifier,
                TransactionId = txId,
                ConfirmationCount = confirmations,
                BlockHeight = blockHeight,
                InvoiceSettledConfirmationThreshold =
                    promptDetails.InvoiceSettledConfirmationThreshold
            };

            var status = GetStatus(details, invoice.SpeedPolicy)
                ? PaymentStatus.Settled
                : PaymentStatus.Processing;

            var paymentData = new PaymentData()
            {
                Status = status,
                Amount = amount,
                Created = DateTimeOffset.UtcNow,
                Id = $"{txId}#{diversifier}",
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

        private bool GetStatus(FiroLikePaymentData details, SpeedPolicy speedPolicy)
            => ConfirmationsRequired(details, speedPolicy) <= details.ConfirmationCount;

        public static long ConfirmationsRequired(FiroLikePaymentData details, SpeedPolicy speedPolicy)
            => (details, speedPolicy) switch
            {
                ({ InvoiceSettledConfirmationThreshold: long v }, _) => v,
                (_, SpeedPolicy.HighSpeed) => 0,
                (_, SpeedPolicy.MediumSpeed) => 1,
                (_, SpeedPolicy.LowMediumSpeed) => 2,
                (_, SpeedPolicy.LowSpeed) => 6,
                _ => 6,
            };

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
