using System.Collections.Generic;

namespace Miningcore.Blockchain
{
    public static class DevDonation
    {
        public const decimal Percent = 0.0m;

        public static readonly Dictionary<string, string> Addresses = new Dictionary<string, string>
        {
            { "BTC", "" },
            { "BCH", "" },
            { "BCD", "" },
            { "BTG", "" },
            { "DASH", "" },
            { "DOGE", "" },
            { "DGB", "" },
            { "ETC", "" },
            { "ETH", "" },
            { "ETN", "" },
            { "LCC", "" },
            { "LTC", "" },
            { "ETP", "" },
            { "MONA", "" },
            { "RVN", "" },
            { "TUBE", "" },
			{ "VRSC", "" },
            { "VTC", "" },
            { "XVG", "" },
            { "XMR", "" },
            { "ZCL", "" },
            { "ZEC", "" },
            { "ZEN", "" }
        };
    }

    public static class CoinMetaData
    {
        public const string BlockHeightPH = "$height$";
        public const string BlockHashPH = "$hash$";
    }
}
