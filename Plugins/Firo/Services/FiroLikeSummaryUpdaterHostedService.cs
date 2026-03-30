using System;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Logging;
using BTCPayServer.Plugins.Firo.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Firo.Services
{
    public class FiroLikeSummaryUpdaterHostedService : IHostedService
    {
        private readonly FiroRpcProvider _firoRpcProvider;
        private readonly FiroLikeConfiguration _firoLikeConfiguration;

        public Logs Logs { get; }

        private CancellationTokenSource _Cts;

        public FiroLikeSummaryUpdaterHostedService(FiroRpcProvider firoRpcProvider,
            FiroLikeConfiguration firoLikeConfiguration, Logs logs)
        {
            _firoRpcProvider = firoRpcProvider;
            _firoLikeConfiguration = firoLikeConfiguration;
            Logs = logs;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            foreach (var configItem in _firoLikeConfiguration.FiroLikeConfigurationItems)
            {
                _ = StartLoop(_Cts.Token, configItem.Key);
            }
            return Task.CompletedTask;
        }

        private async Task StartLoop(CancellationToken cancellation, string cryptoCode)
        {
            Logs.PayServer.LogInformation($"Starting listening Firo daemon ({cryptoCode})");
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        await _firoRpcProvider.UpdateSummary(cryptoCode);
                        if (_firoRpcProvider.IsAvailable(cryptoCode))
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), cancellation);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                        }
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex,
                            $"Unhandled exception in Summary updater ({cryptoCode})");
                        await Task.Delay(TimeSpan.FromSeconds(10), cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
                // ignored
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts?.Cancel();
            _Cts?.Dispose();
            return Task.CompletedTask;
        }
    }
}
