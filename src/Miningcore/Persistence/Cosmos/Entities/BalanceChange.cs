using System;
using Newtonsoft.Json;

namespace Miningcore.Persistence.Cosmos.Entities
{
    public class BalanceChange : IDocumentModel
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "poolId")]
        public string PoolId { get; set; }

        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public decimal Amount { get; set; }

        [JsonProperty(PropertyName = "usage")]
        public string Usage {get; set;}

        [JsonProperty(PropertyName = "dateTime")]
        public DateTime Created { get; set; }

        public string CollectionName { get => "balanceChanges"; }

        [JsonProperty(PropertyName = "partitionKey")]
        public string PartitionKey { get => $"{PoolId}-{Address}"; }

        [JsonProperty(PropertyName = "_etag")]
        public string ETag { get; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
