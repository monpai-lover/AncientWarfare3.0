using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    /// <summary>
    ///     县令候选短名单的排序与截断规则。
    ///
    ///     择优顺序是「门第档次升序、主属性降序、同分按 actor id 升序」。档次来自
    ///     <see cref="LocalLowOfficeVacancyRules.CandidateTier"/>:有功名者
    ///     (Qualified) &gt; 世家(Clan) &gt; 寒门无功名(Ordinary)。科举功名不是
    ///     入仕的唯一凭证 —— 世家子弟本就可以不经科举,从县令这一级的地方官
    ///     起步熬资历往上走,寒门才非走科举不可。城一级的
    ///     <c>SelectCandidate</c> 一直是这么排的,县一级此前**只按主属性排**,
    ///     等于把世家和寒门当成一回事。
    ///
    ///     id 唯一,所以这是个**全序** —— 前 K 名唯一确定,逐个插入维护的短名单
    ///     和「全体排序后取前 K 个」必然一致。把判据抽出来是为了能用随机对拍
    ///     证明这一点:两千多人的全量排序换成有界插入,不能靠肉眼相信它等价。
    /// </summary>
    internal static class CountyShortlistRules
    {
        /// <summary>a 是否排在 b 前面。</summary>
        public static bool SortsBefore(int pTierA, int pAbilityA, long pIdA,
            int pTierB, int pAbilityB, long pIdB)
        {
            if (pTierA != pTierB) return pTierA < pTierB;
            if (pAbilityA != pAbilityB) return pAbilityA > pAbilityB;
            return pIdA < pIdB;
        }

        /// <summary>
        ///     名单已满时,新人排在末位之后就永远轮不到他,可以直接丢弃。
        /// </summary>
        public static bool CanSkipWhenFull(int pTier, int pAbility, long pId,
            int pLastTier, int pLastAbility, long pLastId)
        {
            return !SortsBefore(pTier, pAbility, pId, pLastTier, pLastAbility,
                pLastId);
        }

        /// <summary>
        ///     参考实现,与服务里的插入算法逐步对应,供随机对拍使用。
        /// </summary>
        public static List<long> TopIds(IReadOnlyList<int> pTiers,
            IReadOnlyList<int> pAbilities, IReadOnlyList<long> pIds,
            int pLimit)
        {
            var ids = new List<long>();
            var abilities = new List<int>();
            var tiers = new List<int>();
            if (pTiers == null || pAbilities == null || pIds == null ||
                pLimit <= 0) return ids;
            for (int index = 0; index < pIds.Count; index++)
            {
                int tier = pTiers[index];
                int ability = pAbilities[index];
                long id = pIds[index];
                int count = ids.Count;
                if (count >= pLimit && CanSkipWhenFull(tier, ability, id,
                        tiers[count - 1], abilities[count - 1],
                        ids[count - 1])) continue;
                int position = count;
                while (position > 0 &&
                       !SortsBefore(tiers[position - 1],
                           abilities[position - 1], ids[position - 1],
                           tier, ability, id)) position--;
                ids.Insert(position, id);
                abilities.Insert(position, ability);
                tiers.Insert(position, tier);
                if (ids.Count > pLimit)
                {
                    ids.RemoveAt(ids.Count - 1);
                    abilities.RemoveAt(abilities.Count - 1);
                    tiers.RemoveAt(tiers.Count - 1);
                }
            }
            return ids;
        }
    }
}
