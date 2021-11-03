/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using Miningcore.Configuration;
using Miningcore.Extensions;
using Miningcore.Messaging;
using Miningcore.Notifications.Messages;
using Miningcore.Persistence;
using Miningcore.Persistence.Model;
using Miningcore.Persistence.Repositories;
using Miningcore.Util;
using NLog;
using Contract = Miningcore.Contracts.Contract;

namespace Miningcore.Payments
{
    /// <summary>
    /// Coin agnostic payment processor
    /// </summary>
    public class PayoutManager
    {
        public PayoutManager(IComponentContext ctx,
            IConnectionFactory cf,
            IBlockRepository blockRepo,
            IShareRepository shareRepo,
            IBalanceRepository balanceRepo,
            IMessageBus messageBus)
        {
            Contract.RequiresNonNull(ctx, nameof(ctx));
            Contract.RequiresNonNull(cf, nameof(cf));
            Contract.RequiresNonNull(blockRepo, nameof(blockRepo));
            Contract.RequiresNonNull(shareRepo, nameof(shareRepo));
            Contract.RequiresNonNull(balanceRepo, nameof(balanceRepo));
            Contract.RequiresNonNull(messageBus, nameof(messageBus));

            this.ctx = ctx;
            this.cf = cf;
            this.blockRepo = blockRepo;
            this.shareRepo = shareRepo;
            this.balanceRepo = balanceRepo;
            this.messageBus = messageBus;
        }

        private readonly IBalanceRepository balanceRepo;
        private readonly IBlockRepository blockRepo;
        private readonly IConnectionFactory cf;
        private readonly IComponentContext ctx;
        private readonly IShareRepository shareRepo;
        private readonly IMessageBus messageBus;
        private readonly CancellationTokenSource cts = new();
        private TimeSpan payoutInterval;
        private TimeSpan balanceCalculationInterval;
        private ClusterConfig clusterConfig;
        private static readonly ILogger Logger = LogManager.GetCurrentClassLogger();

        // Start Payment Services
        public void Start()
        {
            Task.Run(async () =>
            {
                Logger.Info(() => "Starting Payout Manager");

                // Run the initial balance calc & payout
                await UpdatePoolBalancesAsync();
                // The observable will trigger the observer once every interval
                Observable.Interval(balanceCalculationInterval).Select(_ => Observable.FromAsync(UpdatePoolBalancesAsync)).Concat().Subscribe(cts.Token);
                if(clusterConfig.PaymentProcessing.OnDemandPayout)
                {
                    await OnDemandPayoutPoolBalancesAsync();
                }
                else
                {
                    await PayoutPoolBalancesAsync();
                    Observable.Interval(payoutInterval).Select(_ => Observable.FromAsync(PayoutPoolBalancesAsync)).Concat().Subscribe(cts.Token);
                }
            });
        }

        public async Task<string> PayoutSingleBalanceAsync(PoolConfig pool, string miner)
        {
            var success = true;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            decimal amount = 0;
            try
            {
                var handler = await ResolveAndConfigurePayoutHandlerAsync(pool);
                var balance = await cf.Run(con => balanceRepo.GetBalanceWithPaidDateAsync(con, pool.Id, miner));
                amount = balance.Amount;
                return (await handler.PayoutAsync(balance))?.Id;
            }
            catch(Exception ex)
            {
                Logger.Error($"Failed to payout {miner}: {ex}");
                success = false;
                throw;
            }
            finally
            {
                TelemetryUtil.TrackMetric("FORCED_PAYOUT", "success", "duration", (double) amount, success.ToString(), timer.ElapsedMilliseconds.ToString());
            }
        }

        internal void Configure(ClusterConfig config)
        {
            clusterConfig = config;
            payoutInterval = TimeSpan.FromSeconds(config.PaymentProcessing.Interval > 0 ? config.PaymentProcessing.Interval : 600);
            balanceCalculationInterval = TimeSpan.FromSeconds(config.PaymentProcessing.BalanceCalculationInterval > 0
                ? config.PaymentProcessing.BalanceCalculationInterval
                : 300);
        }

        public void Stop()
        {
            Logger.Info(() => "Payments Service Stopping ..");

            cts.Cancel();

            Logger.Info(() => "Payment Service Stopped");
        }

        #region Private Methods

        private static CoinFamily HandleFamilyOverride(CoinFamily family, PoolConfig pool)
        {
            switch(family)
            {
                case CoinFamily.Equihash:
                    var equihashTemplate = pool.Template.As<EquihashCoinTemplate>();

                    if(equihashTemplate.UseBitcoinPayoutHandler)
                        return CoinFamily.Bitcoin;

                    break;
            }

            return family;
        }

        private async Task UpdatePoolBalancesAsync()
        {
            if(cts.IsCancellationRequested)
            {
                Logger.Info(() => "Cancel signal: stopping balance calculation");
                return;
            }

            foreach(var pool in clusterConfig.Pools.Where(x => x.Enabled && x.PaymentProcessing.Enabled))
            {
                Logger.Info(() => $"Processing balance calculation for pool [{pool.Id}]");

                try
                {
                    var handler = await ResolveAndConfigurePayoutHandlerAsync(pool);
                    var scheme = ctx.ResolveKeyed<IPayoutScheme>(pool.PaymentProcessing.PayoutScheme);

                    if(!pool.PaymentProcessing.BalanceUpdateEnabled) continue;

                    await UpdatePoolBalancesAsync(pool, handler, scheme);

                    var poolBalance = TelemetryUtil.TrackDependency(() => cf.Run(con => balanceRepo.GetTotalBalanceSum(con, pool.Id, pool.PaymentProcessing.MinimumPayment)),
                        DependencyType.Sql, "GetTotalBalanceSum", "GetTotalBalanceSum");
                    var walletBalance = TelemetryUtil.TrackDependency(() => handler.GetWalletBalance(), DependencyType.Web3, "GetWalletBalance", "GetWalletBalance");
                    await Task.WhenAll(poolBalance, walletBalance);

                    TelemetryUtil.TrackEvent($"Balance_{pool.Id}", new Dictionary<string, string>
                    {
                        {"TotalBalance", poolBalance.Result.TotalAmount.ToStr()},
                        {"TotalOverThreshold", poolBalance.Result.TotalAmountOverThreshold.ToStr()},
                        {"WalletBalance", walletBalance.Result.ToStr()}
                    });
                }
                catch(InvalidOperationException ex)
                {
                    Logger.Error(ex.InnerException ?? ex, () => $"[{pool.Id}] Balance calculation failed");
                }
                catch(AggregateException ex)
                {
                    switch(ex.InnerException)
                    {
                        case HttpRequestException httpEx:
                            Logger.Error(() => $"[{pool.Id}] Balance calculation failed: {httpEx.Message}");
                            break;

                        default:
                            Logger.Error(ex.InnerException, () => $"[{pool.Id}] Balance calculation failed");
                            break;
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error(ex, () => $"[{pool.Id}] Balance calculation failed");
                }
            }
        }

        private async Task UpdatePoolBalancesAsync(PoolConfig pool, IPayoutHandler handler, IPayoutScheme scheme)
        {
            // get pending blockRepo for pool

            var pendingBlocks = await TelemetryUtil.TrackDependency(() => cf.Run(con => blockRepo.GetPendingBlocksForPoolAsync(con, pool.Id)),
                DependencyType.Sql, "getPendingBlocks", "getPendingBlocks");

            // classify
            var updatedBlocks = await handler.ClassifyBlocksAsync(pendingBlocks);

            if(updatedBlocks.Any())
            {
                foreach(var block in updatedBlocks.OrderBy(x => x.Created))
                {
                    Logger.Info(() => $"Processing payments for pool {pool.Id}, block {block.BlockHeight}, effort {block.Effort}");


                    await cf.RunTx(async (con, tx) =>
                    {
                        if(!block.Effort.HasValue)  // fill block effort if empty
                            await CalculateBlockEffortAsync(pool, block, handler);

                        switch(block.Status)
                        {
                            case BlockStatus.Confirmed:
                                // blockchains that do not support block-reward payments via coinbase Tx
                                // must generate balance records for all reward recipients instead
                                var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, block, pool);

                                Logger.Info(() => $" --Pool {pool}");
                                Logger.Info(() => $" --Block {block}");
                                Logger.Info(() => $" --Block reward {blockReward}");

                                Logger.Info(() => $" --Con {con}");
                                Logger.Info(() => $" --tx {tx}");

                                await scheme.UpdateBalancesAsync(con, tx, pool, clusterConfig, handler, block, blockReward);
                                await blockRepo.UpdateBlockAsync(con, tx, block);
                                break;

                            case BlockStatus.Orphaned:
                                await blockRepo.UpdateBlockAsync(con, tx, block);
                                break;

                            case BlockStatus.Pending:
                                await blockRepo.UpdateBlockAsync(con, tx, block);
                                break;
                        }
                    });
                }
            }
            else
            {
                Logger.Info(() => $"No updated blocks for pool {pool.Id} but still payment processed");
                await TelemetryUtil.TrackDependency(() => cf.RunTx(async (con, tx) =>
                {
                    var blockReward = await handler.UpdateBlockRewardBalancesAsync(con, tx, null, pool);
                    await scheme.UpdateBalancesAsync(con, tx, pool, clusterConfig, handler, null, blockReward);
                }), DependencyType.Sql, "UpdateBalancesAsync", "UpdateBalances");
            }
        }

        private async Task PayoutPoolBalancesAsync()
        {
            if(cts.IsCancellationRequested)
            {
                Logger.Info(() => "Cancel signal: stopping payout");
                return;
            }

            foreach(var pool in clusterConfig.Pools.Where(x => x.Enabled && x.PaymentProcessing.Enabled))
            {
                Logger.Info(() => $"Processing payout for pool [{pool.Id}]");

                try
                {
                    var handler = await ResolveAndConfigurePayoutHandlerAsync(pool);
                    if(!pool.PaymentProcessing.PayoutEnabled) continue;

                    await PayoutPoolBalancesAsync(pool, handler);
                }
                catch(InvalidOperationException ex)
                {
                    Logger.Error(ex.InnerException ?? ex, () => $"[{pool.Id}] Payout processing failed");
                }
                catch(AggregateException ex)
                {
                    switch(ex.InnerException)
                    {
                        case HttpRequestException httpEx:
                            Logger.Error(() => $"[{pool.Id}] Payout processing failed: {httpEx.Message}");
                            break;

                        default:
                            Logger.Error(ex.InnerException, () => $"[{pool.Id}] Payout processing failed");
                            break;
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error(ex, () => $"[{pool.Id}] Payout processing failed");
                }
            }
        }

        private async Task OnDemandPayoutPoolBalancesAsync()
        {
            if(cts.IsCancellationRequested)
            {
                Logger.Info(() => "Cancel signal: stopping payout");
                return;
            }

            foreach(var pool in clusterConfig.Pools.Where(x => x.Enabled && x.PaymentProcessing.Enabled))
            {
                Logger.Info(() => $"Starting on-demand payout for pool [{pool.Id}]");

                try
                {
                    var handler = await ResolveAndConfigurePayoutHandlerAsync(pool);
                    if(!pool.PaymentProcessing.PayoutEnabled) continue;

                    handler.OnDemandPayoutAsync();
                }
                catch(Exception ex)
                {
                    Logger.Error(ex, () => $"[{pool.Id}] On-demand payout failed");
                }
            }
        }

        private async Task PayoutPoolBalancesAsync(PoolConfig pool, IPayoutHandler handler)
        {
            var poolBalancesOverMinimum = await TelemetryUtil.TrackDependency(() => cf.Run(con =>
                    balanceRepo.GetPoolBalancesOverThresholdAsync(con, pool.Id, pool.PaymentProcessing.MinimumPayment)),
                    DependencyType.Sql, "GetPoolBalancesOverThresholdAsync", "GetPoolBalancesOverThresholdAsync");

            if(poolBalancesOverMinimum.Length > 0)
            {
                try
                {
                    await TelemetryUtil.TrackDependency(() => handler.PayoutAsync(poolBalancesOverMinimum, cts.Token), DependencyType.Sql, "PayoutPoolBalancesAsync",
                        $"miners:{poolBalancesOverMinimum.Length}");
                }

                catch(Exception ex)
                {
                    Logger.Error(ex, "Error while processing balance payout");
                    await NotifyPayoutFailureAsync(poolBalancesOverMinimum, pool, ex);
                    throw;
                }
            }
            else
                Logger.Info(() => $"No balances over configured minimum payout {pool.PaymentProcessing.MinimumPayment.ToStr()} for pool {pool.Id}");
        }

        private Task NotifyPayoutFailureAsync(Balance[] balances, PoolConfig pool, Exception ex)
        {
            messageBus.SendMessage(new PaymentNotification(pool.Id, ex.Message, balances.Sum(x => x.Amount), pool.Template.Symbol));

            return Task.FromResult(true);
        }

        private async Task CalculateBlockEffortAsync(PoolConfig pool, Block block, IPayoutHandler handler)
        {
            Logger.Info(() => $"Calculate Block Effort");

            // get share date-range
            var from = DateTime.MinValue;
            var to = block.Created;

            // get last block for pool
            var lastBlock = await cf.Run(con => blockRepo.GetBlockBeforeAsync(con, pool.Id, new[]
            {
                BlockStatus.Confirmed,
                BlockStatus.Orphaned,
                BlockStatus.Pending,
            }, block.Created));

            if(lastBlock != null)
                from = lastBlock.Created;

            // get combined diff of all shares for block
            var accumulatedShareDiffForBlock = await cf.Run(con =>
                shareRepo.GetAccumulatedShareDifficultyBetweenCreatedAsync(con, pool.Id, from, to));

            // handler has the final say
            if(accumulatedShareDiffForBlock.HasValue)
                await handler.CalculateBlockEffortAsync(block, accumulatedShareDiffForBlock.Value);
        }

        private async Task<IPayoutHandler> ResolveAndConfigurePayoutHandlerAsync(PoolConfig pool)
        {
            var family = HandleFamilyOverride(pool.Template.Family, pool);

            // resolve payout handler
            var handlerImpl = ctx.Resolve<IEnumerable<Meta<Lazy<IPayoutHandler, CoinFamilyAttribute>>>>()
                .First(x => x.Value.Metadata.SupportedFamilies.Contains(family)).Value;

            var handler = handlerImpl.Value;
            await handler.ConfigureAsync(clusterConfig, pool);

            return handler;
        }

        #endregion
    }
}
