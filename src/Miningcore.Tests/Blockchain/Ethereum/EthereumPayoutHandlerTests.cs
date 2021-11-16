using Miningcore.Blockchain.Ethereum;
using Xunit;

namespace Miningcore.Tests.Blockchain.Ethereum
{
    public class EthereumPayoutHandlerTests : TestBase
    {
        public static readonly ulong TransferGas = 21000;
        public static readonly decimal DefaultMinimumPayout = (decimal)0.004;

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_IsZero_UseDefaultMinimumPayout()
        {
            decimal actualMinimumPayout = EthereumPayoutHandler.GetMinimumPayout(DefaultMinimumPayout, 120000000000, 0, TransferGas);
            Assert.Equal(DefaultMinimumPayout, actualMinimumPayout);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_FiftyPercent_MinimumPayout_Equals_TransactionCost()
        {
            // 50% = 1 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 50, 1);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 50, 1);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_TwentyPercent_MinimumPayout_Equals_4X_TransactionCost()
        {
            // 20% = 4 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 20, 4);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 20, 4);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_TwentyFivePercent_MinimumPayout_Equals_3X_TransactionCost()
        {
            // 25% = 3 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 25, 3);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 25, 3);
        }

        [Fact]
        public void GetMinimumPayout_GasDeductionPercentage_Is_FourtyPercent_MinimumPayout_Equals_1_5X_TransactionCost()
        {
            // 40% = 1.5 to 1 ratio
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(120000000000, 40, (decimal)1.5);

            // This ratio should hold true regardless of gas price
            GetMinimumPayout_GasDeductionPercentage_CheckRatio(113000000000, 40, (decimal)1.5);
        }

        private void GetMinimumPayout_GasDeductionPercentage_CheckRatio(ulong baseFeePerGas, decimal gasDeductionPercentage, decimal multiplier)
        {
            decimal actualMinimumPayout = EthereumPayoutHandler.GetMinimumPayout(DefaultMinimumPayout, baseFeePerGas, gasDeductionPercentage, TransferGas);
            decimal expectedTransactionCost = EthereumPayoutHandler.GetTransactionCost(baseFeePerGas, TransferGas);
            Assert.Equal(multiplier * expectedTransactionCost, actualMinimumPayout);
        }
    }
}