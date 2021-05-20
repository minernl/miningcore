using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Miningcore.Blockchain.Ethereum
{
    public class EthereumConstants
    {
        public const ulong EpochLength = 30000;
        public const ulong CacheSizeForTesting = 1024;
        public const ulong DagSizeForTesting = 1024 * 32;
        public static BigInteger BigMaxValue = BigInteger.Pow(2, 256);
        public static double Pow2x32 = Math.Pow(2, 32);
        public static BigInteger BigPow2x32 = new BigInteger(Pow2x32);
        public const int AddressLength = 20;
        public const decimal Wei = 1000000000000000000;
        public static BigInteger WeiBig = new BigInteger(1000000000000000000);
        public const string EthereumStratumVersion = "EthereumStratum/1.0.0";
        public const decimal StaticTransactionFeeReserve = 0.0025m; // in ETH
        public const string BlockTypeUncle = "uncle";
        public static double StratumDiffFactor = 4294901760.0;
        public const int MinPayoutPeerCount = 1;

        public static readonly Regex ValidAddressPattern = new Regex("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
        public static readonly Regex ZeroHashPattern = new Regex("^0?x?0+$", RegexOptions.Compiled);
        public static readonly Regex NoncePattern = new Regex("^0x[0-9a-f]{16}$", RegexOptions.Compiled);
        public static readonly Regex HashPattern = new Regex("^0x[0-9a-f]{64}$", RegexOptions.Compiled);
        public static readonly Regex WorkerPattern = new Regex("^[0-9a-zA-Z-_]{1,8}$", RegexOptions.Compiled);

        public const ulong ByzantiumHardForkHeight = 4370000;
        public const ulong  ConstantinopleHardForkHeight = 7280000;
        public const decimal HomesteadBlockReward = 5.0m;
        public const decimal ByzantiumBlockReward = 3.0m;
        public const decimal ConstantinopleReward = 2.0m;
        public const decimal TestnetBlockReward = 3.0m;
        public const decimal ExpanseBlockReward = 8.0m;
        public const decimal EllaismBlockReward = 5.0m;
        public const decimal JoysBlockReward = 2.0m;

        public const int MinConfimations = 16;
    }
}
