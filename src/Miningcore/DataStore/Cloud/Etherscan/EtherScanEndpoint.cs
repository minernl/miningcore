using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Miningcore.Configuration;
using Miningcore.Util;
using NLog;

namespace Miningcore.DataStore.Cloud.EtherScan
{
    internal class EtherScanEndpoint : ApiEndpoint
    {
        private const string DateFormat = "yyyy-MM-dd";
        private readonly string apiKey;
        private readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public EtherScanEndpoint(ClusterConfig config)
        {
            if(config?.Pools == null) throw new ArgumentNullException(nameof(config));
            var ethPoolConfig = config.Pools.FirstOrDefault(p => p.Coin.Equals(CoinFamily.Ethereum.ToString(), StringComparison.OrdinalIgnoreCase));

            if(ethPoolConfig?.EtherScan == null) throw new ArgumentNullException(nameof(config));
            apiKey = ethPoolConfig.EtherScan.apiKey;
            Client.BaseAddress = new Uri(ethPoolConfig.EtherScan.apiUrl);
        }

        public async Task<DailyUncleBlkCount[]> GetDailyUncleBlockCount(DateTime start, DateTime end)
        {
            var url = $"?module=stats&action=dailyuncleblkcount&startdate={start.ToString(DateFormat)}&enddate={end.ToString(DateFormat)}" +
                      $"&sort=asc&apikey={apiKey}";

            var resp = await TelemetryUtil.TrackDependency(() => GetAsync<EtherScanResponse<DailyUncleBlkCount[]>>(url, new Dictionary<string, string>()
            {
                {WebConstants.HeaderAccept, WebConstants.ContentTypeText}
            }), DependencyType.EtherScan, nameof(GetDailyUncleBlockCount), $"st:{start},end:{end}");

            if(resp?.Status > 0) return resp.Result;

            logger.Error($"GetDailyUncleBlockCount failed. reason={resp?.Message}, status={resp?.Status}");
            return null;
        }

        public async Task<DailyUncleBlkCount[]> GetDailyUncleBlockCountForToday()
        {
            return await GetDailyUncleBlockCount(DateTime.Today.AddDays(-1), DateTime.Today);
        }

    }
}
