
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Miningcore.Configuration;
using Miningcore.Extensions;
using NBitcoin.BouncyCastle.Math;

namespace Miningcore.Blockchain.Cryptonote
{
    public static class CryptonoteCommands
    {
        public const string GetInfo = "get_info";
        public const string GetBlockTemplate = "getblocktemplate";
        public const string SubmitBlock = "submitblock";
        public const string GetBlockHeaderByHash = "getblockheaderbyhash";
        public const string GetBlockHeaderByHeight = "getblockheaderbyheight";
    }
}
