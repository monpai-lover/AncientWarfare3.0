using System;
using System.Threading;

namespace AncientWarfare3.core.db
{
    internal static class HistoricalContentRevision
    {
        private static long _revision = 1L;

        public static long Current => Interlocked.Read(ref _revision);

        public static void AdvanceAfterSuccessfulSynchronousWrite(
            Action pWrite)
        {
            if (pWrite == null) throw new ArgumentNullException(nameof(pWrite));
            pWrite();
            Advance();
        }

        public static long Advance()
        {
            while (true)
            {
                long current = Interlocked.Read(ref _revision);
                if (current == long.MaxValue) return current;
                long next = current + 1L;
                if (Interlocked.CompareExchange(ref _revision, next,
                        current) == current)
                    return next;
            }
        }
    }
}
