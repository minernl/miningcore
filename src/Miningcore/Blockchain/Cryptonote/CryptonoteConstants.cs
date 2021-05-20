
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Miningcore.Configuration;
using Miningcore.Extensions;
using NBitcoin.BouncyCastle.Math;

namespace Miningcore.Blockchain.Cryptonote
{
    public class CryptonoteConstants
    {
        public const string WalletDaemonCategory = "wallet";

        public const string DaemonRpcLocation = "json_rpc";
        public const string DaemonRpcDigestAuthRealm = "monero_rpc";
        public const int MoneroRpcMethodNotFound = -32601;
        public const char MainNetAddressPrefix = '4';
        public const char TestNetAddressPrefix = '9';
        public const int PaymentIdHexLength = 64;
        public static readonly Regex RegexValidNonce = new Regex("^[0-9a-f]{8}$", RegexOptions.Compiled);

        public static readonly BigInteger Diff1 = new BigInteger("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16);
        public static readonly System.Numerics.BigInteger Diff1b = System.Numerics.BigInteger.Parse("00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

#if DEBUG
        public const int PayoutMinBlockConfirmations = 2;
#else
        public const int PayoutMinBlockConfirmations = 60;
#endif

        public const int InstanceIdSize = 3;
        public const int ExtraNonceSize = 4;

        // NOTE: for whatever strange reason only reserved_size -1 can be used,
        // the LAST byte MUST be zero or nothing works
        public const int ReserveSize = ExtraNonceSize + InstanceIdSize + 1;

        // Offset to nonce in block blob
        public const int BlobNonceOffset = 39;

        public const decimal StaticTransactionFeeReserve = 0.03m; // in monero
    }
}
