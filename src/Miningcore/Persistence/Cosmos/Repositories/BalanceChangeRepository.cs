using System;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Miningcore.Persistence.Cosmos.Entities;
using Miningcore.PoolCore;
using Miningcore.Extensions;
using NLog;

namespace Miningcore.Persistence.Cosmos.Repositories
{
    public class BalanceChangeRepository
    {
        public BalanceChangeRepository(CosmosClient cosmosClient)
        {
            this.cosmosClient = cosmosClient;
        }

        private readonly CosmosClient cosmosClient;
        private readonly string databaseId = Pool.clusterConfig.Persistence.Cosmos.DatabaseId;
        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public async Task AddNewBalanceChange(string poolId, string address, decimal amount, string usage)
        {
            logger.LogInvoke();

            var date = DateTime.UtcNow.Date;

            var balanceChange = new BalanceChange()
            {
                Id = $"{poolId}-{address}-{date}",
                PoolId = poolId,
                Address = address,
                Amount = amount,
                Usage = usage,
                Created = date
            };
            var requestOptions = new ItemRequestOptions();

            if (!String.IsNullOrEmpty(balanceChange.ETag))
            {
                requestOptions.IfMatchEtag = balanceChange.ETag;
            }

            await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .CreateItemAsync(balanceChange, new PartitionKey(balanceChange.PartitionKey), requestOptions);
        }

        public async Task<BalanceChange> GetBalanceChangeByDate(string poolId, string address, DateTime created)
        {
            logger.LogInvoke();

            var date = created.Date;
            var balanceChange = new BalanceChange()
            {
                Id = $"{poolId}-{address}-{date}",
                PoolId = poolId,
                Address = address,
                Created = date
            };
            ItemResponse<BalanceChange> balanceChangeResponse = await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .ReadItemAsync<BalanceChange>(balanceChange.Id, new PartitionKey(balanceChange.PartitionKey));
            return balanceChangeResponse.Resource;
        }

        public async Task UpdateBalanceChange(BalanceChange balanceChange)
        {
            logger.LogInvoke();

            await cosmosClient.GetContainer(databaseId, balanceChange.CollectionName)
                .ReplaceItemAsync(balanceChange, balanceChange.Id, new PartitionKey(balanceChange.PartitionKey));
        }
    }
}
