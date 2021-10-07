using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Autofac;
using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.ApplicationInsights;
using Miningcore.Blockchain;
using Miningcore.Configuration;
using Miningcore.DataStore.Cloud.EtherScan;
using Miningcore.Extensions;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
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
            IBalanceRepository balanceRepo,
            IPaymentRepository paymentRepo)
        {
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(statsRepo, nameof(statsRepo));
            Contract.RequiresNonNull(paymentRepo, nameof(paymentRepo));
            Contract.RequiresNonNull(mapper, nameof(mapper));
            
            this.cf = cf;
            this.shareRepo = shareRepo;
            this.balanceRepo = balanceRepo;
            this.statsRepo = statsRepo;
            this.mapper = mapper;
            this.paymentRepo = paymentRepo;

            BuildFaultHandlingPolicy();
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IConnectionFactory cf;
        private readonly IShareRepository shareRepo;
        private readonly IStatsRepository statsRepo;
        private readonly IPaymentRepository paymentRepo;
        private readonly IMapper mapper;
        private static readonly ILogger Logger = LogManager.GetLogger("PPS Payment", typeof(PPSPaymentScheme));
        private const int RetryCount = 4;
        private const int DefaultHashRateCalculationWindow = 10; // mins
        private Policy shareReadFaultPolicy;

        #region IPayoutScheme

        public async Task UpdateBalancesAsync(IDbConnection con, IDbTransaction tx, PoolConfig poolConfig, ClusterConfig clusterConfig,
            IPayoutHandler payoutHandler, Block block, decimal blockReward)
        {
            // calculate rewards
            var shares = new Dictionary<string, double>();
            var rewards = new Dictionary<string, decimal>();
            var paidUntil = DateTime.UtcNow;
            var shareCutOffDate = CalculateRewards(poolConfig, shares, rewards, blockReward, paidUntil);

            // update balances
            foreach(var address in rewards.Keys)
            {
                var amount = rewards[address];

                if(amount > 0)
                {
                    // Deduct the predicted transaction fee
                    var txDeduction = payoutHandler.GetTransactionDeduction(amount);
                    if(txDeduction < 0 || txDeduction >= amount)
                    {
                        Logger.Error(() => $"Payouts are mis-configured. Transaction Deduction was calculated to be an invalid value: {payoutHandler.FormatAmount(txDeduction)}");
                    }
                    Logger.Info(() => $"Adding {payoutHandler.FormatAmount(amount)} to balance of {address} for {FormatUtil.FormatQuantity(shares[address])} ({shares[address]}) shares after deducting {payoutHandler.FormatAmount(txDeduction)}");

                    await TelemetryUtil.TrackDependency(
                            () => balanceRepo.AddAmountAsyncDeductingTxFee(con, tx, poolConfig.Id, address, amount, $"Reward for {FormatUtil.FormatQuantity(shares[address])} shares for block {block?.BlockHeight}", txDeduction, poolConfig.PaymentProcessing.MinimumPayment),
                            DependencyType.Sql, "AddBalanceAmount", $"miner:{address}, amount:{payoutHandler.FormatAmount(amount)}, txDeduction:{payoutHandler.FormatAmount(txDeduction)}");

                    await TelemetryUtil.TrackDependency(() => shareRepo.ProcessSharesForUserBeforeAcceptedAsync(con, tx, poolConfig.Id, address, shareCutOffDate.Value),
                    DependencyType.Sql, "ProcessMinerShares", $"miner:{address},cutoffDate:{shareCutOffDate.Value}");
                }
            }

            // delete discarded shares
            var deleteWindow = clusterConfig.Statistics?.HashrateCalculationWindow ?? DefaultHashRateCalculationWindow;
            var deleteCutoffTime = DateTime.UtcNow.AddMinutes(0 - (deleteWindow + 1)); // delete any data no longer needed by the StatsRecorder, plus a one-minute buffer

            await TelemetryUtil.TrackDependency(() => shareRepo.DeleteProcessedSharesBeforeAcceptedAsync(con, tx, poolConfig.Id, deleteCutoffTime),
            DependencyType.Sql, "DeleteOldShares", $"cutoffDate:{deleteCutoffTime}");

            // diagnostics
            var totalShareCount = shares.Values.ToList().Sum(x => new decimal(x));
            var totalRewards = rewards.Values.ToList().Sum(x => x);

            if(totalRewards > 0)
                Logger.Info(() => $"{FormatUtil.FormatQuantity((double) totalShareCount)} ({Math.Round(totalShareCount, 2)}) shares contributed to a total payout of {payoutHandler.FormatAmount(totalRewards)} ({totalRewards / blockReward * 100:0.00}% of block reward) to {rewards.Keys.Count} addresses");
        }

        #endregion // IPayoutScheme

        private DateTime? CalculateRewards(
            PoolConfig poolConfig,
            Dictionary<string, double> shares, 
            Dictionary<string, decimal> rewards, 
            decimal blockData, 
            DateTime paidUntil)
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

            while(!done)
            {
                Logger.Info(() => $"Fetching page {currentPage} of shares for pool {poolConfig.Id}");

                var pageTask = TelemetryUtil.TrackDependency(() => shareReadFaultPolicy.Execute(() =>
                    cf.Run(con => shareRepo.ReadUnprocessedSharesBeforeAcceptedAsync(con, poolConfig.Id, before, inclusive, pageSize))),
                    DependencyType.Sql, "ReadAllSharesMined", "ReadAllSharesMined");

                Task.WaitAll(pageTask);
                var page = pageTask.Result;
                inclusive = false;
                currentPage++;

                Logger.Info(() => $"No. of shares : {page.Length}");

                for(var i = 0; !done && i < page.Length; i++)
                {
                    var share = page[i];
                    var address = share.Miner;

                    // record attributed shares for diagnostic purposes
                    if(!shares.ContainsKey(address))
                        shares[address] = share.Difficulty;
                    else
                        shares[address] += share.Difficulty;

                    // determine a share's overall score
                    var score = (decimal) (share.Difficulty / share.NetworkDifficulty);

                    // track total hashes
                    sumDifficulty += share.Difficulty;

                    if(!scores.ContainsKey(address))
                        scores[address] = score;
                    else
                        scores[address] += score;
                    accumulatedScore += score;

                    // set the cutoff date to clean up old shares after a successful payout
                    if(shareCutOffDate == null || share.Accepted > shareCutOffDate)
                        shareCutOffDate = share.Accepted;
                }

                if(page.Length < pageSize)
                {
                    done = true;
                    break;
                }

                before = page[page.Length - 1].Accepted;
                done = page.Length <= 0;
            }

            if(accumulatedScore > 0)
            {
                // build rewards for all addresses that contributed to the round
                foreach(var address in scores.Select(x => x.Key).Distinct())
                {
                    // loop all scores for the current address
                    foreach(var score in scores.Where(x => x.Key == address))
                    {
                        var reward = blockData * (score.Value / accumulatedScore);

                        if(reward > 0)
                        {
                            // accumulate miner reward
                            if(!rewards.ContainsKey(address))
                                rewards[address] = reward;
                            else
                                rewards[address] += reward;
                        }
                    }
                }
            }

            /* update the value per hash in the pool payment processing config based on latest calculation */
            Decimal valPerMHash = 0;
            if(sumDifficulty > 0)
            {
                valPerMHash = (blockData / (Decimal) sumDifficulty) * 1000000;  // count MegaHashes instead of hashes.
            }
            poolConfig.PaymentProcessing.HashValue = valPerMHash;

            // Update the hashvalue in the database
            Task.WaitAll(cf.Run(con => paymentRepo.SetPoolStateHashValue(con, poolConfig.Id, valPerMHash)));
            
            TelemetryClient tc = TelemetryUtil.GetTelemetryClient();
            if(null != tc)
            {
                tc.TrackEvent("HashValue_" + poolConfig.Id, new Dictionary<string, string>
                {
                    {"BlockPayout", blockData.ToString()},
                    {"TotalHashes", sumDifficulty.ToString()},
                    {"HashValue", valPerMHash.ToString()}
                });
            }

            // this should never happen
            if(!done)
                throw new OverflowException("Did not go through all shares");

            Logger.Info(() => $"Balance-calculation for pool {poolConfig.Id} completed with accumulated score {accumulatedScore:0.#######} ({accumulatedScore * 100:0.#####}%)");

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
            Logger.Warn(() => $"Retry {retry} due to {ex.Source}: {ex.GetType().Name} ({ex.Message})");
        }
    }
}
