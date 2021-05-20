
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Miningcore.Configuration;
using Miningcore.Extensions;
using NBitcoin.BouncyCastle.Math;

namespace Miningcore.Blockchain.Cryptonote
{
    public static class CryptonoteWalletCommands
    {
        public const string GetBalance = "get_balance";
        public const string GetAddress = "getaddress";
        public const string Transfer = "transfer";
        public const string TransferSplit = "transfer_split";
        public const string GetTransfers = "get_transfers";
        public const string SplitIntegratedAddress = "split_integrated_address";
        public const string Store = "store";
    }
}
