using BTCPayServer.Plugins.Firo.RPC;

using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Firo.Controllers
{
    [Route("[controller]")]
    public class FiroLikeDaemonCallbackController : Controller
    {
        private readonly EventAggregator _eventAggregator;

        public FiroLikeDaemonCallbackController(EventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
        }

        [HttpGet("block")]
        public IActionResult OnBlockNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new FiroEvent()
            {
                BlockHash = hash,
                CryptoCode = (cryptoCode ?? "FIRO").ToUpperInvariant()
            });
            return Ok();
        }

        [HttpGet("tx")]
        public IActionResult OnTransactionNotify(string hash, string cryptoCode)
        {
            _eventAggregator.Publish(new FiroEvent()
            {
                TransactionHash = hash,
                CryptoCode = (cryptoCode ?? "FIRO").ToUpperInvariant()
            });
            return Ok();
        }
    }
}
