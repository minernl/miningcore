using System.Data;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Persistence.Model;

namespace Miningcore.Payments
{
    public interface IPayoutScheme
    {
        Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig, IPayoutHandler payoutHandler, Block block, decimal blockReward);
    }
}
