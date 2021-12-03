using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miningcore.Persistence.Cosmos.Entities
{
    public class BalanceChange : IDocumentModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        public string PoolId { get; set; }

        public string Address { get; set; }

        public decimal Amount { get; set; }

        public string Usage {get; set;}

        public DateTime Created { get; set; }

        public string CollectionName { get => "balanceChanges"; }

        public string PartitionKey { get => $"{PoolId}-{Address}"; }

        [JsonPropertyName("_etag")]
        public string ETag { get; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
