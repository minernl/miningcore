using System;

namespace Miningcore.Blockchain
{
    public interface IExtraNonceProvider
    {
        string Next();
    }
}
