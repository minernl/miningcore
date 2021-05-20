using System;
using System.Threading;
using Miningcore.Native;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash
{
    public unsafe class EquihashSolver_144_5 : EquihashSolver
    {
        public EquihashSolver_144_5(string personalization)
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
                        return LibMultihash.equihash_verify_144_5(h, header.Length, s, solution.Length, personalization);
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
