namespace Miningcore.Persistence.Cosmos
{
    public interface IDocumentModel
    {
        public string Id { get; }
        public string CollectionName { get; }
        public string PartitionKey { get; }
        public string ETag { get; }
    }
}
