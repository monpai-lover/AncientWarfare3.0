using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 县令自动补缺的逐级归因。
    ///
    /// 现象:旧存档里县令席位长期显示「空缺」、候选也在,但 AI 永不任命。而
    /// LocalCourtAppointmentService 的县分支每一环单独看都没有针对县的硬阻断
    /// (CourtVacancyKey 含 CountyId 的相等性、按层分派、CanUseLayerCandidate、
    /// CanUseCandidateFacts、CanReceiveFormalCivilAppointment 的
    /// CanUseUnqualifiedFallback 都走得通,候选目录也是全国单位无过滤)。
    ///
    /// 静态读代码已经到头,所以按级计数:每一级过滤后还剩几个候选,以及最终
    /// 走了哪个出口。跨帧累计、发日志时取走。纯观测。
    ///
    /// 关键出口的含义:
    ///   already_held  空缺被判为已占 → 返回 Invalid → 条目会被移出登记表,
    ///                 之后只有年度扫描或少数事件才会重新登记,这正是「永不
    ///                 补人」的一种可能形状。
    ///   no_qualified  候选全被资格闸门挡掉。配合 pool / after_available /
    ///                 after_facts 三个计数就能看出是哪一级清零。
    /// </summary>
    internal static class CountyFillDiagnostics
    {
        private static readonly Dictionary<string, long> Counters =
            new Dictionary<string, long>(StringComparer.Ordinal);

        internal static void Count(string pId, long pAmount = 1L)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            if (string.IsNullOrEmpty(pId)) return;
            lock (Counters)
            {
                Counters.TryGetValue(pId, out long total);
                Counters[pId] = total + pAmount;
            }
        }

        /// <summary>记一个出口并把传入的结果原样返回,便于在 return 处内联。</summary>
        internal static CourtVacancyOutcome Report(string pId,
            CourtVacancyOutcome pOutcome)
        {
            Count(pId);
            return pOutcome;
        }

        internal static string TakeDiagnostics()
        {
            lock (Counters)
            {
                if (Counters.Count == 0) return "none";
                var ranked = new List<KeyValuePair<string, long>>(Counters);
                Counters.Clear();
                ranked.Sort((left, right) =>
                {
                    int byValue = right.Value.CompareTo(left.Value);
                    return byValue != 0
                        ? byValue
                        : string.CompareOrdinal(left.Key, right.Key);
                });
                var parts = new string[ranked.Count];
                for (int i = 0; i < ranked.Count; i++)
                    parts[i] = ranked[i].Key + ":" + ranked[i].Value;
                return string.Join(",", parts);
            }
        }
    }
}
