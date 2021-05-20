using System.Collections.Generic;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Equihash.DaemonResponses
{
    public class EquihashBlockTransaction
    {
        /// <summary>
        /// transaction data encoded in hexadecimal (byte-for-byte)
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// transaction id encoded in little-endian hexadecimal
        /// </summary>
        public string TxId { get; set; }

        /// <summary>
        /// hash encoded in little-endian hexadecimal (including witness data)
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// The amount of the fee in BTC
        /// </summary>
        public decimal Fee { get; set; }
    }
}
