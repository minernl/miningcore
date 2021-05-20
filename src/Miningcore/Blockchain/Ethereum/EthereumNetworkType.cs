using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Miningcore.Blockchain.Ethereum
{
    public enum EthereumNetworkType
    {
        Main = 1,
        Morden = 2,
        Ropsten = 3,
        Rinkeby = 4,
        Kovan = 42,
        Galilei = 7919,
        Joys = 35855456,

        Unknown = -1,
    }
}
