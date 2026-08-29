using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AncientWarfare3.core.schools
{
    /// <summary>
    /// 学派写缓冲的成本归因。
    ///
    /// 实测 write_buffer 占学派耗时的 74~98%(峰值 139ms/区间),而学派又是
    /// authority 唯一的大头。但 write_buffer 本身只是执行者 —— 要知道该动谁,
    /// 得看这些写入是谁产生的,以及成本落在"执行语句"还是"提交事务"上。
    ///
    /// 按 OperationKey 的前缀聚合(membership-join / guest-end / ...),跨帧累计、
    /// 发日志时取走。纯观测。
    /// </summary>
    internal static class HistoricalSchoolWriteDiagnostics
    {
        private static readonly Dictionary<string, long[]> OperationCost =
            new Dictionary<string, long[]>(StringComparer.Ordinal);
        private static long _commitTicks;
        private static long _commitCount;

        internal static void AccountOperation(string pKey, long pStarted)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            if (pStarted == 0L) return;
            long elapsed = Stopwatch.GetTimestamp() - pStarted;
            if (elapsed < 0L) return;
            string prefix = ResolvePrefix(pKey);
            lock (OperationCost)
            {
                if (!OperationCost.TryGetValue(prefix, out long[] entry))
                {
                    entry = new long[2];
                    OperationCost[prefix] = entry;
                }

                entry[0] += elapsed;
                entry[1]++;
            }
        }

        internal static void AccountCommit(long pStarted)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            if (pStarted == 0L) return;
            long elapsed = Stopwatch.GetTimestamp() - pStarted;
            if (elapsed < 0L) return;
            lock (OperationCost)
            {
                _commitTicks += elapsed;
                _commitCount++;
            }
        }

        /// <summary>OperationKey 形如 "membership-join:123" 或
        /// "guest-end:v1|actor=456",取第一个分隔符之前的部分。</summary>
        private static string ResolvePrefix(string pKey)
        {
            if (string.IsNullOrEmpty(pKey)) return "<none>";
            int cut = pKey.IndexOfAny(new[] { ':', '|' });
            return cut <= 0 ? pKey : pKey.Substring(0, cut);
        }

        internal static string TakeDiagnostics()
        {
            lock (OperationCost)
            {
                long commitTicks = _commitTicks;
                long commitCount = _commitCount;
                _commitTicks = 0L;
                _commitCount = 0L;
                if (OperationCost.Count == 0 && commitCount == 0)
                    return "none";

                var ranked = new List<KeyValuePair<string, long[]>>(
                    OperationCost);
                OperationCost.Clear();
                ranked.Sort((left, right) =>
                {
                    int byTicks = right.Value[0].CompareTo(left.Value[0]);
                    return byTicks != 0
                        ? byTicks
                        : string.CompareOrdinal(left.Key, right.Key);
                });

                var text = new System.Text.StringBuilder();
                text.Append("commit:").Append(Format(commitTicks))
                    .Append('/').Append(commitCount);
                int limit = Math.Min(12, ranked.Count);
                for (int i = 0; i < limit; i++)
                {
                    text.Append(',').Append(ranked[i].Key).Append(':')
                        .Append(Format(ranked[i].Value[0]))
                        .Append('/').Append(ranked[i].Value[1]);
                }

                return text.ToString();
            }
        }

        private static string Format(long pTicks)
        {
            return (pTicks * 1000.0 / Stopwatch.Frequency)
                .ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
