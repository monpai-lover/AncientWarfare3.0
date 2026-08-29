using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 头衔继承的「预定继承人」通用校验。
    ///
    /// 四条继承路径(王位 / 封国 / 爵位头衔 / 虚衔)都改成「现任死之前把继承人
    /// 找好并定死,死亡时直接让他继承;继承人死亡等意外由事件驱动重新找一个」。
    /// 死亡那一刻只做一次廉价校验,不再跑全量旁系扫描 —— 实测那个扫描单次
    /// 60.9ms(succession:reconcile_heir),占 annual_succession 的 99.8%。
    ///
    /// 判定集中在这里而不是各路径各写一份,是为了让「预定还算不算有效」只有一个
    /// 答案;四条路径的候选资格细则各自不同,但这几条硬门槛是共通的。
    ///
    /// 预定失效时调用方退回搜索一次(最坏退化成改动前的行为),绝不因为漏掉一个
    /// 事件就让头衔白白绝嗣。Account 记录命中/兜底,兜底占比就是这套设计是否真的
    /// 生效的直接证据。
    /// </summary>
    internal static class TitleSuccessionDesignation
    {
        private static readonly Dictionary<string, long[]> PathCounts =
            new Dictionary<string, long[]>(StringComparer.Ordinal);

        /// <summary>
        /// 预定继承人是否仍可继承。pContext 是头衔所属王国:各路径的转移逻辑都
        /// 要求继承人当前就在这个王国里(例如 FeudatoryService.TransferPrince
        /// 直接要求 pSuccessor.kingdom == pEmpire),所以这里一并校验。
        /// </summary>
        internal static bool TryResolve(long pDesignatedId, Kingdom pContext,
            out Actor pSuccessor)
        {
            pSuccessor = null;
            if (pDesignatedId < 0L || pContext?.data == null ||
                pContext.isRekt()) return false;
            Actor actor;
            try { actor = World.world?.units?.get(pDesignatedId); }
            catch { return false; }
            if (actor?.data == null) return false;
            try
            {
                if (!actor.isAlive() || actor.isRekt()) return false;
                if (actor.kingdom != pContext) return false;
                if (actor.hasTrait("madness")) return false;
                if (SlaveService.IsSlave(actor)) return false;
            }
            catch { return false; }
            pSuccessor = actor;
            return true;
        }

        /// <summary>
        /// 记一次继承结算:pHit 为 true 表示用上了预定,false 表示预定失效、
        /// 退回搜索。跨帧累计,发日志时取走。
        /// </summary>
        internal static void Account(string pPath, bool pHit)
        {
            if (!AncientWarfare3.core.performance.AWDiagnosticsGate.Enabled)
                return;
            if (string.IsNullOrEmpty(pPath)) return;
            lock (PathCounts)
            {
                if (!PathCounts.TryGetValue(pPath, out long[] entry))
                {
                    entry = new long[2];
                    PathCounts[pPath] = entry;
                }

                entry[pHit ? 0 : 1]++;
            }
        }

        /// <summary>形如 路径:命中/兜底。</summary>
        internal static string TakeDiagnostics()
        {
            lock (PathCounts)
            {
                if (PathCounts.Count == 0) return "none";
                var ranked = new List<KeyValuePair<string, long[]>>(PathCounts);
                PathCounts.Clear();
                ranked.Sort((left, right) =>
                {
                    long leftTotal = left.Value[0] + left.Value[1];
                    long rightTotal = right.Value[0] + right.Value[1];
                    int byTotal = rightTotal.CompareTo(leftTotal);
                    return byTotal != 0
                        ? byTotal
                        : string.CompareOrdinal(left.Key, right.Key);
                });
                var parts = new string[ranked.Count];
                for (int i = 0; i < ranked.Count; i++)
                    parts[i] = ranked[i].Key + ":" +
                        ranked[i].Value[0].ToString(
                            CultureInfo.InvariantCulture) + "/" +
                        ranked[i].Value[1].ToString(
                            CultureInfo.InvariantCulture);
                return string.Join(",", parts);
            }
        }
    }
}
