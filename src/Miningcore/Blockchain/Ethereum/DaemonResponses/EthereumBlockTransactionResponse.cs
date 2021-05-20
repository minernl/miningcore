using System.Numerics;
using Miningcore.Serialization;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Ethereum.DaemonResponses
{
    public class EthereumBlockTransactionResponse
    {
        /// <summary>
        /// 32 Bytes - hash of the transaction.
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// the number of transactions made by the sender prior to this one.
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong>))]
        public ulong Nonce { get; set; }

        /// <summary>
        /// 32 Bytes - hash of the block where this transaction was in. null when its pending.
        /// </summary>
        public string BlockHash { get; set; }

        /// <summary>
        /// block number where this transaction was in. null when its pending.
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong? BlockNumber { get; set; }

        /// <summary>
        /// integer of the transactions index position in the block. null when its pending.
        /// </summary>
        [JsonProperty("transactionIndex")]
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<ulong?>))]
        public ulong? Index { get; set; }

        /// <summary>
        /// address of the sender.
        /// </summary>
        public string From { get; set; }

        /// <summary>
        /// address of the receiver. null when its a contract creation transaction.
        /// </summary>
        public string To { get; set; }

        /// <summary>
        /// Value transferred in Wei
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger Value { get; set; }

        /// <summary>
        /// gas price provided by the sender in Wei.
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger GasPrice { get; set; }

        /// <summary>
        /// gas provided by the sender
        /// </summary>
        [JsonConverter(typeof(HexToIntegralTypeJsonConverter<BigInteger>))]
        public BigInteger Gas { get; set; }

        /// <summary>
        /// the data send along with the transaction.
        /// </summary>
        public string Input { get; set; }
    }
}
