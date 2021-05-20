using System;
using System.Threading;
using Miningcore.Native;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash
{
    public unsafe class VerusSolver : EquihashSolver
    {
        public VerusSolver(string personalization)
        {
            this.personalization = personalization;
        }

        public override bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution)
        {
            return true; //dummy verify as Verus doesnt verify solutions
        }
    }

}
