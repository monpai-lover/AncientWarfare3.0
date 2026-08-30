using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 共和推举的取人规则。
    ///
    /// 这条路径原来是「全国扫一遍 → 建 List → 整体排序 → 取 [0]」,但三个调用点
    /// 要的只有**第一名**和**有没有人**,排好的后半截从来没人读。排序纯属浪费。
    ///
    /// <see cref="RepublicGovernmentRules.CompareCandidates"/> 最后一项按
    /// <c>ActorId</c> 定胜负,而 id 唯一,所以它是个**全序** —— 全序下
    /// 「一趟扫出最大」与「排完取第一」必然是同一个人。这不是近似,是恒等,
    /// 所以能直接把排序删掉,不必担心并列时换人。
    ///
    /// <see cref="SelectBest"/> 是生产路径唯一的实现;测试拿「排完取第一」跟它
    /// 随机对拍。只有一份实现,也就没有两条路径慢慢分叉的余地。
    /// </summary>
    internal static class RepublicElectorateRules
    {
        /// <summary>
        /// 一趟扫出最优候选的下标,空表返回 -1。
        ///
        /// 判据是 <c>CompareCandidates(候选, 当前最优) &lt; 0</c> —— 严格小于,
        /// 所以完全并列时保留先遇到的那个。全序下不存在真正的并列(id 唯一),
        /// 这一条只是让退化输入(同一个 id 出现两次)也有确定答案。
        /// </summary>
        internal static int SelectBest(IReadOnlyList<RepublicCandidateScore> pScores)
        {
            if (pScores == null) return -1;
            int best = -1;
            for (int index = 0; index < pScores.Count; index++)
            {
                if (best < 0)
                {
                    best = index;
                    continue;
                }

                if (RepublicGovernmentRules.CompareCandidates(
                        pScores[index], pScores[best]) < 0)
                    best = index;
            }

            return best;
        }

        /// <summary>
        /// 上一次推举一个人都没找到时,还要不要再扫一遍。
        ///
        /// <see cref="RepublicGovernmentRules.ShouldRefreshSuccessorDuringStableReign"/>
        /// 在「没有合格继任者」时返回 true,而没找到人恰恰就是这种状态 ——
        /// 于是王国 AI 每跳一次就重扫全国一遍,且注定还是找不到人。空结果必须
        /// 记下来,否则这是个永不收敛的循环。
        ///
        /// 用年份而不是事件代际:能让空选民团变成非空的事件太多(成年、迁入、
        /// 脱籍、出生),挂不全;漏一个就永久不再推举,那是正确性问题。年份是
        /// 保守兜底 —— 最坏情况推迟到下一年,而重扫频率从「每跳」降到「每年一次」。
        /// </summary>
        internal static bool ShouldRescanEmptyElectorate(bool pHasMemo,
            int pMemoYear, int pCurrentYear)
        {
            if (!pHasMemo) return true;
            return pMemoYear != pCurrentYear;
        }

        /// <summary>
        /// 记空结果的年份。年份读不到(<paramref name="pCurrentYear"/> 为负)时
        /// 返回 -1,而 -1 与任何真实年份都不相等,所以下一跳照旧会重扫 ——
        /// 读不到年份就退回原来的行为,不会把王国永久锁死在「不再推举」。
        /// </summary>
        internal static int MemoYearFor(int pCurrentYear)
        {
            return pCurrentYear < 0 ? -1 : pCurrentYear;
        }
    }
}
