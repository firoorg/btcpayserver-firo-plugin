using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Firo.Configuration;
using BTCPayServer.Plugins.Firo.Payments;
using BTCPayServer.Plugins.Firo.Services;
using BTCPayServer.Services.Stores;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Plugins.Firo.Controllers
{
    [Route("stores/{storeId}/firolike")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UIFiroLikeStoreController : Controller
    {
        private readonly FiroLikeConfiguration _firoLikeConfiguration;
        private readonly StoreRepository _storeRepository;
        private readonly FiroRpcProvider _firoRpcProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private IStringLocalizer StringLocalizer { get; }

        public UIFiroLikeStoreController(FiroLikeConfiguration firoLikeConfiguration,
            StoreRepository storeRepository, FiroRpcProvider firoRpcProvider,
            PaymentMethodHandlerDictionary handlers,
            IStringLocalizer stringLocalizer)
        {
            _firoLikeConfiguration = firoLikeConfiguration;
            _storeRepository = storeRepository;
            _firoRpcProvider = firoRpcProvider;
            _handlers = handlers;
            StringLocalizer = stringLocalizer;
        }

        public StoreData StoreData => HttpContext.GetStoreData();

        [NonAction]
        public FiroLikePaymentMethodListViewModel GetVM(StoreData storeData)
        {
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();

            return new FiroLikePaymentMethodListViewModel()
            {
                Items = _firoLikeConfiguration.FiroLikeConfigurationItems.Select(pair =>
                    GetFiroPaymentMethodViewModel(storeData, pair.Key, excludeFilters))
            };
        }

        private FiroLikePaymentMethodViewModel GetFiroPaymentMethodViewModel(
            StoreData storeData, string cryptoCode, IPaymentFilter excludeFilters)
        {
            var firo = storeData.GetPaymentMethodConfigs(_handlers)
                .Where(s => s.Value is FiroPaymentPromptDetails)
                .Select(s => (PaymentMethodId: s.Key, Details: (FiroPaymentPromptDetails)s.Value));
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode);
            var settings = firo.Where(method => method.PaymentMethodId == pmi)
                .Select(m => m.Details).SingleOrDefault();
            _firoRpcProvider.Summaries.TryGetValue(cryptoCode, out var summary);

            var settlementThresholdChoice = FiroLikeSettlementThresholdChoice.StoreSpeedPolicy;
            if (settings != null && settings.InvoiceSettledConfirmationThreshold is { } confirmations)
            {
                settlementThresholdChoice = confirmations switch
                {
                    0 => FiroLikeSettlementThresholdChoice.ZeroConfirmation,
                    1 => FiroLikeSettlementThresholdChoice.AtLeastOne,
                    10 => FiroLikeSettlementThresholdChoice.AtLeastTen,
                    _ => FiroLikeSettlementThresholdChoice.Custom
                };
            }

            return new FiroLikePaymentMethodViewModel()
            {
                Enabled = settings != null &&
                          !excludeFilters.Match(pmi),
                Summary = summary,
                CryptoCode = cryptoCode,
                SettlementConfirmationThresholdChoice = settlementThresholdChoice,
                CustomSettlementConfirmationThreshold =
                    settings != null &&
                    settlementThresholdChoice is FiroLikeSettlementThresholdChoice.Custom
                        ? settings.InvoiceSettledConfirmationThreshold
                        : null
            };
        }

        [HttpGet("{cryptoCode}")]
        public IActionResult GetStoreFiroLikePaymentMethod(string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_firoLikeConfiguration.FiroLikeConfigurationItems.ContainsKey(cryptoCode))
            {
                return NotFound();
            }

            var vm = GetFiroPaymentMethodViewModel(StoreData, cryptoCode,
                StoreData.GetStoreBlob().GetExcludedPaymentMethods());
            return View("/Views/Firo/GetStoreFiroLikePaymentMethod.cshtml", vm);
        }

        [HttpPost("{cryptoCode}")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> GetStoreFiroLikePaymentMethod(
            FiroLikePaymentMethodViewModel viewModel, string command, string cryptoCode)
        {
            cryptoCode = cryptoCode.ToUpperInvariant();
            if (!_firoLikeConfiguration.FiroLikeConfigurationItems.TryGetValue(cryptoCode,
                    out _))
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                var vm = GetFiroPaymentMethodViewModel(StoreData, cryptoCode,
                    StoreData.GetStoreBlob().GetExcludedPaymentMethods());
                vm.Enabled = viewModel.Enabled;
                vm.SettlementConfirmationThresholdChoice =
                    viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold =
                    viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/Firo/GetStoreFiroLikePaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            storeData.SetPaymentMethodConfig(
                _handlers[PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode)],
                new FiroPaymentPromptDetails()
                {
                    InvoiceSettledConfirmationThreshold =
                        viewModel.SettlementConfirmationThresholdChoice switch
                        {
                            FiroLikeSettlementThresholdChoice.ZeroConfirmation => 0,
                            FiroLikeSettlementThresholdChoice.AtLeastOne => 1,
                            FiroLikeSettlementThresholdChoice.AtLeastTen => 10,
                            FiroLikeSettlementThresholdChoice.Custom
                                when viewModel.CustomSettlementConfirmationThreshold is { } custom =>
                                custom,
                            _ => null
                        }
                });

            blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId(viewModel.CryptoCode),
                !viewModel.Enabled);
            storeData.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(storeData);
            return RedirectToAction("GetStoreFiroLikePaymentMethod", new { cryptoCode });
        }

        public class FiroLikePaymentMethodListViewModel
        {
            public IEnumerable<FiroLikePaymentMethodViewModel> Items { get; set; }
        }

        public class FiroLikePaymentMethodViewModel : IValidatableObject
        {
            public FiroRpcProvider.FiroLikeSummary Summary { get; set; }
            public string CryptoCode { get; set; }
            public bool Enabled { get; set; }

            [Display(Name = "Consider the invoice settled when the payment transaction ...")]
            public FiroLikeSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }

            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is FiroLikeSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        new[] { nameof(CustomSettlementConfirmationThreshold) });
                }
            }
        }

        public enum FiroLikeSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,

            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,

            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,

            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,

            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}
