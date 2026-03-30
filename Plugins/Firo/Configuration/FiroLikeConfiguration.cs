using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Firo.Configuration
{
    public class FiroLikeConfiguration
    {
        public Dictionary<string, FiroLikeConfigurationItem> FiroLikeConfigurationItems { get; set; } = [];
    }

    public class FiroLikeConfigurationItem
    {
        public Uri DaemonRpcUri { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
