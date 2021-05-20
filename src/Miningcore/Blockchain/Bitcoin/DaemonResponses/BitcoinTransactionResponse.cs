using Newtonsoft.Json;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class BitcoinTransactionResponse
    {
        public decimal Amount { get; set; }
        public int Confirmations { get; set; }
        public bool Generated { get; set; }
        public string BlockHash { get; set; }
        public long BlockIndex { get; set; }
        public ulong BlockTime { get; set; }
        public string TxId { get; set; }
        public string[] WalletConflicts { get; set; }
        public ulong Time { get; set; }
        public ulong TimeReceived { get; set; }

        [JsonProperty("bip125-replaceable")]
        public string Bip125Replaceable { get; set; }

        public BitcoinTransactionDetails[] Details { get; set; }
    }

    public class BitcoinTransactionDetails
    {
        public string Address { get; set; }
        public string Category { get; set; }
        public decimal Amount { get; set; }
        public string Label { get; set; }
        public int Vout { get; set; }
    }
}
