# BTCPay Server Firo Spark Plugin

A BTCPay Server plugin that enables merchants to accept Firo payments using Spark addresses.

## Features

- **Spark Address per Invoice**: Generates a unique Spark address for each invoice/payment request
- **Privacy**: Accepts payments from both Spark-to-Spark spends and transparent-to-Spark mints
- **Multi-transaction Support**: Tracks partial payments across multiple transactions to the same Spark address
- **Confirmation Tracking**: Configurable confirmation thresholds with speed policy support
- **Real-time Detection**: Supports block and transaction notifications via HTTP callbacks

## Requirements

- BTCPay Server >= 2.1.0
- Firo daemon (firod) with wallet enabled and Spark support
- .NET 8.0 SDK (for building)

## Configuration

Set the following environment variables for BTCPay Server:

| Variable | Description | Required |
|----------|-------------|----------|
| `BTCPAY_FIRO_DAEMON_URI` | Firo daemon RPC endpoint (e.g., `http://localhost:8888`) | Yes |
| `BTCPAY_FIRO_DAEMON_USERNAME` | RPC username | No |
| `BTCPAY_FIRO_DAEMON_PASSWORD` | RPC password | No |

## Firo Daemon Configuration

Add the following to your `firo.conf` to enable real-time payment notifications:

```ini
# RPC settings
server=1
rpcuser=yourusername
rpcpassword=yourpassword
rpcport=8888
rpcallowip=127.0.0.1

# Notification callbacks to BTCPay Server
blocknotify=curl -s http://your-btcpay-server/FiroLikeDaemonCallback/block?hash=%s&cryptoCode=FIRO
walletnotify=curl -s http://your-btcpay-server/FiroLikeDaemonCallback/tx?hash=%s&cryptoCode=FIRO
```

## Building

```bash
git submodule update --init --recursive
dotnet build
```

## Architecture

This plugin follows the same architecture as the [Monero BTCPay plugin](https://github.com/btcpayserver/btcpayserver-monero-plugin), adapted for Firo's Bitcoin-style JSON-RPC and Spark privacy protocol.

### Key Flows

**Invoice Creation:**
1. Calls `getnewsparkaddress` to generate a unique Spark address
2. Calls `getallsparkaddresses` to determine the diversifier (address index) for tracking
3. Stores address + diversifier in the invoice payment details

**Payment Detection:**
1. Block/transaction notifications trigger payment checking
2. Calls `listsparkmints` to get all Spark mints in the wallet
3. Matches mint diversifiers to tracked invoice diversifiers
4. Calls `gettransaction` for each matching mint to get confirmation counts
5. Creates/updates payment entities in BTCPay Server

**Health Monitoring:**
1. Periodically polls `getblockchaininfo` for sync status
2. Checks wallet availability via `getwalletinfo`

### Firo Spark RPC Commands Used

| Command | Purpose |
|---------|---------|
| `getnewsparkaddress` | Generate new Spark address per invoice |
| `getallsparkaddresses` | Map addresses to diversifiers |
| `listsparkmints` | List all Spark mints for payment matching |
| `gettransaction` | Get transaction confirmation count |
| `getblockchaininfo` | Check daemon sync status |
| `getwalletinfo` | Check wallet availability |
| `estimatesmartfee` | Estimate transaction fees |

## License

MIT
