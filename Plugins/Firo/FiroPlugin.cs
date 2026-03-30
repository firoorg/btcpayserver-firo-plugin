using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Firo.Configuration;
using BTCPayServer.Plugins.Firo.Payments;
using BTCPayServer.Plugins.Firo.Services;
using BTCPayServer.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NBitcoin;

using NBXplorer;

namespace BTCPayServer.Plugins.Firo;

public class FiroPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.1.0" }
    };

    public override void Execute(IServiceCollection services)
    {
        var pluginServices = (PluginServiceCollection)services;
        var prov = pluginServices.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var chainName = prov.NetworkType;

        var network = new FiroLikeSpecificBtcPayNetwork()
        {
            CryptoCode = "FIRO",
            DisplayName = "Firo",
            Divisibility = 8,
            DefaultRateRules = new[]
            {
                "FIRO_X = FIRO_BTC * BTC_X",
                "FIRO_BTC = binance(FIRO_BTC)"
            },
            CryptoImagePath = "firo.svg",
            UriScheme = "firo"
        };
        var blockExplorerLink = chainName == ChainName.Mainnet
            ? "https://explorer.firo.org/tx/{0}"
            : "https://testexplorer.firo.org/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("FIRO");
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddBTCPayNetwork(network)
            .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));

        services.AddSingleton(provider =>
            ConfigureFiroLikeConfiguration(provider));
        services.AddHttpClient("FIROclient")
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var configuration = provider.GetRequiredService<FiroLikeConfiguration>();
                if (!configuration.FiroLikeConfigurationItems.TryGetValue("FIRO", out var firoConfig) ||
                    firoConfig.Username is null || firoConfig.Password is null)
                {
                    return new HttpClientHandler();
                }
                return new HttpClientHandler
                {
                    Credentials = new NetworkCredential(firoConfig.Username, firoConfig.Password),
                    PreAuthenticate = true
                };
            });
        services.AddSingleton<FiroRpcProvider>();
        services.AddHostedService<FiroLikeSummaryUpdaterHostedService>();
        services.AddHostedService<FiroListener>();
        services.AddSingleton(provider =>
            (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider,
                typeof(FiroLikePaymentMethodHandler), network));
        services.AddSingleton(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider,
                typeof(FiroPaymentLinkExtension), network, pmi));
        services.AddSingleton(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider,
                typeof(FiroCheckoutModelExtension), network, pmi));

        services.AddUIExtension("store-wallets-nav",
            "/Views/Firo/StoreWalletsNavFiroExtension.cshtml");
        services.AddUIExtension("store-invoices-payments",
            "/Views/Firo/ViewFiroLikePaymentData.cshtml");
        services.AddSingleton<ISyncSummaryProvider, FiroSyncSummaryProvider>();
    }

    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
            {
                return null;
            }
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }

    private static FiroLikeConfiguration ConfigureFiroLikeConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new FiroLikeConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll()
            .OfType<FiroLikeSpecificBtcPayNetwork>();

        foreach (var firoNetwork in supportedNetworks)
        {
            var daemonUri =
                configuration.GetOrDefault<Uri>($"{firoNetwork.CryptoCode}_daemon_uri", null);
            var daemonUsername =
                configuration.GetOrDefault<string>($"{firoNetwork.CryptoCode}_daemon_username", null);
            var daemonPassword =
                configuration.GetOrDefault<string>($"{firoNetwork.CryptoCode}_daemon_password", null);

            if (daemonUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<FiroPlugin>>();
                var cryptoCode = firoNetwork.CryptoCode.ToUpperInvariant();
                logger.LogWarning($"BTCPAY_{cryptoCode}_DAEMON_URI is not configured");
                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.FiroLikeConfigurationItems.Add(firoNetwork.CryptoCode,
                    new FiroLikeConfigurationItem
                    {
                        DaemonRpcUri = daemonUri,
                        Username = daemonUsername,
                        Password = daemonPassword
                    });
            }
        }
        return result;
    }
}
