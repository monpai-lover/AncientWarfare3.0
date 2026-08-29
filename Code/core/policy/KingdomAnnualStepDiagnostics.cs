using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AncientWarfare3.core.policy
{
    /// <summary>
    /// 王国年度工作(kingdom_annual)的分阶段成本归因。
    ///
    /// 实测 deferred_prefix_ms 里 kingdom_annual:168.939/14 是延迟队列最贵的
    /// 前缀(12.1ms/项),而 annual_stage 字段抓到了单次 annual_succession
    /// 88.22ms —— 同区间 worst_frame_ms=100.928、sched_aw3_authority=98.388,
    /// 也就是那个最差帧就是它。
    ///
    /// 已有的两套插桩都看不到里面:
    ///  - RecentFeatureBenchmark 有 _sampling 门控,只在采样帧记录,那次 88ms
    ///    没落在采样帧上(同区间 aw3_total_ms 只有 2.484)。
    ///  - annual_stage 只报最慢的那一个阶段,没有 16 个阶段的分布。
    ///
    /// 所以按 school_steps / court_death_steps 同一套做法:跨帧累计、发日志时
    /// 取走,格式 id:总计/次数/单次最大。只有总计和次数时,样本数一少均值就会被
    /// 误读成单次成本(career_close 和 guest-end 都栽在这上面)。纯观测。
    /// </summary>
    internal static class KingdomAnnualStepDiagnostics
    {
        private static readonly Dictionary<string, long[]> StepCost =
            new Dictionary<string, long[]>(StringComparer.Ordinal);

        /// <summary>接力计时的起点。关闭诊断时返回 0,整条链自动失效。</summary>
        internal static long Mark()
        {
            return AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        /// <summary>记下 pId 这一段,返回新的起点给下一段。</summary>
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
                int limit = Math.Min(14, ranked.Count);
                var parts = new string[limit];
                for (int i = 0; i < limit; i++)
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
