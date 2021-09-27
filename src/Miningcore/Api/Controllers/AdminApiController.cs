using Autofac;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miningcore.Api.Requests;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Mining;
using Miningcore.Persistence;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using System;
using System.Collections.Concurrent;
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
        }

        private readonly ClusterConfig clusterConfig;
        private readonly IConnectionFactory cf;
        private readonly IPaymentRepository paymentsRepo;
        private readonly IBalanceRepository balanceRepo;
        private readonly ConcurrentDictionary<string, IMiningPool> pools;

        private AdminGcStats gcStats;

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

        #endregion // Actions
    }
}
