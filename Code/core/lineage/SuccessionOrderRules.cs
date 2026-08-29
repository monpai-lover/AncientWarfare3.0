namespace AncientWarfare3.core.lineage
{
    /// <summary>
    ///     继承顺位的排序依据。
    /// </summary>
    public enum SuccessionOrderBasis
    {
        /// <summary>血缘:直系在前、嫡在庶前、同档比长幼。王位、分封、宗族用这个。</summary>
        Bloodline = 0,
        /// <summary>能力:学派道统的「嫡长」看的是学问声望,不是血缘。</summary>
        Ability = 1
    }

    /// <summary>
    ///     通用继承顺位规则。
    ///
    ///     所有继承场景共用一条排序:王位、分封、宗族看血缘,学派看能力。
    ///     **继承人就是顺位池的第一席** —— 不再每次现算,算一次排好,之后按部就班。
    ///
    ///     血缘序的四个键,依次:
    ///       ① 支系   直系(0)在前,旁支(1)在末尾 —— 旁支是兜底,不是竞争者
    ///       ② 嫡庶   嫡在庶前,且**压过长幼**:嫡幼排在庶长之前
    ///       ③ 长幼   同支同档内比出生先后,先出生的在前
    ///       ④ id     全序收尾,保证顺位唯一确定、存读档一致
    ///
    ///     能力序(学派)只有两个键:能力降序,再按 id。
    ///
    ///     ④ 之所以必要:前三个键都可能相等(同母双生的庶子),没有它顺位就不是
    ///     全序,"第一席"会随遍历顺序漂移,存档前后能选出不同的继承人。
    /// </summary>
    public static class SuccessionOrderRules
    {
        public const int DirectLine = 0;
        public const int CollateralLine = 1;

        /// <summary>
        ///     能不能进继承池。只判不随顺位变化的硬条件。
        ///
        ///     女性由专门的继承法控制:法律未开放时不入池;开放后与男性同池,
        ///     按同一套嫡庶长幼排序,不额外加减。
        /// </summary>
        public static bool CanEnterPool(bool pAlive, bool pMale,
            bool pFemaleSuccessionAllowed, bool pSlave, bool pMadness,
            bool pCurrentRuler, bool pSameRealm)
        {
            if (!pAlive || pSlave || pMadness) return false;
            if (pCurrentRuler || !pSameRealm) return false;
            return pMale || pFemaleSuccessionAllowed;
        }

        /// <summary>
        ///     顺位比较:A 是否排在 B 前面。严格全序 —— 任意两个不同的人一定分得出先后。
        /// </summary>
        public static bool SortsBefore(SuccessionOrderBasis pBasis,
            int pBranchA, bool pLegitimateA, double pBirthA, int pAbilityA,
            long pIdA,
            int pBranchB, bool pLegitimateB, double pBirthB, int pAbilityB,
            long pIdB)
        {
            if (pBasis == SuccessionOrderBasis.Ability)
            {
                if (pAbilityA != pAbilityB) return pAbilityA > pAbilityB;
                return pIdA < pIdB;
            }

            // ① 旁支永远在直系之后,不管嫡庶长幼 —— 它是顺位的兜底尾部。
            if (pBranchA != pBranchB) return pBranchA < pBranchB;
            // ② 嫡庶压过长幼:嫡幼排在庶长之前。
            if (pLegitimateA != pLegitimateB) return pLegitimateA;
            // ③ 同支同档内,先出生的在前。
            if (pBirthA != pBirthB) return pBirthA < pBirthB;
            return pIdA < pIdB;
        }

        /// <summary>
        ///     新生子嗣插进顺位池时的落点:排在**同支同档的兄姊之后**,
        ///     而不是简单地追加到池尾。
        ///
        ///     当朝君主新添的嫡子会插到所有庶兄之前、所有嫡兄之后;旁支不受影响,
        ///     仍在末尾。这就是 <see cref="SortsBefore"/> 的直接结果 —— 单独点出来
        ///     是因为它是唯一一处「插入」而非「重排」的场景,容易被写成 Add 到末尾。
        /// </summary>
        public static int FindInsertPosition(SuccessionOrderBasis pBasis,
            int pCount,
            System.Func<int, (int Branch, bool Legitimate, double Birth,
                int Ability, long Id)> pAt,
            int pBranch, bool pLegitimate, double pBirth, int pAbility,
            long pId)
        {
            if (pAt == null || pCount <= 0) return 0;
            int low = 0;
            int high = pCount;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                (int Branch, bool Legitimate, double Birth, int Ability,
                    long Id) other = pAt(middle);
                bool otherFirst = SortsBefore(pBasis, other.Branch,
                    other.Legitimate, other.Birth, other.Ability, other.Id,
                    pBranch, pLegitimate, pBirth, pAbility, pId);
                if (otherFirst) low = middle + 1;
                else high = middle;
            }

            return low;
        }
    }
}
