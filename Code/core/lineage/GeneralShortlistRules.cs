using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 将领候选表的规则。
    ///
    /// 将领的评分比城官的更不安分。它有两类项:
    /// <list type="bullet">
    ///   <item><b>可挂事件的</b> —— 功绩(<c>AwardMerit</c>)、城主、贵族、
    ///         宗室成年非储君。这些都由离散事件改变,能在改变时换位;</item>
    ///   <item><b>会自己漂移的</b> —— 是不是军队长(军队每次重组都可能变)、
    ///         职业、以及战斗属性(随升级连续涨)。这些没有事件可挂。</item>
    /// </list>
    ///
    /// 所以持久表里只放**稳定分**,漂移项在取人时补回。这不是妥协:漂移项
    /// 的总上界是 <see cref="VolatileCap"/>,表按稳定分降序,所以答案必然落在
    /// 「稳定分 + 上界 &gt;= 目前最佳全分」这一段里。再往后的人把漂移项拿满
    /// 也追不上,可以停 —— 和城官那边籍贯加成的做法同一个证明。
    ///
    /// <see cref="PickBest"/> 是参考实现,供随机对拍证明「扫描前缀」与
    /// 「对全体算全分后排序取第一」逐项相同。
    /// </summary>
    internal static class GeneralShortlistRules
    {
        /// <summary>军队长加成 —— 军队重组即变,无事件可挂。</summary>
        internal const int CaptainBonus = 30;

        /// <summary>战斗属性折算的上限。</summary>
        internal const int CombatCap = 15;

        /// <summary>战士职业加成 —— 转职即变。</summary>
        internal const int WarriorBonus = 8;

        /// <summary>
        /// 漂移项能贡献的最大值。停止扫描的判据就靠它 —— 少算会漏掉真正的
        /// 第一名,多算只是多扫几个人,所以这个数**只能偏大不能偏小**。
        /// </summary>
        internal const int VolatileCap = CaptainBonus + CombatCap +
                                         WarriorBonus;

        /// <summary>低于这个分不值得任命 —— 与 RefreshGenerals 的门槛一致。</summary>
        internal const int MinimumAppointScore = 45;

        /// <summary>
        /// 还要不要继续往下看。<paramref name="pNextStable"/> 是表里下一个人的
        /// 稳定分,它把漂移项拿满仍追不上目前最佳全分时就可以停 —— 表按稳定分
        /// 降序,后面的人只会更低。
        /// </summary>
        internal static bool NeedsMoreForVolatile(int pBestFull,
            int pNextStable)
        {
            return pNextStable + VolatileCap >= pBestFull;
        }

        /// <summary>
        /// 稳定分降序,同分按 id 升序。id 唯一,所以这是全序,插入位置唯一
        /// 确定 —— 二分插入才有意义。
        /// </summary>
        internal static bool SortsBefore(int pStableA, long pIdA,
            int pStableB, long pIdB)
        {
            if (pStableA != pStableB) return pStableA > pStableB;
            return pIdA < pIdB;
        }

        /// <summary>
        /// 全分 = 稳定分 + 漂移项。集中在这里,免得取人路径和建表路径各写
        /// 一份然后慢慢分叉。
        /// </summary>
        internal static int FullScore(int pStable, bool pCaptain,
            bool pWarrior, int pCombat)
        {
            int combat = pCombat < 0 ? 0
                : pCombat > CombatCap ? CombatCap : pCombat;
            return pStable + (pCaptain ? CaptainBonus : 0) +
                   (pWarrior ? WarriorBonus : 0) + combat;
        }

        /// <summary>
        /// 参考实现:给定按稳定分排好的表和每人的漂移项,算出全分第一名的
        /// 下标。返回 -1 表示没有可用的人。
        ///
        /// <paramref name="pTaken"/> 里的人跳过 —— 一轮里可能连任命多名将领,
        /// 刚上任的那个不能再被选中,而占用是逐个席位变化的,不进共享表。
        /// </summary>
        internal static int PickBest(IReadOnlyList<int> pStable,
            IReadOnlyList<long> pIds, IReadOnlyList<bool> pCaptain,
            IReadOnlyList<bool> pWarrior, IReadOnlyList<int> pCombat,
            ISet<long> pTaken, int pMinimumScore)
        {
            if (pStable == null || pIds == null || pCaptain == null ||
                pWarrior == null || pCombat == null) return -1;
            int best = -1;
            int bestFull = 0;
            for (int index = 0; index < pIds.Count; index++)
            {
                if (best >= 0 &&
                    !NeedsMoreForVolatile(bestFull, pStable[index])) break;
                // 门槛也能停:稳定分加满漂移项都到不了下限的人,后面的更不行。
                if (pStable[index] + VolatileCap < pMinimumScore) break;
                if (pTaken != null && pTaken.Contains(pIds[index])) continue;
                int full = FullScore(pStable[index], pCaptain[index],
                    pWarrior[index], pCombat[index]);
                if (full < pMinimumScore) continue;
                // 全分相同时取表里在前的那个,**不是**取 id 小的那个。表本身
                // 已经是全序(稳定分降序、同分 id 升序),按位置定胜负就等于沿用
                // 那个序;再引入一条按 id 的判据等于同一次选择里有两套并列规则,
                // 前缀扫描和全量扫描会给出不同的人。
                if (best < 0 || full > bestFull)
                {
                    best = index;
                    bestFull = full;
                }
            }

            return best;
        }
    }
}
