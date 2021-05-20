using System;
using System.Threading;
using Miningcore.Native;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash
{
    public unsafe class EquihashSolver_200_9 : EquihashSolver
    {
        public EquihashSolver_200_9(string personalization)
        {
            this.personalization = personalization;
        }

        public override bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution)
        {
            try
            {
                sem.Value.WaitOne();

                fixed (byte* h = header)
                {
                    fixed (byte* s = solution)
                    {
                        return LibMultihash.equihash_verify_200_9(h, header.Length, s, solution.Length, personalization);
                    }
                }
            }

            finally
            {
                sem.Value.Release();
            }
        }
    }
}
