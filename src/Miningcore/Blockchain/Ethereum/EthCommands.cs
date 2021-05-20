using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Miningcore.Blockchain.Ethereum
{
    public static class EthCommands
    {
        public const string GetWork = "eth_getWork";
        public const string SubmitWork = "eth_submitWork";
        public const string Sign = "eth_sign";
        public const string GetNetVersion = "net_version";
        public const string GetClientVersion = "web3_clientVersion";
        public const string GetCoinbase = "eth_coinbase";
        public const string GetAccounts = "eth_accounts";
        public const string GetPeerCount = "net_peerCount";
        public const string GetSyncState = "eth_syncing";
        public const string GetBlockNumber = "eth_blockNumber";      // MinerNL added
        public const string GetBlockByNumber = "eth_getBlockByNumber";
        public const string GetBlockByHash = "eth_getBlockByHash";
        public const string GetUncleByBlockNumberAndIndex = "eth_getUncleByBlockNumberAndIndex";
        public const string GetTxReceipt = "eth_getTransactionReceipt";
        public const string SendTx = "eth_sendTransaction";
        public const string UnlockAccount = "personal_unlockAccount";
        public const string Subscribe = "eth_subscribe";

        public const string ParityVersion = "parity_versionInfo";
        public const string ParityChain = "parity_chain";
        public const string ParitySubscribe = "parity_subscribe";
    }
}
