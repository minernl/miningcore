using System.Collections.Generic;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Equihash.DaemonResponses
{
    public class EquihashBlockResponse
    {
        /// <summary>
        /// The preferred block version
        /// </summary>
        public uint Version { get; set; }

        /// <summary>
        /// The hash of current highest block
        /// </summary>
        public string PreviousBlockhash { get; set; }

        /// <summary>
        /// Maximum allowable input to coinbase transaction, including the generation award and transaction fees (in Satoshis)
        /// </summary>
        public long CoinbaseValue { get; set; }

        /// <summary>
        /// The hash target
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// A range of valid nonces
        /// </summary>
        public string NonceRange { get; set; }

        /// <summary>
        /// Current timestamp in seconds since epoch (Jan 1 1970 GMT)
        /// </summary>
        public uint CurTime { get; set; }

        /// <summary>
        /// Compressed target of next block
        /// </summary>
        public string Bits { get; set; }

        /// <summary>
        /// The height of the next block
        /// </summary>
        public uint Height { get; set; }

        /// <summary>
        /// Contents of non-coinbase transactions that should be included in the next block
        /// </summary>
        public EquihashBlockTransaction[] Transactions { get; set; }

        /// <summary>
        /// Data that should be included in the coinbase's scriptSig content
        /// </summary>
        public EquihashCoinbaseAux CoinbaseAux { get; set; }

        /// <summary>
        /// SegWit
        /// </summary>
        [JsonProperty("default_witness_commitment")]
        public string DefaultWitnessCommitment { get; set; }

        [JsonExtensionData]
        public IDictionary<string, object> Extra { get; set; }

        public string[] Capabilities { get; set; }

        [JsonProperty("coinbasetxn")]
        public EquihashCoinbaseTransaction CoinbaseTx { get; set; }

        public string LongPollId { get; set; }
        public ulong MinTime { get; set; }
        public ulong SigOpLimit { get; set; }
        public ulong SizeLimit { get; set; }
        public string[] Mutable { get; set; }

        public ZCashBlockSubsidy Subsidy { get; set; }

        [JsonProperty("finalsaplingroothash")]
        public string FinalSaplingRootHash { get; set; }
		
		[JsonProperty("solution")]
        public string Solution { get; set; }
    }
}
