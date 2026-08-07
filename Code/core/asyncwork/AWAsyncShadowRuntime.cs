using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;

namespace AncientWarfare3.core.asyncwork
{
    internal readonly struct AWAsyncShadowSnapshot
    {
        public AWAsyncShadowSnapshot(long pComparisons, long pMismatches)
        {
            Comparisons = pComparisons;
            Mismatches = pMismatches;
        }

        public long Comparisons { get; }
        public long Mismatches { get; }
    }

    internal static class AWAsyncShadowRuntime
    {
        private static long _comparisons;
        private static long _mismatches;
        private const int WarningCapacity = 64;
        private static readonly ConcurrentQueue<string> PendingWarnings =
            new ConcurrentQueue<string>();
        private static int _pendingWarningCount;

        public static bool CompareIds(string pChannel, string pKey,
            IReadOnlyList<long> pSynchronousIds,
            IReadOnlyList<long> pAsynchronousIds)
        {
            if (!AWAsyncRuntime.ShadowEnabled) return true;
            Interlocked.Increment(ref _comparisons);
            AWAsyncShadowComparison comparison =
                AWAsyncShadowComparisonRules.CompareIds(
                    AWAsyncRuntime.WorldGeneration, pChannel, pKey,
                    pSynchronousIds, pAsynchronousIds);
            if (comparison.IsMatch) return true;
            Interlocked.Increment(ref _mismatches);
            QueueWarning(comparison.Message);
            return false;
        }

        public static bool CompareSummary(string pChannel, string pKey,
            string pSynchronousSummary, string pAsynchronousSummary)
        {
            if (!AWAsyncRuntime.ShadowEnabled) return true;
            Interlocked.Increment(ref _comparisons);
            AWAsyncShadowComparison comparison =
                AWAsyncShadowComparisonRules.CompareSummary(
                    AWAsyncRuntime.WorldGeneration, pChannel, pKey,
                    pSynchronousSummary, pAsynchronousSummary);
            if (comparison.IsMatch) return true;
            Interlocked.Increment(ref _mismatches);
            QueueWarning(comparison.Message);
            return false;
        }

        public static int DrainMainThread(int pMaximumWarnings)
        {
            int processed = 0;
            while (processed < Math.Max(0, pMaximumWarnings) &&
                   PendingWarnings.TryDequeue(out string warning))
            {
                Interlocked.Decrement(ref _pendingWarningCount);
                ModClass.LogWarning(warning);
                processed++;
            }
            return processed;
        }

        public static void Clear()
        {
            while (PendingWarnings.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _pendingWarningCount, 0);
        }

        public static AWAsyncShadowSnapshot Snapshot()
        {
            return new AWAsyncShadowSnapshot(
                Interlocked.Read(ref _comparisons),
                Interlocked.Read(ref _mismatches));
        }

        private static void QueueWarning(string pMessage)
        {
            PendingWarnings.Enqueue(pMessage ?? string.Empty);
            int count = Interlocked.Increment(ref _pendingWarningCount);
            while (count > WarningCapacity &&
                   PendingWarnings.TryDequeue(out _))
                count = Interlocked.Decrement(ref _pendingWarningCount);
        }
    }
}
