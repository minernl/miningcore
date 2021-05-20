using System;
using System.Security.Cryptography;

namespace Miningcore.Blockchain
{
    public class ExtraNonceProviderBase : IExtraNonceProvider
    {
        public ExtraNonceProviderBase(int extranonceBytes)
        {
            this.extranonceBytes = extranonceBytes;
            nonceMax = (1L << (extranonceBytes * 8)) - 1;
            stringFormat = "x" + extranonceBytes * 2;

            uint instanceId;

            using(var rng = RandomNumberGenerator.Create())
            {
                var bytes = new byte[4];
                rng.GetNonZeroBytes(bytes);
                instanceId = BitConverter.ToUInt32(bytes, 0);
            }

            var mask = (1L << (extranonceBytes * 8)) - 1;
            counter = Math.Abs(instanceId & mask);
        }

        private readonly object counterLock = new object();
        protected long counter;
        protected readonly int extranonceBytes;
        protected readonly long nonceMax;
        protected readonly string stringFormat;

        //#region IExtraNonceProvider

        public string Next()
        {
            lock(counterLock)
            {
                counter++;
                if(counter > nonceMax)
                    counter = 0;

                // encode to hex
                var result = counter.ToString(stringFormat);
                return result;
            }
        }

        //#endregion // IExtraNonceProvider
    }
}
