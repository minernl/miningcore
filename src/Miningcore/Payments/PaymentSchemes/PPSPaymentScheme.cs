using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ApplicationInsights;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using Newtonsoft.Json;
using NLog;
using Polly;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments.PaymentSchemes
{
    /// <summary>
    /// PPS payout scheme implementation  
    /// </summary>
    public class PPSPaymentScheme : IPayoutScheme
    {
        public PPSPaymentScheme(IConnectionFactory cf,
            IShareRepository shareRepo,
            IStatsRepository statsRepo,
            IMapper mapper,
            IBlockRepository blockRepo,
            IBalanceRepository balanceRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));

            this.cf = cf;
            this.shareRepo = shareRepo;
            this.blockRepo = blockRepo;
            this.balanceRepo = balanceRepo;
            this.statsRepo = statsRepo;
            this.mapper = mapper;

            BuildFaultHandlingPolicy();
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private readonly IStatsRepository statsRepo;
        private readonly IMapper mapper;
        private static readonly ILogger logger = LogManager.GetLogger("PPS Payment", typeof(PPSPaymentScheme));
        private static IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());

        private const int RetryCount = 4;
        private const decimal RECEPIENT_SHARE = 0.85m;
        private const int SIXTY = 60;
        private const int TWENTY_FOUR_HRS = 24;
        private const String BLOCK_REWARD = "blockReward";
        private const String DATE_FORMAT = "yyyy-MM-dd";
        private const String ACCEPT_TEXT_HTML = "text/html";
        private String URL_PARAMETER_FORMAT = "?module=stats&action=dailyblkcount&startdate={0}&enddate={1}&sort=asc&apikey={2}";
        private Policy shareReadFaultPolicy;

        private class Config
        {
            public decimal Factor { get; set; }
        }

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig, ClusterConfig clusterConfig,
            IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            var payoutConfig = poolConfig.PaymentProcessing.PayoutSchemeConfig;

            object blockRewardInCache = cache.Get(BLOCK_REWARD);
            if(blockRewardInCache == null)
            {
                blockRewardInCache = await GetBlockReward(poolConfig);
            }

            decimal blockData = await CalculateBlockData((decimal) blockRewardInCache, poolConfig, clusterConfig);
            // calculate rewards
            var shares = new Dictionary<string, double>();
            var rewards = new Dictionary<string, decimal>();
            var paidUntil = DateTime.UtcNow.AddSeconds(-5);
            var shareCutOffDate = CalculateRewards(poolConfig, shares, rewards, blockData, paidUntil);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if (amount > 0)
                {
                    logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) shares for block {block?.BlockHeight}");

                    await TelemetryUtil.TrackDependency(
                            () => balanceRepo.AddAmountAsync(con, tx, poolConfig.Id, address, amount, $"Reward for {FormatUtil.FormatQuantity(shares[address])} shares for block {block?.BlockHeight}"),
                            DependencyType.Sql, "AddBalanceAmount", "AddBalanceAmount");
                }
            }

            // delete discarded shares
            //await TelemetryUtil.TrackDependency(() => shareRepo.DeleteSharesBeforeCreatedAsync(con, null, poolConfig.Id, shareCutOffDate.Value),
            //DependencyType.Sql, "DeleteOldShares", "DeleteOldShares");

            // diagnostics
            var totalShareCount = shares.Values.ToList().Sum(x => new decimal(x));
            var totalRewards = rewards.Values.ToList().Sum(x => x);

            if(totalRewards > 0)
                logger.Info(() => $"{FormatUtil.FormatQuantity((double) totalShareCount)} ({Math.Round(totalShareCount, 2)}) shares contributed to a total payout of {payoutHandler.FormatAmount(totalRewards)} ({totalRewards / blockReward * 100:0.00}% of block reward) to {rewards.Keys.Count} addresses");

            return;
        }

        private async Task<decimal> GetBlockReward(PoolConfig poolConfig)
        {
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(poolConfig.EtherScan.apiUrl);
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(ACCEPT_TEXT_HTML));
            var yesterdayDate = DateTime.Today.AddDays(-1).ToString(DATE_FORMAT);
            var todayDate = DateTime.Today.ToString(DATE_FORMAT);
            String requestUri = String.Format(URL_PARAMETER_FORMAT, yesterdayDate, todayDate, poolConfig.EtherScan.apiKey);
            HttpResponseMessage response = client.GetAsync(requestUri).Result;
            decimal blockReward;
            if(response.IsSuccessStatusCode)
            {
                var dataObjects = await response.Content.ReadAsStringAsync();

                dynamic desrializedObject = JsonConvert.DeserializeObject(dataObjects);
                dynamic result = desrializedObject.result[0];
                decimal blockCount = result.blockCount;
                decimal blockRewardsInEth = result.blockRewards_Eth;
                blockReward = blockRewardsInEth / blockCount;

                //Add blockReward to cache and set cache data expiration to 24 hours
                logger.Info(() => $"Network Block Reward : {blockReward}");
                cache.Set(BLOCK_REWARD, blockReward, TimeSpan.FromHours(TWENTY_FOUR_HRS));
            }
            else
            {
                throw new Exception($"Get Block reward info request failed with status code : {response.StatusCode}");
            }
            return blockReward;
        }

        private async Task<decimal> CalculateBlockData(decimal blockRewardInEth, PoolConfig poolConfig, ClusterConfig clusterConfig)
        {
            var stats = await cf.Run(con => statsRepo.GetLastPoolStatsAsync(con, poolConfig.Id));
            PoolStats poolStats = new PoolStats();
            BlockchainStats blockchainStats = null;
            if(stats != null)
            {
                poolStats = mapper.Map<PoolStats>(stats);
                blockchainStats = mapper.Map<BlockchainStats>(stats);
            }

            double networkHashRate = blockchainStats.NetworkHashrate;
            double poolHashRate = poolStats.PoolHashrate;

            if(networkHashRate == 0)
            {
                logger.Warn(() => $"networkHashRate from daemon is zero!");
                networkHashRate = Int32.MaxValue;
            }
            double avgBlockTime = blockchainStats.NetworkDifficulty / networkHashRate;

            if(poolHashRate == 0)
            {
                logger.Info(() => $"pool hashrate is currently zero.  Payouts will also be zero.");
                poolHashRate = 1;
            }
            double blockFrequency = networkHashRate / poolHashRate * (avgBlockTime / SIXTY);
            double maxBlockFrequency = poolConfig.PaymentProcessing.MaxBlockFrequency;
            if(blockFrequency > maxBlockFrequency)
            {
                blockFrequency = maxBlockFrequency;
            }
            int payoutConfig = clusterConfig.PaymentProcessing.Interval;
            if(payoutConfig == 0)
            {
                logger.Warn(() => $"Payments are misconfigured. Interval should not be zero");
                payoutConfig = 600;          
            }

            double recepientBlockReward = (double)(blockRewardInEth * RECEPIENT_SHARE);
            double blockFrequencyPerPayout = blockFrequency / (payoutConfig / SIXTY);
            double blockData = recepientBlockReward / blockFrequencyPerPayout;
            logger.Info(() => $"BlockData : {blockData}, Network Block Time : {avgBlockTime}, Block Frequency : {blockFrequency}");

            return (decimal)blockData;
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(PoolConfig poolConfig,
            Dictionary<string, double> shares, Dictionary<string, decimal> rewards, decimal blockData, DateTime paidUntil)
        {
            var done = false;
            var before = paidUntil;
            var inclusive = true;
            var pageSize = 50000;
            var currentPage = 0;
            var accumulatedScore = 0.0m;

            double sumDifficulty = 0;
            DateTime? shareCutOffDate = null;
            Dictionary<string, decimal> scores = new Dictionary<string, decimal>();

            while (!done)
            {
                logger.Info(() => $"Fetching page {currentPage} of shares for pool {poolConfig.Id}");

                var pageTask = TelemetryUtil.TrackDependency(() => shareReadFaultPolicy.Execute(() =>
                    cf.Run(con => shareRepo.ReadSharesBeforeCreatedAsync(con, poolConfig.Id, before, inclusive, pageSize))),
                    DependencyType.Sql, "ReadAllSharesMined", "ReadAllSharesMined");
 
                Task.WaitAll(pageTask);
                var page = pageTask.Result;
                inclusive = false;
                currentPage++;

                logger.Info(() => $"No. of shares : {page.Length}");

                for (var i = 0; !done && i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;

                    // record attributed shares for diagnostic purposes
                    if(!shares.ContainsKey(address))
                        shares[address] = share.Difficulty;
                    else
                        shares[address] += share.Difficulty;

                    // determine a share's overall score
                    var score = (decimal)(share.Difficulty / share.NetworkDifficulty);

                    // track total hashes
                    sumDifficulty += share.Difficulty;

                    if (!scores.ContainsKey(address))
                        scores[address] = score;
                    else
                        scores[address] += score;
                    accumulatedScore += score;

                    // set the cutoff date to clean up old shares after a successful payout
                    if (shareCutOffDate == null || share.Created > shareCutOffDate)
                        shareCutOffDate = share.Created;
                }

                if (page.Length < pageSize)
                {
                    done = true;
                    break;
                }

                before = page[page.Length - 1].Created;
                done = page.Length <= 0;
            }

            if (accumulatedScore > 0)
            {
                // build rewards for all addresses that contributed to the round
                foreach (var address in scores.Select(x => x.Key).Distinct())
                {
                    // loop all scores for the current address
                    foreach (var score in scores.Where(x => x.Key == address))
                    {
                        var reward = blockData * (score.Value / accumulatedScore);

                        if (reward > 0)
                        {
                            // accumulate miner reward
                            if (!rewards.ContainsKey(address))
                                rewards[address] = reward;
                            else
                                rewards[address] += reward;
                        }
                    }
                }
            }
            
            /* update the value per hash in the pool payment processing config based on latest calculation */
            var valPerHash = blockData / (Decimal)sumDifficulty;
            poolConfig.PaymentProcessing.HashValue = valPerHash;
            TelemetryClient tc = TelemetryUtil.GetTelemetryClient();
            if(null != tc)
            {
                tc.TrackEvent("HashValue_" + poolConfig.Id, new Dictionary<string, string>
                {
                    {"BlockPayout", blockData.ToString()},
                    {"TotalHashes", sumDifficulty.ToString()},
                    {"HashValue", valPerHash.ToString()}
                });
            }

            // this should never happen
            if (!done)
                throw new OverflowException("Did not go through all shares");

            logger.Info(() => $"Balance-calculation for pool {poolConfig.Id} completed with accumulated score {accumulatedScore:0.#######} ({accumulatedScore * 100:0.#####}%)");

            return shareCutOffDate;
        }

        private void BuildFaultHandlingPolicy()
        {
            var retry = Policy
                .Handle<DbException>()
                .Or<SocketException>()
                .Or<TimeoutException>()
                .Retry(RetryCount, OnPolicyRetry);

            shareReadFaultPolicy = retry;
        }

        private static void OnPolicyRetry(Exception ex, int retry, object context)
        {
            logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
