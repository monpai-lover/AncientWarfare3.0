namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirCandidateRank
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly bool IsAgnaticDescendantOfKing;
        public readonly int GenerationDelta;
        public readonly double BirthTime;
        public readonly bool IsAdult;
        public readonly bool LegitimateBirth;

        public HeirCandidateRank(long actorId, bool eligible, bool isAgnaticDescendantOfKing,
            int generationDelta, double birthTime, bool isAdult,
            bool legitimateBirth = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            IsAgnaticDescendantOfKing = isAgnaticDescendantOfKing;
            GenerationDelta = generationDelta;
            BirthTime = birthTime;
            IsAdult = isAdult;
            LegitimateBirth = legitimateBirth;
        }
    }

    // 继承人辈分就近查找的纯规则:按"氏族大谱内合法男系 + 辈分"分层与排序。
    // 顺序:直系后裔(由近及远) → 同辈 → 旁系过继;严禁辈分高于国王者。
    public static class HeirGenerationRules
    {
        public const int TierDirectDescendant = 1; // 子/孙/曾孙…(国王男系后裔)
        public const int TierSameGeneration = 2;   // 同辈(兄弟/堂兄弟)
        public const int TierCollateral = 3;        // 旁系过继(更年轻辈分的其它男系)
        public const int TierElderCollateral = 4;   // 长辈旁系(伯叔祖等)——仅当后代/同辈/晚辈旁系都找尽后的末位兜底
        public const int TierIneligible = 99;       // 不合格(非本姓男系等)

        /// <summary>
        ///     pGenerationDelta = 候选辈分 − 国王辈分(沿男系):正=更低辈(晚辈),0=同辈,负=长辈。
        ///     pIsAgnaticDescendantOfKing:候选是否国王本人的男系直系后裔。
        ///     长辈(delta&lt;0)不再直接排除,而是降为末位兜底 tier——后代找尽仍可回溯族谱选旁系长辈,避免误绝嗣。
        /// </summary>
        public static int ClassifyTier(bool pIsAgnaticDescendantOfKing, int pGenerationDelta)
        {
            if (pIsAgnaticDescendantOfKing) return TierDirectDescendant;
            if (pGenerationDelta == 0) return TierSameGeneration;
            if (pGenerationDelta > 0) return TierCollateral;
            return TierElderCollateral; // delta < 0:长辈旁系,末位兜底
        }

        /// <summary>
        ///     候选排序:先比层级(直系&lt;同辈&lt;晚辈旁系&lt;长辈旁系),再比辈分接近度(|delta| 小者优先),
        ///     再比嫡庶(嫡出优先),再比长幼(出生早者优先),最后成年优先。
        ///     返回 &lt;0 表示 A 更优先。
        ///
        ///     嫡庶这一档原来是缺的 —— 只有儿子那一辈走 HeirDirectSonRules 时论嫡庶,
        ///     孙辈及以下只按出生先后。于是庶长孙压过嫡长孙。这里补齐,口径与
        ///     <see cref="SuccessionOrderRules"/> 一致:**嫡庶压过长幼**,嫡幼排在庶长之前。
        /// </summary>
        public static int Compare(
            int pTierA, int pGenerationDeltaA, bool pLegitimateBirthA,
            double pBirthTimeA, bool pIsAdultA,
            int pTierB, int pGenerationDeltaB, bool pLegitimateBirthB,
            double pBirthTimeB, bool pIsAdultB)
        {
            if (pTierA != pTierB) return pTierA.CompareTo(pTierB);
            int absA = pGenerationDeltaA < 0 ? -pGenerationDeltaA : pGenerationDeltaA;
            int absB = pGenerationDeltaB < 0 ? -pGenerationDeltaB : pGenerationDeltaB;
            if (absA != absB) return absA.CompareTo(absB);                             // 辈分越接近国王越优先
            if (pLegitimateBirthA != pLegitimateBirthB)
                return pLegitimateBirthA ? -1 : 1;                                     // 嫡出优先,且压过长幼
            if (pBirthTimeA != pBirthTimeB) return pBirthTimeA.CompareTo(pBirthTimeB); // 长幼(早生)优先
            if (pIsAdultA != pIsAdultB) return pIsAdultA ? -1 : 1;                     // 再论成年
            return 0;
        }

        public static bool IsEligible(int pTier)
        {
            return pTier == TierDirectDescendant || pTier == TierSameGeneration ||
                   pTier == TierCollateral || pTier == TierElderCollateral;
        }

        public static long SelectBestCandidateId(
            System.Collections.Generic.IEnumerable<HeirCandidateRank> pCandidates)
        {
            long bestId = -1L;
            int bestTier = TierIneligible;
            int bestDelta = 0;
            bool bestLegitimate = false;
            double bestBirth = 0;
            bool bestAdult = false;
            if (pCandidates == null) return bestId;

            foreach (HeirCandidateRank candidate in pCandidates)
            {
                if (!candidate.Eligible) continue;
                int tier = ClassifyTier(candidate.IsAgnaticDescendantOfKing,
                    candidate.GenerationDelta);
                if (!IsEligible(tier)) continue;
                if (bestId >= 0 && Compare(
                        tier, candidate.GenerationDelta,
                        candidate.LegitimateBirth, candidate.BirthTime,
                        candidate.IsAdult,
                        bestTier, bestDelta, bestLegitimate, bestBirth,
                        bestAdult) >= 0)
                    continue;
                bestId = candidate.ActorId;
                bestTier = tier;
                bestDelta = candidate.GenerationDelta;
                bestLegitimate = candidate.LegitimateBirth;
                bestBirth = candidate.BirthTime;
                bestAdult = candidate.IsAdult;
            }
            return bestId;
        }
    }
}
