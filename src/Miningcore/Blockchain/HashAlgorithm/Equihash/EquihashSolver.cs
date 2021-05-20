using System;
using System.Threading;
using Miningcore.Native;

// ReSharper disable InconsistentNaming

namespace Miningcore.Crypto.Hashing.Equihash
{
    public abstract class EquihashSolver
    {
        private static int maxThreads = 1;

        public static int MaxThreads
        {
            get => maxThreads;
            set
            {
                if(sem.IsValueCreated)
                    throw new InvalidOperationException("Too late: semaphore already created");

                maxThreads = value;
            }
        }

        protected static readonly Lazy<Semaphore> sem = new Lazy<Semaphore>(() =>
            new Semaphore(maxThreads, maxThreads));

        protected string personalization;

        public string Personalization => personalization;

        /// <summary>
        /// Verify an Equihash solution
        /// </summary>
        /// <param name="header">header including nonce (140 bytes)</param>
        /// <param name="solution">equihash solution without size-preamble</param>
        /// <returns></returns>
        public abstract bool Verify(ReadOnlySpan<byte> header, ReadOnlySpan<byte> solution);
    }
}
