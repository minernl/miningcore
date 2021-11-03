using Nethereum.Web3;

namespace Miningcore.Persistence.Model
{
    public class TransactionReceipt
    {
        public TransactionReceipt(Nethereum.RPC.Eth.DTOs.TransactionReceipt tx)
        {
            TransactionHash = tx.TransactionHash;
            EffectiveGasPrice = Web3.Convert.FromWei(tx.EffectiveGasPrice);
            GasUsed = Web3.Convert.FromWei(tx.GasUsed);
        }

        public TransactionReceipt(string id)
        {
            TransactionHash = id;
        }

        public string TransactionHash { get; set; }
        public decimal GasUsed { get; set; }
        public decimal EffectiveGasPrice { get; set; }
    }
}
