using System;
using System.Diagnostics;

namespace AncientWarfare3.core.asyncwork
{
    internal static class AWAsyncCompletionDrainRules
    {
        internal const double FrameBudgetMilliseconds = 0.35d;

        internal static int ResolveItemLimit(int pPendingCount)
        {
            if (pPendingCount <= 0) return 0;
            // A completion callback may touch Unity even when the worker did
            // all expensive computation in the background. Keep replay small
            // enough that a burst cannot become a simulation hitch.
            return pPendingCount <= 8 ? 1 : 2;
        }

        internal static int ResolveBatchLimit(int pPendingCount)
        {
            return pPendingCount <= 0 ? 0 : 1;
        }

        internal static double RemainingMilliseconds(long pDeadline)
        {
            long remaining = pDeadline - Stopwatch.GetTimestamp();
            if (remaining <= 0L) return 0.01d;
            return Math.Max(0.01d, remaining * 1000d /
                Stopwatch.Frequency);
        }

        internal static bool HasTime(long pDeadline)
        {
            return Stopwatch.GetTimestamp() < pDeadline;
        }
    }
}
