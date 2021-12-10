using Autofac;
using Miningcore.PoolCore;
using ILogger = NLog.ILogger;
using Miningcore.Util;
using NLog;
using Microsoft.Azure.Cosmos;
using Miningcore.Configuration;
using System;
using System.Reflection;
using Miningcore.Persistence.Cosmos.Repositories;

namespace Miningcore.DataStore.Cosmos {
    internal class CosmosInterface {
        private static readonly ILogger logger = LogManager.GetLogger("Cosmos");

        internal static void ConnectDatabase(ContainerBuilder builder) {
            ConfigurePersistence(builder);
        }

        private static void ConfigurePersistence(ContainerBuilder builder)
        {
            if(Pool.clusterConfig.Persistence == null && Pool.clusterConfig.PaymentProcessing?.Enabled == true && Pool.clusterConfig.ShareRelay == null)
                logger.ThrowLogPoolStartupException("Persistence is not configured!");

            if(Pool.clusterConfig.Persistence?.Cosmos != null)
            {
                ConfigureCosmos(Pool.clusterConfig.Persistence.Cosmos, builder);
            }
        }

        private static void ConfigureCosmos(CosmosConfig cosmosConfig, ContainerBuilder builder)
        {
            // validate config
            if(string.IsNullOrEmpty(cosmosConfig.EndpointUrl))
                logger.ThrowLogPoolStartupException("Cosmos configuration: invalid or missing 'endpoint url'");

            if(string.IsNullOrEmpty(cosmosConfig.AuthorizationKey))
                logger.ThrowLogPoolStartupException("Cosmos configuration: invalid or missing 'authorizationKey'");

            if(string.IsNullOrEmpty(cosmosConfig.DatabaseId))
                logger.ThrowLogPoolStartupException("Comos configuration: invalid or missing 'databaseId'");

            logger.Info(() => $"Connecting to Cosmos Server {cosmosConfig.EndpointUrl}");

            var cosmosClientOptions = new CosmosClientOptions();

            if (Enum.TryParse(cosmosConfig.ConsistencyLevel, out ConsistencyLevel consistencyLevel))
                cosmosClientOptions.ConsistencyLevel = consistencyLevel;

            if (Enum.TryParse(cosmosConfig.ConnectionMode, out ConnectionMode connectionMode))
                cosmosClientOptions.ConnectionMode = connectionMode;

            if (TimeSpan.TryParse(cosmosConfig.RequestTimeout, out TimeSpan requestTimeout))
                cosmosClientOptions.RequestTimeout = requestTimeout;
 
            if (int.TryParse(cosmosConfig.MaxRetryAttempt, out int maxRetryAttempt))
                cosmosClientOptions.MaxRetryAttemptsOnRateLimitedRequests = maxRetryAttempt;

            if (TimeSpan.TryParse(cosmosConfig.MaxRetryWaitTime, out TimeSpan maxRetryWaitTime))
                cosmosClientOptions.MaxRetryWaitTimeOnRateLimitedRequests = maxRetryWaitTime;

            if (int.TryParse(cosmosConfig.MaxPoolSize, out int maxPoolSize))
                cosmosClientOptions.MaxRequestsPerTcpConnection = maxPoolSize;

            if (cosmosConfig.PreferredLocations != null && cosmosConfig.PreferredLocations.Count > 0)
                cosmosClientOptions.ApplicationPreferredRegions = cosmosConfig.PreferredLocations;

            var cosmos = new CosmosClient(cosmosConfig.EndpointUrl, cosmosConfig.AuthorizationKey, cosmosClientOptions);

            // register CosmosClient
            builder.RegisterInstance(cosmos).AsSelf().SingleInstance();

            // register repositories
            builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
                .Where(t => t.Namespace.StartsWith(typeof(BalanceChangeRepository).Namespace))
                .AsImplementedInterfaces()
                .SingleInstance();
        }
    }
}