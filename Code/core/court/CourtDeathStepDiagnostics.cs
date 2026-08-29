using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// court-officer-death 延迟项的分阶段成本归因。
    ///
    /// 实测这一项是延迟队列里最贵的:44.913ms / 5 项 = 9.0ms 每项,而
    /// DeferredQueueDrain 整体 205.8ms(单区间峰值 55.9ms)。但把这条路径读
    /// 完只找到两个独立写事务,按每事务约 0.83ms 的实测固定开销只能解释约
    /// 2ms,剩下的看不出来 —— 所以按阶段测。
    ///
    /// 跨帧累计、发日志时取走。和 school_steps / authority_steps 同一套做法:
    /// 每帧清零的计数器只能拍到「采样那一帧」,而尖峰几乎从不落在采样帧上。
    /// 纯观测,不改任何分支。
    /// </summary>
    internal static class CourtDeathStepDiagnostics
    {
        private static readonly Dictionary<string, long[]> StepCost =
            new Dictionary<string, long[]>(StringComparer.Ordinal);

        /// <summary>记一段,并返回新的起点供下一段接力计时。</summary>
        internal static long Account(string pId, long pStarted)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return 0L;
            long now = Stopwatch.GetTimestamp();
            if (pStarted <= 0L) return now;
            long elapsed = now - pStarted;
            if (elapsed < 0L) return now;
            lock (StepCost)
            {
                if (!StepCost.TryGetValue(pId, out long[] entry))
                {
                    entry = new long[3];
                    StepCost[pId] = entry;
                }

                entry[0] += elapsed;
                entry[1]++;
                if (elapsed > entry[2]) entry[2] = elapsed;
            }

            return now;
        }

        internal static string TakeDiagnostics()
        {
            lock (StepCost)
            {
                if (StepCost.Count == 0) return "none";
                var ranked = new List<KeyValuePair<string, long[]>>(StepCost);
                StepCost.Clear();
                ranked.Sort((left, right) =>
                {
                    int byTicks = right.Value[0].CompareTo(left.Value[0]);
                    return byTicks != 0
                        ? byTicks
                        : string.CompareOrdinal(left.Key, right.Key);
                });
                // 形如 id:总计/次数/单次最大。只有总计和次数的话,3 个样本的
                // 均值会被误读成单次成本 —— career_close 实测 77.787/3,到底是
                // 「每次 26ms」还是「一次 75ms + 两次 1.4ms」,这两者要查的东西
                // 完全不同。加上最大值才分得开。
                var parts = new string[ranked.Count];
                for (int i = 0; i < ranked.Count; i++)
                    parts[i] = ranked[i].Key + ":" +
                        Milliseconds(ranked[i].Value[0]) +
                        "/" + ranked[i].Value[1] +
                        "/" + Milliseconds(ranked[i].Value[2]);
                return string.Join(",", parts);
            }
        }

        private static string Milliseconds(long pTicks)
        {
            return (pTicks * 1000.0 / Stopwatch.Frequency)
                .ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
