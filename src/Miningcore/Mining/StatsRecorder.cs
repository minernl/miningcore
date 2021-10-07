using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.ApplicationInsights;
using Miningcore.Configuration;
using Miningcore.Contracts;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Time;
using Miningcore.Util;
using NLog;
using Polly;

namespace Miningcore.Mining
{
    public class StatsRecorder
    {
        public StatsRecorder(IComponentContext ctx,
            IMasterClock clock,
            IConnectionFactory cf,
            IMessageBus messageBus,
            IMapper mapper,
            IShareRepository shareRepo,
            IStatsRepository statsRepo)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(clock, nameof(clock));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));

            this.ctx = ctx;
            this.clock = clock;
            this.cf = cf;
            this.mapper = mapper;
            this.messageBus = messageBus;
            this.shareRepo = shareRepo;
            this.statsRepo = statsRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IMasterClock clock;
        private readonly IStatsRepository statsRepo;
        private readonly IConnectionFactory cf;
        private readonly IMapper mapper;
        private readonly IMessageBus messageBus;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly CancellationTokenSource cts = new();
        private readonly ConcurrentDictionary<string, IMiningPool> pools = new();

        // MinerNL Stats calculation variables
        private const int StatsUpdateInterval = 60;       // seconds. Default setting if not in config.json
        private const int HashrateCalculationWindow = 10; // minutes. Default setting if not in config.json
        private const int StatsCleanupInterval = 24;      // hours.   Default setting if not in config.json
        private const int StatsDbCleanupHistory = 24;     // hours.   Default setting if not in config.json
        private int statsUpdateInterval;
        private int hashrateCalculationWindow;
        private int statsCleanupInterval;
        // MinerNL end

        private ClusterConfig clusterConfig;
        private const int RetryCount = 4;
        private IAsyncPolicy readFaultPolicy;
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        #region API-Surface

        public void Configure(ClusterConfig clusterConfig)
        {
            this.clusterConfig = clusterConfig;
        }

        public void AttachPool(IMiningPool pool)
        {
            pools[pool.Config.Id] = pool;
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                Logger.Info(() => "Starting Pool Stats Service");

                // warm-up delay
                await Task.Delay(TimeSpan.FromSeconds(10));

                // MinerNL read variables from config.json
                // Stats broadcast interval
                statsUpdateInterval = clusterConfig.Statistics?.StatsUpdateInterval ?? StatsUpdateInterval;
                if(statsUpdateInterval == 0)
                {
                    statsUpdateInterval = StatsUpdateInterval;
                    Logger.Warn(() => $"statistics -> statsUpdateInterval not valid in config.json. using default : {statsUpdateInterval} seconds");
                }

                // Stats calculation window
                hashrateCalculationWindow = clusterConfig.Statistics?.HashrateCalculationWindow ?? HashrateCalculationWindow;
                if(hashrateCalculationWindow == 0)
                {
                    hashrateCalculationWindow = HashrateCalculationWindow;
                    Logger.Warn(() => $"statistics -> hashrateCalculationWindow not valid in config.json. using default : {hashrateCalculationWindow} minutes");
                }

                // Stats DB cleanup interval
                statsCleanupInterval = clusterConfig.Statistics?.StatsCleanupInterval ?? StatsCleanupInterval;
                if(statsCleanupInterval == 0)
                {
                    statsCleanupInterval = StatsCleanupInterval;
                    Logger.Warn(() => $"statistics -> statsCleanupInterval not valid in config.json. using default : {statsCleanupInterval} minutes");
                }

                // Set DB Cleanup time
                var performStatsGcInterval = DateTime.UtcNow;
                // MinerNL end

                while(!cts.IsCancellationRequested)
                {
                    try
                    {
                        await UpdatePoolHashratesAsync();    // Pool stats update

                        // MinerNL - Stats cleanup at StatsCleanupInterval
                        Logger.Info(() => $"Next Stats DB cleanup at {performStatsGcInterval.ToLocalTime()}");
                        if(clock.UtcNow >= performStatsGcInterval)
                        {
                            await PerformStatsGcAsync();
                            performStatsGcInterval = DateTime.UtcNow.AddHours(statsCleanupInterval);
                        }
                        // MinerNL end
                    }

                    catch(Exception ex)
                    {
                        Logger.Error(ex);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(statsUpdateInterval), cts.Token);

                }
            });
            Logger.Info(() => "Pool Stats Online");
        }

        public void Stop()
        {
            Logger.Info(() => "Pool Stats Stopping ..");

            cts.Cancel();

            Logger.Info(() => "Pool Stats Stopped");
        }

        #endregion // API-Surface

        private async Task UpdatePoolHashratesAsync()
        {
            var currentTimeUtc = clock.UtcNow;
            var timeFrom = currentTimeUtc.AddMinutes(-hashrateCalculationWindow);
            var statsWindowsTimeFrame = TimeSpan.FromMinutes(hashrateCalculationWindow);

            Logger.Info(() => "--------------------------------------------------------------------------------------------");
            Logger.Info(() => $"Stats Update Interval  : {statsUpdateInterval} seconds");
            Logger.Info(() => $"Hashrate Calc Windows  : {hashrateCalculationWindow} minutes");
            Logger.Info(() => $"Current Time UTC       : {currentTimeUtc}");
            Logger.Info(() => $"Getting Stats from UTC : {timeFrom}");
            Logger.Info(() => "--------------------------------------------------------------------------------------------");
            // MinerNL

            var stats = new MinerWorkerPerformanceStats
            {
                Created = currentTimeUtc      // MinerNL Time to UTC
            };

            foreach(var poolId in pools.Keys)
            {
                var stopWatch = Stopwatch.StartNew();
                stats.PoolId = poolId;

                Logger.Info(() => $"[{poolId}] Updating Statistics for pool");

                var pool = pools[poolId];

                // fetch stats from DB for the last X minutes
                // MinerNL get stats
                var result = await readFaultPolicy.ExecuteAsync(() => cf.Run(con => shareRepo.GetHashAccumulationBetweenAcceptedAsync(con, poolId, timeFrom, currentTimeUtc)));

                var byMiner = result.GroupBy(x => x.Miner).ToArray();

                // calculate & update pool, connected workers & hashrates
                if(result.Length > 0)
                {
                    // pool miners 
                    pool.PoolStats.ConnectedMiners = byMiner.Length; // update connected miners

                    // Stats calc windows
                    var timeFrameBeforeFirstShare = ((result.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                    var timeFrameAfterLastShare = ((currentTimeUtc - result.Max(x => x.LastShare)).TotalSeconds);
                    var timeFrameFirstLastShare = (statsWindowsTimeFrame.TotalSeconds - timeFrameBeforeFirstShare - timeFrameAfterLastShare);

                    //var poolHashTimeFrame         = Math.Floor(TimeFrameFirstLastShare + (TimeFrameBeforeFirstShare / 3) + (TimeFrameAfterLastShare * 3)) ;

                    var poolHashTimeFrame = statsWindowsTimeFrame.TotalSeconds;

                    // pool hashrate
                    var poolHashesAccumulated = result.Sum(x => x.Sum);
                    var poolHashrate = pool.HashrateFromShares(poolHashesAccumulated, poolHashTimeFrame);
                    poolHashrate = Math.Floor(poolHashrate);
                    pool.PoolStats.PoolHashrate = poolHashrate;

                    // pool shares
                    var poolHashesCountAccumulated = result.Sum(x => x.Count);
                    pool.PoolStats.SharesPerSecond = (int) (poolHashesCountAccumulated / poolHashTimeFrame);

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, poolHashrate);
                    // MinerNL end
                }
                else
                {
                    // reset
                    pool.PoolStats.ConnectedMiners = 0;
                    pool.PoolStats.PoolHashrate = 0;
                    pool.PoolStats.SharesPerSecond = 0;

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, 0);

                    Logger.Info(() => $"[{poolId}] Reset performance stats for pool");
                }
                Logger.Info(() => $"[{poolId}] Connected Miners : {pool.PoolStats.ConnectedMiners} miners");
                Logger.Info(() => $"[{poolId}] Pool hashrate    : {pool.PoolStats.PoolHashrate} hashes/sec");
                Logger.Info(() => $"[{poolId}] Pool shares      : {pool.PoolStats.SharesPerSecond} shares/sec");

                var tc = TelemetryUtil.GetTelemetryClient();
                if(null != tc)
                {
                    tc.GetMetric("PoolHashRate_" + poolId).TrackValue(pool.PoolStats.PoolHashrate);
                    tc.GetMetric("PoolMinerCount_" + poolId).TrackValue(pool.PoolStats.ConnectedMiners);
                }

                // persist. Save pool stats in DB.
                await cf.RunTx(async (con, tx) =>
                {
                    var mapped = new Persistence.Model.PoolStats
                    {
                        PoolId = poolId,
                        Created = currentTimeUtc   // MinerNL time to UTC
                    };

                    mapper.Map(pool.PoolStats, mapped);
                    mapper.Map(pool.NetworkStats, mapped);

                    await statsRepo.InsertPoolStatsAsync(con, tx, mapped);
                });

                // retrieve most recent miner/worker hashrate sample, if non-zero
                var previousMinerWorkerHashrates = await cf.Run(async (con) => await statsRepo.GetPoolMinerWorkerHashratesAsync(con, poolId));

                string BuildKey(string miner, string worker = null)
                {
                    return !string.IsNullOrEmpty(worker) ? $"{miner}:{worker}" : miner;
                }

                var previousNonZeroMinerWorkers = new HashSet<string>(previousMinerWorkerHashrates.Select(x => BuildKey(x.Miner, x.Worker)));
                var currentNonZeroMinerWorkers = new HashSet<string>();

                if(result.Length == 0)
                {
                    // identify and reset "orphaned" miner stats
                    var orphanedHashrateForMinerWorker = previousNonZeroMinerWorkers.Except(currentNonZeroMinerWorkers).ToArray();

                    await cf.RunTx(async (con, tx) =>
                    {
                        // reset
                        stats.Hashrate = 0;
                        stats.SharesPerSecond = 0;

                        foreach(var item in orphanedHashrateForMinerWorker)
                        {
                            var parts = item.Split(":");
                            var miner = parts[0];
                            var worker = parts.Length > 1 ? parts[1] : null;

                            stats.Miner = parts[0];
                            stats.Worker = worker;

                            // persist
                            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                            // broadcast
                            messageBus.NotifyHashrateUpdated(pool.Config.Id, 0, stats.Miner, stats.Worker);

                            if(string.IsNullOrEmpty(stats.Worker))
                                Logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}");
                            else
                                Logger.Info(() => $"[{poolId}] Reset performance stats for miner {stats.Miner}.{stats.Worker}");
                        }
                    });
                    stopWatch.Stop();
                    Logger.Info(() => $"[{poolId}] Statistics updated in {stopWatch.Elapsed.Seconds}s");
                    Logger.Info(() => "--------------------------------------------");
                    continue;
                };

                // MinerNL calculate & update miner, worker hashrates
                foreach(var minerHashes in byMiner)
                {
                    double minerTotalHashrate = 0;

                    await cf.RunTx(async (con, tx) =>
                    {
                        stats.Miner = minerHashes.Key;

                        // book keeping
                        currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner));

                        foreach(var item in minerHashes)
                        {
                            // set default values
                            double minerHashrate = 0;
                            stats.Worker = "Default_Miner";
                            stats.Hashrate = 0;
                            stats.SharesPerSecond = 0;

                            // miner stats calculation windows
                            var timeFrameBeforeFirstShare = ((minerHashes.Min(x => x.FirstShare) - timeFrom).TotalSeconds);
                            var timeFrameAfterLastShare = ((currentTimeUtc - minerHashes.Max(x => x.LastShare)).TotalSeconds);
                            var timeFrameFirstLastShare = (statsWindowsTimeFrame.TotalSeconds - timeFrameBeforeFirstShare - timeFrameAfterLastShare);

                            var minerHashTimeFrame = statsWindowsTimeFrame.TotalSeconds;

                            if(timeFrameBeforeFirstShare >= (statsWindowsTimeFrame.TotalSeconds * 0.1))
                                minerHashTimeFrame = Math.Floor(statsWindowsTimeFrame.TotalSeconds - timeFrameBeforeFirstShare);

                            if(timeFrameAfterLastShare >= (statsWindowsTimeFrame.TotalSeconds * 0.1))
                                minerHashTimeFrame = Math.Floor(statsWindowsTimeFrame.TotalSeconds + timeFrameAfterLastShare);

                            if((timeFrameBeforeFirstShare >= (statsWindowsTimeFrame.TotalSeconds * 0.1)) &&
                               (timeFrameAfterLastShare >= (statsWindowsTimeFrame.TotalSeconds * 0.1)))
                                minerHashTimeFrame = (statsWindowsTimeFrame.TotalSeconds - timeFrameBeforeFirstShare + timeFrameAfterLastShare);

                            // let's not update hashrate if minerHashTimeFrame is too small, less than 10% of StatsWindowsTimeFrame. Otherwise, hashrate will be too high.
                            if(minerHashTimeFrame < statsWindowsTimeFrame.TotalSeconds * 0.1)
                            {
                                Logger.Debug(() => $"MinerHashTimeFrame is too small. Skip calculate minerHashrate. [{poolId}] Miner: {stats.Miner}");
                                continue;
                            };

                            // logger.Info(() => $"[{poolId}] StatsWindowsTimeFrame : {StatsWindowsTimeFrame.TotalSeconds} | minerHashTimeFrame : {minerHashTimeFrame} |  TimeFrameFirstLastShare : {TimeFrameFirstLastShare} | TimeFrameBeforeFirstShare: {TimeFrameBeforeFirstShare} | TimeFrameAfterLastShare: {TimeFrameAfterLastShare}");

                            // calculate miner/worker stats
                            minerHashrate = pool.HashrateFromShares(item.Sum, minerHashTimeFrame);
                            minerHashrate = Math.Floor(minerHashrate);
                            minerTotalHashrate += minerHashrate;
                            stats.Hashrate = minerHashrate;

                            if(item.Worker != null)
                            {
                                stats.Worker = item.Worker;
                            }

                            stats.SharesPerSecond = Math.Round(((double) item.Count / minerHashTimeFrame), 3);

                            // persist. Save miner stats in DB.
                            await statsRepo.InsertMinerWorkerPerformanceStatsAsync(con, tx, stats);

                            // broadcast
                            messageBus.NotifyHashrateUpdated(pool.Config.Id, minerHashrate, stats.Miner, stats.Worker);
                            Logger.Debug(() => $"[{poolId}] Miner: {stats.Miner}.{stats.Worker} | Hashrate: {minerHashrate} " +
                                              $"| HashTimeFrame : {minerHashTimeFrame} | Shares per sec: {stats.SharesPerSecond}");
                            // book keeping
                            currentNonZeroMinerWorkers.Add(BuildKey(stats.Miner, stats.Worker));
                        }
                    });

                    messageBus.NotifyHashrateUpdated(pool.Config.Id, minerTotalHashrate, stats.Miner, null);
                    Logger.Debug(() => $"[{poolId}] Total miner hashrate: {stats.Miner} | {minerTotalHashrate}");
                }
                // MinerNL end calculate & update miner, worker hashrates
                stopWatch.Stop();
                Logger.Info(() => $"[{poolId}] Statistics updated in {stopWatch.Elapsed.Seconds}s");
                Logger.Info(() => "--------------------------------------------");
            }
        }

        private async Task PerformStatsGcAsync()
        {
            Logger.Info(() => $"Performing stats DB cleanup");

            await cf.Run(async con =>
            {
                // MinerNL Stats cleanup
                var statsDbCleanupHistory = clusterConfig.Statistics?.StatsDBCleanupHistory ?? StatsDbCleanupHistory;
                if(statsDbCleanupHistory == 0)
                {
                    statsDbCleanupHistory = StatsDbCleanupHistory;
                    Logger.Info(() => $"statistics -> statsDBCleanupHistory not valid in config.json. using default : {statsDbCleanupHistory} hours");
                }

                Logger.Info(() => $"Removing all stats older then {statsDbCleanupHistory} hours");

                var cutOff = DateTime.UtcNow.AddHours(-statsDbCleanupHistory);
                // MinerNL end

                var rowCount = await statsRepo.DeletePoolStatsBeforeAsync(con, cutOff);
                Logger.Info(() => $"Deleted {rowCount} old poolstats records");

                rowCount = await statsRepo.DeleteMinerStatsBeforeAsync(con, cutOff);
                Logger.Info(() => $"Deleted {rowCount} old minerstats records");
            });

            Logger.Info(() => $"Stats cleanup DB complete");
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .RetryAsync(RetryCount, OnPolicyRetry);

            readFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            Logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
