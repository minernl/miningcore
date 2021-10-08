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
            apiKey = ethPoolConfig.EtherScan.ApiKey;
            Client.BaseAddress = new Uri(ethPoolConfig.EtherScan.ApiUrl);
        }

        public async Task<DailyBlkCount[]> GetDailyBlockCount(DateTime start, DateTime end)
        {
            var url = $"?module=stats&action=dailyblkcount&startdate={start.ToString(DateFormat)}&enddate={end.ToString(DateFormat)}" +
                      $"&sort=desc&apikey={apiKey}";

            var resp = await TelemetryUtil.TrackDependency(() => GetAsync<EtherScanResponse<DailyBlkCount[]>>(url, new Dictionary<string, string>()
            {
                {WebConstants.HeaderAccept, WebConstants.ContentTypeText}
            }), DependencyType.EtherScan, nameof(GetDailyBlockCount), $"st:{start},end:{end}");

            if(resp?.Status > 0) return resp.Result;

            logger.Error($"GetDailyUncleBlockCount failed. reason={resp?.Message}, status={resp?.Status}");
            return null;
        }

        public async Task<DailyBlkCount[]> GetDailyBlockCount(int lookBackDays)
        {
            return await GetDailyBlockCount(DateTime.Today.AddDays(-lookBackDays), DateTime.Today);
        }

        public async Task<DailyAverageBlockTime[]> GetDailyAverageBlockTime(DateTime start, DateTime end)
        {
            var url = $"?module=stats&action=dailyavgblocktime&startdate={start.ToString(DateFormat)}&enddate={end.ToString(DateFormat)}" +
                      $"&sort=desc&apikey={apiKey}";

            var resp = await TelemetryUtil.TrackDependency(() => GetAsync<EtherScanResponse<DailyAverageBlockTime[]>>(url, new Dictionary<string, string>()
            {
                {WebConstants.HeaderAccept, WebConstants.ContentTypeText}
            }), DependencyType.EtherScan, nameof(GetDailyAverageBlockTime), $"st:{start},end:{end}");

            if(resp?.Status > 0) return resp.Result;

            logger.Error($"GetDailyAverageBlockTime failed. reason={resp?.Message}, status={resp?.Status}");
            return null;
        }

        public async Task<DailyAverageBlockTime[]> GetDailyAverageBlockTime(int lookBackDays)
        {
            return await GetDailyAverageBlockTime(DateTime.Today.AddDays(lookBackDays), DateTime.Today);
        }

    }
}
