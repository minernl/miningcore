/*
MiningCore 2.0
Copyright 2021 MinerNL (Miningcore.com)
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Autofac.Features.Metadata;
using AutoMapper;
using Miningcore.Api.Responses;
using Miningcore.Configuration;
using Miningcore.Crypto.Hashing.Equihash;
using Miningcore.DataStore.FileLogger;
using Miningcore.DataStore.Postgres;
using Miningcore.Mining;
using Miningcore.Notifications;
using Miningcore.Payments;
using Miningcore.Util;
using NBitcoin.Zcash;
using Newtonsoft.Json;
using NLog;
using ILogger = NLog.ILogger;
using System.Collections.Concurrent;
using Miningcore.DataStore.Cloud.EtherScan;

namespace Miningcore.PoolCore
{
    internal class Pool
    {

        private static readonly CancellationTokenSource Cts = new CancellationTokenSource();
        private static readonly ILogger Logger = LogManager.GetLogger("PoolCore");
        private static ShareRecorder shareRecorder;
        private static ShareRelay shareRelay;
        private static ShareReceiver shareReceiver;
        private static PayoutManager payoutManager;
        private static StatsRecorder statsRecorder;
        private static NotificationService notificationService;
        private static MetricsPublisher metricsPublisher;
        private static BtStreamReceiver btStreamReceiver;
        private static readonly ConcurrentDictionary<string, IMiningPool> Pools = new ConcurrentDictionary<string, IMiningPool>();
        private static AdminGcStats gcStats = new AdminGcStats();
        private static readonly IPAddress IPv4LoopBackOnIPv6 = IPAddress.Parse("::ffff:127.0.0.1");

        internal static ClusterConfig clusterConfig;
        internal static IContainer container;

        internal static void StartMiningCorePool(string configFile)
        {
            StartMiningCorePoolInternal(PoolConfig.GetConfigFromFile(configFile));
        }

        internal static void StartMiningCorePoolWithEnvConfig(string config)
        {
            Console.WriteLine($"Loading config from env variable '{PoolConfig.EnvironmentConfig}'");
            StartMiningCorePoolInternal(PoolConfig.GetConfigFromJson(config));
        }

        internal static void StartMiningCorePoolWithRemoteConfig(string appConfig)
        {
            var vault = Environment.GetEnvironmentVariable(PoolConfig.VaultName);
            StartMiningCorePoolInternal(!string.IsNullOrEmpty(vault)
                ? PoolConfig.GetConfigFromKeyVault(vault, appConfig)
                : PoolConfig.GetConfigFromAppConfig(Environment.GetEnvironmentVariable(PoolConfig.ConnectionString), appConfig));
        }

        private static void StartMiningCorePoolInternal(ClusterConfig config)
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly();
                // Display Software Version Info

                var basePath = Path.GetDirectoryName(assembly?.Location);
                Console.WriteLine($"{assembly?.GetName().Name} - MinerNL build v{assembly?.GetName().Version}");
                Console.WriteLine($"Run location: {basePath}");
                Console.WriteLine(" ");

                // log unhandled program exception errors

                var currentDomain = AppDomain.CurrentDomain;
                currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MC_UnhandledException);
                currentDomain.ProcessExit += OnProcessExit;
                Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);

                // ValidateRuntimeEnvironment();
                // root check
                if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Environment.UserName == "root")
                    Logger.Warn(() => "Running as root is discouraged!");

                // require 64-bit Windows OS
                if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X86)
                    throw new PoolStartupAbortException("Miningcore requires 64-Bit Windows");
                
                clusterConfig = config;

                // Initialize Logging
                FileLogger.ConfigureLogging();

                // LogRuntimeInfo();
                //-----------------------------------------------------------------------------
                Logger.Info(() => $"{RuntimeInformation.FrameworkDescription.Trim()} on {RuntimeInformation.OSDescription.Trim()} [{RuntimeInformation.ProcessArchitecture}]");

                // Bootstrap();
                //-----------------------------------------------------------------------------
                ZcashNetworks.Instance.EnsureRegistered();

                // Service collection
                var builder = new ContainerBuilder();
                builder.RegisterAssemblyModules(typeof(AutofacModule).GetTypeInfo().Assembly);
                builder.RegisterInstance(clusterConfig);
                builder.RegisterInstance(Pools);
                builder.RegisterInstance(gcStats);

                // AutoMapper
                var amConf = new MapperConfiguration(cfg => { cfg.AddProfile(new AutoMapperProfile()); });
                builder.Register((ctx, parms) => amConf.CreateMapper());
                builder.Register(_ => new EtherScanEndpoint(clusterConfig)).SingleInstance();
                PostgresInterface.ConnectDatabase(builder);

                container = builder.Build();

                // Configure Equihash
                if(clusterConfig.EquihashMaxThreads.HasValue)
                    EquihashSolver.MaxThreads = clusterConfig.EquihashMaxThreads.Value;

                MonitorGarbageCollection();

                // Start Miningcore Pool Services
                if(!Cts.IsCancellationRequested)
                    StartMiningcorePoolServices().Wait(Cts.Token);

            }

            catch(PoolStartupAbortException ex)
            {
                if(!string.IsNullOrEmpty(ex.Message))
                    Console.WriteLine(ex.Message);

                Console.WriteLine("\nCluster cannot start. Good Bye!");
            }

            catch(JsonException)
            {
                // ignored
            }

            catch(IOException)
            {
                // ignored
            }

            catch(AggregateException ex)
            {
                if(!(ex.InnerExceptions.First() is PoolStartupAbortException))
                    Console.WriteLine(ex);

                Console.WriteLine("Cluster cannot start. Good Bye!");
            }

            catch(OperationCanceledException)
            {
                // Ctrl+C
            }

            catch(Exception ex)
            {
                Console.WriteLine(ex);

                Console.WriteLine("Cluster cannot start. Good Bye!");
            }

            finally
            {
                // Shutdown();
                Console.WriteLine("Miningcore is shuting down... bye!");
                Logger?.Info(() => "Miningcore is shuting down... bye!");

                foreach(var poolToStop in Pools.Values)
                {
                    Console.WriteLine($"Stopping pool {poolToStop}");
                    poolToStop.Stop();
                }

                shareRelay?.Stop();
                shareReceiver?.Stop();
                shareRecorder?.Stop();
                statsRecorder?.Stop();
                Process.GetCurrentProcess().Kill();

            }

        }

        // **************************************************************************
        //  MININGCORE POOL SERVICES
        // **************************************************************************
        private static async Task StartMiningcorePoolServices()
        {
            var coinTemplates = PoolCoinTemplates.LoadCoinTemplates();
            Logger.Info($"{coinTemplates.Keys.Count} coins loaded from {string.Join(", ", clusterConfig.CoinTemplates)}");

            // Populate pool configs with corresponding template
            foreach(var poolConfig in clusterConfig.Pools.Where(x => x.Enabled))
            {
                // Foreach coin definition
                if(!coinTemplates.TryGetValue(poolConfig.Coin, out var template))
                    Logger.ThrowLogPoolStartupException($"Pool {poolConfig.Id} references undefined coin '{poolConfig.Coin}'");

                poolConfig.Template = template;
            }

            // Notifications
            notificationService = container.Resolve<NotificationService>();

            // start btStream receiver
            btStreamReceiver = container.Resolve<BtStreamReceiver>();
            btStreamReceiver.Start(clusterConfig);

            if(clusterConfig.ShareRelay == null)
            {
                // start share recorder
                shareRecorder = container.Resolve<ShareRecorder>();
                shareRecorder.Start(clusterConfig);

                // start share receiver (for external shares)
                shareReceiver = container.Resolve<ShareReceiver>();
                shareReceiver.Start(clusterConfig);
            }
            else
            {
                // start share relay
                shareRelay = container.Resolve<ShareRelay>();
                shareRelay.Start(clusterConfig);
            }

            // start API
            if(clusterConfig.Api == null || clusterConfig.Api.Enabled)
            {
                Api.ApiService.StartApiService(clusterConfig);
                metricsPublisher = container.Resolve<MetricsPublisher>();
            }
            else
            {
                Logger.Warn("API is disabled");
            }

            // configure and start payment processor
            payoutManager = container.Resolve<PayoutManager>();
            payoutManager.Configure(clusterConfig);
            if(clusterConfig.PaymentProcessing?.Enabled == true && clusterConfig.Pools.Any(x => x.PaymentProcessing?.Enabled == true))
            {
                payoutManager.Start();
            }
            else
            {
                Logger.Warn("Payment processing is Disabled");
            }

            if(clusterConfig.ShareRelay == null)
            {
                // start pool stats updater
                statsRecorder = container.Resolve<StatsRecorder>();
                statsRecorder.Configure(clusterConfig);
                statsRecorder.Start();
            }
            else
            {
                Logger.Info("Share Relay is Active!");
            }

            // start stratum pools
            await Task.WhenAll(clusterConfig.Pools.Where(x => x.Enabled).Select(async poolConfig =>
            {
                // resolve pool implementation
                var poolImpl = container.Resolve<IEnumerable<Meta<Lazy<IMiningPool, CoinFamilyAttribute>>>>()
                    .First(x => x.Value.Metadata.SupportedFamilies.Contains(poolConfig.Template.Family)).Value;

                // create and configure
                var stratumPool = poolImpl.Value;
                stratumPool.Configure(poolConfig, clusterConfig);
                Pools[poolConfig.Id] = stratumPool;

                // pre-start attachments
                shareReceiver?.AttachPool(stratumPool);
                statsRecorder?.AttachPool(stratumPool);
                //apiServer?.AttachPool(stratumPool);

                await stratumPool.StartAsync(Cts.Token);
            }));

            // keep running
            await Observable.Never<Unit>().ToTask(Cts.Token);
        }

        private static void MonitorGarbageCollection()
        {
            var thread = new Thread(() =>
            {
                var sw = new Stopwatch();

                while(true)
                {
                    var s = GC.WaitForFullGCApproach();
                    if(s == GCNotificationStatus.Succeeded)
                    {
                        Logger.Info(() => "Garbage Collection bin Full soon");
                        sw.Start();
                    }

                    s = GC.WaitForFullGCComplete();

                    if(s == GCNotificationStatus.Succeeded)
                    {
                        Logger.Info(() => "Garbage Collection bin Full!!");

                        sw.Stop();

                        if(sw.Elapsed.TotalSeconds > gcStats.MaxFullGcDuration)
                            gcStats.MaxFullGcDuration = sw.Elapsed.TotalSeconds;

                        sw.Reset();
                    }
                }
            });

            GC.RegisterForFullGCNotification(1, 1);
            thread.Start();
        }

        internal static Task RecoverSharesAsync(string recoveryFilename)
        {
            shareRecorder = container.Resolve<ShareRecorder>();
            return shareRecorder.RecoverSharesAsync(clusterConfig, recoveryFilename);
        }

        // log unhandled program exception errors
        private static void MC_UnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            if(Logger != null)
            {
                Logger.Error(args.ExceptionObject);
                LogManager.Flush(TimeSpan.Zero);
            }
            Exception e = (Exception) args.ExceptionObject;
            Console.WriteLine("----------------------------------------------------------------------------------------");
            Console.WriteLine("MyHandler caught : " + e.Message);
            Console.WriteLine("Runtime terminating: {0}", args.IsTerminating);
        }

        protected static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args)
        {
            Logger?.Info(() => $"Miningcore is stopping because exit key [{args.SpecialKey}] recieved. Exiting.");
            Console.WriteLine($"Miningcore is stopping because exit key  [{args.SpecialKey}] recieved. Exiting.");

            try
            {
                Cts?.Cancel();
            }
            catch
            {
            }

            args.Cancel = true;
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Logger?.Info(() => "Miningcore received process stop request.");
            Console.WriteLine("Miningcore received process stop request.");

            try
            {
                Cts?.Cancel();
            }
            catch
            {
            }
        }
    }
}
