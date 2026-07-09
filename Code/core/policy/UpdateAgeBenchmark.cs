using System;
using System.Diagnostics;

namespace AncientWarfare3.core.policy
{
    internal static class UpdateAgeBenchmark
    {
        private static readonly long[] s_ticks = new long[UpdateAgeBenchmarkRules.EntryIds.Length];
        private static readonly int[] s_counts = new int[UpdateAgeBenchmarkRules.EntryIds.Length];

        public static long Begin()
        {
            return Bench.bench_enabled ? Stopwatch.GetTimestamp() : 0L;
        }

        public static void End(int pIndex, long pStartTicks)
        {
            if (pStartTicks == 0L || !Bench.bench_enabled || !UpdateAgeBenchmarkRules.IsValidIndex(pIndex)) return;
            s_ticks[pIndex] += Stopwatch.GetTimestamp() - pStartTicks;
            s_counts[pIndex]++;
        }

        public static void Flush(long pFullStartTicks = 0L)
        {
            if (!Bench.bench_enabled)
            {
                Reset();
                return;
            }

            long totalTicks = 0L;
            int totalCalls = 0;
            for (int i = 0; i < s_ticks.Length; i++)
            {
                if (!UpdateAgeBenchmarkRules.IsTopLevelIndex(i)) continue;
                totalTicks += s_ticks[i];
                totalCalls += s_counts[i];
            }

            if (pFullStartTicks > 0L)
            {
                long fullTicks = Stopwatch.GetTimestamp() - pFullStartTicks;
                Save(UpdateAgeBenchmarkRules.FullWall, fullTicks, 1, UpdateAgeBenchmarkRules.ParentGroup);
                Save(UpdateAgeBenchmarkRules.UnaccountedWall, Math.Max(0L, fullTicks - totalTicks), 1,
                    UpdateAgeBenchmarkRules.ParentGroup);
            }

            Save(UpdateAgeBenchmarkRules.Total, totalTicks, totalCalls, UpdateAgeBenchmarkRules.ParentGroup);
            for (int i = 0; i < UpdateAgeBenchmarkRules.EntryIds.Length; i++)
            {
                Save(UpdateAgeBenchmarkRules.EntryIds[i], s_ticks[i], s_counts[i],
                    UpdateAgeBenchmarkRules.ParentForIndex(i));
            }

            Reset();
        }

        private static void Save(string pId, long pTicks, int pCalls, string pGroup)
        {
            double seconds = pTicks <= 0L ? 0.0 : (double)pTicks / Stopwatch.Frequency;
            Bench.benchSaveSplit(pId, seconds, pCalls, pGroup);
            Bench.saveAverageCounter(pId, pGroup);
        }

        private static void Reset()
        {
            for (int i = 0; i < s_ticks.Length; i++)
            {
                s_ticks[i] = 0L;
                s_counts[i] = 0;
            }
        }
    }
}
