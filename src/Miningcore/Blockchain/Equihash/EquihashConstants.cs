using System.Globalization;
using NBitcoin.Zcash;

namespace Miningcore.Blockchain.Equihash
{
    public class EquihashConstants
    {
        public const int TargetPaddingLength = 32;

        public static readonly System.Numerics.BigInteger ZCashDiff1b =
            System.Numerics.BigInteger.Parse("0007ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.HexNumber);
    }
}
