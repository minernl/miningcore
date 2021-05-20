using System.Collections.Generic;
using Newtonsoft.Json;

namespace Miningcore.Blockchain.Equihash.DaemonResponses
{
    public class EquihashCoinbaseTransaction
    {
        public string Data { get; set; }
        public string Hash { get; set; }
        public decimal Fee { get; set; }
        public int SigOps { get; set; }
        public ulong FoundersReward { get; set; }
        public bool Required { get; set; }

        // "depends":[ ],
    }
}
