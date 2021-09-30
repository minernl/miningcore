using Autofac;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api.Requests;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Payments;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Miningcore.Api.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("api/admin")]
    [ApiController]
    public class AdminApiController : ControllerBase
    {
        public AdminApiController(IComponentContext ctx)
        {
            gcStats = ctx.Resolve<AdminGcStats>();
            clusterConfig = ctx.Resolve<ClusterConfig>();
            pools = ctx.Resolve<ConcurrentDictionary<string, IMiningPool>>();
            cf = ctx.Resolve<IConnectionFactory>();
            paymentsRepo = ctx.Resolve<IPaymentRepository>();
            balanceRepo = ctx.Resolve<IBalanceRepository>();
            payoutManager = ctx.Resolve<PayoutManager>();
        }

        private readonly ClusterConfig clusterConfig;
        private readonly IConnectionFactory cf;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IBalanceRepository balanceRepo;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;
        private readonly PayoutManager payoutManager;

        private AdminGcStats gcStats;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        #region Actions

        [HttpGet("stats/gc")]
        public ActionResult<AdminGcStats> GetGcStats()
        {
            gcStats.GcGen0 = GC.CollectionCount(0);
            gcStats.GcGen1 = GC.CollectionCount(1);
            gcStats.GcGen2 = GC.CollectionCount(2);
            gcStats.MemAllocated = FormatUtil.FormatCapacity(GC.GetTotalMemory(false));

            return gcStats;
        }

        [HttpPost("forcegc")]
        public ActionResult<string> ForceGc()
        {
            GC.Collect(2, GCCollectionMode.Forced);
            return "Ok";
        }

        [HttpGet("pools/{poolId}/miners/{address}/getbalance")]
        public async Task<decimal> GetMinerBalanceAsync(string poolId, string address)
        {
            return await cf.Run(con => balanceRepo.GetBalanceAsync(con, poolId, address));
        }

        [HttpPost("pools/{poolId}/miners/{address}/forcePayout")]
        public async Task<string> ForcePayout(string poolId, string address)
        {
            logger.Info($"Forcing payout for {address}");
            try
            {
                if(string.IsNullOrEmpty(poolId))
                {
                    throw new ApiException($"Invalid pool id", HttpStatusCode.NotFound);
                }

                var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

                if(pool == null)
                {
                    throw new ApiException($"Pool {poolId} is not known", HttpStatusCode.NotFound);
                }

                return await payoutManager.PayoutSingleBalanceAsync(GetPool(poolId), address);
            }
            catch(Exception ex)
            {
                //rethrow as ApiException to be handled by ApiExceptionHandlingMiddleware
                throw new ApiException(ex.Message, HttpStatusCode.InternalServerError);
            }
        }

        #endregion // Actions

        private PoolConfig GetPool(string poolId)
        {
            if(string.IsNullOrEmpty(poolId))
                throw new ApiException($"Invalid pool id", HttpStatusCode.NotFound);

            var pool = clusterConfig.Pools.FirstOrDefault(x => x.Id == poolId && x.Enabled);

            if(pool == null)
                throw new ApiException($"Pool {poolId} is not known", HttpStatusCode.NotFound);

            return pool;
        }
    }
}
