using System.Globalization;
using NBitcoin.Zcash;

namespace Miningcore.Blockchain.Equihash
{
    public enum ZOperationStatus
    {
        Queued,
        Executing,
        Success,
        Cancelled,
        Failed
    }
}
