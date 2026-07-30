using System;
using System.Diagnostics;
using System.Threading;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.policy
{
    internal static class RecentFeatureBenchmark
    {
        private static readonly long[] Ticks =
            new long[RecentFeatureBenchmarkRules.EntryIds.Length];
        private static readonly int[] Counts =
            new int[RecentFeatureBenchmarkRules.EntryIds.Length];
        private static long _totalTicks;
        private static int _totalCalls;
        private const int MaxTrackedScopeDepth = 64;
        [System.ThreadStatic]
        private static int _scopeDepth;
        [System.ThreadStatic]
        private static long[] _nestedTicksByDepth;

        public static long Begin()
        {
            if (!Bench.bench_enabled &&
                !RuntimePerformanceDiagnostic.IsSampling) return 0L;
            if (_nestedTicksByDepth == null)
                _nestedTicksByDepth = new long[MaxTrackedScopeDepth];
            if (_scopeDepth < MaxTrackedScopeDepth)
                _nestedTicksByDepth[_scopeDepth] = 0L;
            long token = RecentFeatureBenchmarkRules.EncodeScopeStart(
                Stopwatch.GetTimestamp(), _scopeDepth);
            _scopeDepth++;
            return token;
        }

        public static void End(int pIndex, long pStartTicks)
        {
            if (pStartTicks == 0L) return;
            int depth = System.Math.Max(0, _scopeDepth - 1);
            try
            {
                long elapsed = Stopwatch.GetTimestamp() -
                               RecentFeatureBenchmarkRules.DecodeScopeStart(
                                   pStartTicks);
                if (elapsed < 0L) return;
                long nested = depth < MaxTrackedScopeDepth &&
                              _nestedTicksByDepth != null
                    ? _nestedTicksByDepth[depth]
                    : 0L;
                long exclusive = RecentFeatureBenchmarkRules.ExclusiveScopeTicks(
                    elapsed, nested);
                if (depth > 0 && depth - 1 < MaxTrackedScopeDepth &&
                    _nestedTicksByDepth != null)
                    _nestedTicksByDepth[depth - 1] += elapsed;
                if (!RecentFeatureBenchmarkRules.IsValidIndex(pIndex)) return;
                bool outermost =
                    RecentFeatureBenchmarkRules.IsOutermostScopeToken(
                        pStartTicks);
                if (Bench.bench_enabled)
                {
                    Interlocked.Add(ref Ticks[pIndex], exclusive);
                    Interlocked.Increment(ref Counts[pIndex]);
                    if (outermost)
                    {
                        Interlocked.Add(ref _totalTicks, elapsed);
                        Interlocked.Increment(ref _totalCalls);
                    }
                }
                RuntimePerformanceDiagnostic.RecordRecent(pIndex, exclusive,
                    outermost, elapsed);
            }
            finally
            {
                if (depth < MaxTrackedScopeDepth && _nestedTicksByDepth != null)
                    _nestedTicksByDepth[depth] = 0L;
                _scopeDepth = depth;
            }
        }

        public static void Flush()
        {
            ArmyRtsBenchmarkSnapshot armyRts =
                ArmyRtsBenchmark.TakeIntervalSnapshot();
            if (!Bench.bench_enabled)
            {
                Reset();
                return;
            }

            long totalTicks = Interlocked.Read(ref _totalTicks);
            int totalCalls = Volatile.Read(ref _totalCalls);
            if (RecentFeatureBenchmarkRules.ShouldSaveSample(totalCalls))
                Save(RecentFeatureBenchmarkRules.Total, totalTicks, totalCalls,
                    RecentFeatureBenchmarkRules.TotalParentGroup);
            for (int i = 0; i < Ticks.Length; i++)
            {
                int count = Volatile.Read(ref Counts[i]);
                if (!RecentFeatureBenchmarkRules.ShouldSaveSample(count))
                    continue;
                Save(RecentFeatureBenchmarkRules.IdForIndex(i),
                    Interlocked.Read(ref Ticks[i]), count,
                    RecentFeatureBenchmarkRules.Group);
            }
            SaveArmyRtsCounters(armyRts);
            Reset();
        }

        private static void SaveArmyRtsCounters(
            ArmyRtsBenchmarkSnapshot pSnapshot)
        {
            long total = 0L;
            for (var index = 0;
                 index < ArmyRtsBenchmark.EntryIds.Length; index++)
            {
                long value = Math.Max(0L, pSnapshot.ValueAt(index));
                total = total > long.MaxValue - value
                    ? long.MaxValue
                    : total + value;
                Save(ArmyRtsBenchmark.EntryIds[index], 0L,
                    ClampCounter(value), ArmyRtsBenchmark.Group);
            }
            Save(ArmyRtsBenchmark.Total, 0L, ClampCounter(total),
                ArmyRtsBenchmark.TotalParentGroup);
        }

        private static int ClampCounter(long pValue)
        {
            return pValue >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Max(0L, pValue);
        }

        private static void Save(string pId, long pTicks, int pCalls,
            string pGroup)
        {
            double seconds = pTicks <= 0L
                ? 0d
                : (double)pTicks / Stopwatch.Frequency;
            Bench.benchSaveSplit(pId, seconds, pCalls, pGroup);
            Bench.saveAverageCounter(pId, pGroup);
        }

        private static void Reset()
        {
            Interlocked.Exchange(ref _totalTicks, 0L);
            Interlocked.Exchange(ref _totalCalls, 0);
            for (int i = 0; i < Ticks.Length; i++)
            {
                Interlocked.Exchange(ref Ticks[i], 0L);
                Interlocked.Exchange(ref Counts[i], 0);
            }
        }
    }
}
