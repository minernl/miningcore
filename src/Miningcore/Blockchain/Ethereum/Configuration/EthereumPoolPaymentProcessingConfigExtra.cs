
namespace Miningcore.Blockchain.Ethereum.Configuration
{
    public class EthereumPoolPaymentProcessingConfigExtra
    {
        /// <summary>
        /// Password of the daemons wallet (needed for processing payouts)
        /// </summary>
        public string CoinbasePassword { get; set; }

        /// <summary>
        /// True to exempt transaction fees from miner rewards
        /// </summary>
        public bool KeepTransactionFees { get; set; }

        /// <summary>
        /// True to exempt uncle rewards from miner rewards
        /// </summary>
        public bool KeepUncles { get; set; }
    }
}
