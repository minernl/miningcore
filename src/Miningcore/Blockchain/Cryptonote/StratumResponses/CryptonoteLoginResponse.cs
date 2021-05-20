

using Newtonsoft.Json;

namespace Miningcore.Blockchain.Cryptonote.StratumResponses
{
    public class CryptonoteLoginResponse : CryptonoteResponseBase
    {
        public string Id { get; set; }
        public CryptonoteJobParams Job { get; set; }
    }
}
