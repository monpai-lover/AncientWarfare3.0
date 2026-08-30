using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    /// <summary>
    /// 城官共享候选表的规则。
    ///
    /// 表按<b>行为类</b>(品级 + 方镇标志 + 通道)建一次,同类席位共用 ——
    /// 见 <see cref="CandidatePoolBehavior"/>。表里的评分**不含籍贯加成**,
    /// 因为那一项按城变;取人时再把加成算回去。
    ///
    /// 关键问题:共享表按无加成的分排序,而实际要的是有加成的第一名。
    /// 加成有上限 <see cref="LocalOfficialCandidateRules.HometownBonus"/>,
    /// 所以答案必然落在「无加成分 &gt;= 最佳有加成分 - 加成上限」这一段里。
    /// 只要取到这一段就够了 —— 再往后的人即使拿满加成也追不上。
    ///
    /// 这不是近似:<see cref="Rerank"/> 对这一段做完整的有加成排序,结果与
    /// 「对全体算加成后排序取第一」逐项相同。截断只发生在**不可能改变答案**
    /// 的位置,由 <see cref="NeedsMoreForHometownBonus"/> 判定。
    /// </summary>
    internal static class CityShortlistRules
    {
        /// <summary>
        /// 还要不要继续往下看。<paramref name="pBestWithBonus"/> 是目前算出的
        /// 最佳有加成分,<paramref name="pNextWithoutBonus"/> 是共享表里下一个
        /// 人的无加成分。后者加满上限仍追不上前者时,后面的人只会更低
        /// (表按无加成分降序),可以停。
        /// </summary>
        internal static bool NeedsMoreForHometownBonus(int pBestWithBonus,
            int pNextWithoutBonus, int pBonus)
        {
            return pNextWithoutBonus + pBonus >= pBestWithBonus;
        }

        /// <summary>
        /// 同一档次内按分降序、同分按 id 升序。档次优先于分 —— 和
        /// <see cref="CountyShortlistRules.SortsBefore"/> 同一套口径,
        /// 所以城县两条分支的择优标准一致。
        /// </summary>
        internal static bool SortsBefore(int pTierA, int pScoreA, long pIdA,
            int pTierB, int pScoreB, long pIdB)
        {
            return CountyShortlistRules.SortsBefore(pTierA, pScoreA, pIdA,
                pTierB, pScoreB, pIdB);
        }

        /// <summary>
        /// 参考实现:给定共享表(已按无加成分排好)和每人是否同籍贯,
        /// 算出加上籍贯加成后的第一名。供随机对拍证明「扫描前缀」与
        /// 「全体重排」等价。
        ///
        /// 返回 -1 表示表为空。<paramref name="pReserved"/> 里的人跳过 ——
        /// 占用是逐席位变化的,不能进共享表。
        /// </summary>
        internal static int PickWithHometownBonus(IReadOnlyList<int> pTiers,
            IReadOnlyList<int> pScores, IReadOnlyList<long> pIds,
            IReadOnlyList<bool> pSameCity, ISet<long> pReserved, int pBonus)
        {
            if (pTiers == null || pScores == null || pIds == null ||
                pSameCity == null) return -1;
            int best = -1;
            int bestTier = 0;
            int bestScore = 0;
            for (int index = 0; index < pIds.Count; index++)
            {
                // 档次不受加成影响,所以一旦跨到更差的档次就可以停 ——
                // 更差档次的人无论多少分都排在后面。
                if (best >= 0 && pTiers[index] > bestTier) break;
                if (best >= 0 && pTiers[index] == bestTier &&
                    !NeedsMoreForHometownBonus(bestScore, pScores[index],
                        pBonus)) break;
                if (pReserved != null && pReserved.Contains(pIds[index]))
                    continue;
                int score = pScores[index] +
                    (pSameCity[index] ? pBonus : 0);
                if (best < 0 || SortsBefore(pTiers[index], score, pIds[index],
                        bestTier, bestScore, pIds[best]))
                {
                    best = index;
                    bestTier = pTiers[index];
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
