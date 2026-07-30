using System.Collections.Generic;
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
            ModClass.LogWarning(comparison.Message);
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
            ModClass.LogWarning(comparison.Message);
            return false;
        }

        public static AWAsyncShadowSnapshot Snapshot()
        {
            return new AWAsyncShadowSnapshot(
                Interlocked.Read(ref _comparisons),
                Interlocked.Read(ref _mismatches));
        }
    }
}
