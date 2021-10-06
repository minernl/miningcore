using System;
using Newtonsoft.Json;

namespace Miningcore.DataStore.Cloud.EtherScan
{
    public class EtherScanResponse<T>
    {
        public int Status { get; set; }
        public string Message { get; set; }
        public T Result { get; set; }

    }

    public class DailyUncleBlkCount
    {
        public DateTime UtcDate { get; set; }
        public string UnixTimeStamp { get; set; }
        public long UncleBlockCount { get; set; }
        [JsonProperty("uncleBlockRewards_Eth")]
        public decimal UncleBlockRewardsEth { get; set; }
    }
}
