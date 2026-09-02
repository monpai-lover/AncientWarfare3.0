using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirFullBrotherCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly bool SharesBothParents;
        public readonly double BirthTime;
        /// <summary>是否已在本国。不在本国不淘汰,只让位于在本国的同胞兄弟。</summary>
        public readonly bool SameRealm;

        public HeirFullBrotherCandidate(long actorId, bool eligible,
            bool sharesBothParents, double birthTime, bool sameRealm = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            SharesBothParents = sharesBothParents;
            BirthTime = birthTime;
            SameRealm = sameRealm;
        }
    }

    public static class HeirFullBrotherRules
    {
        /// <summary>
        ///     同胞兄弟里的最长者。在本国的先来,组内再论长幼。
        ///
        ///     「在本国」原来是**硬条件**,一个联姻出去、随军在外或流亡的同胞弟
        ///     直接被当作不存在;而正统继承本来允许候选人不在本国(登记时由
        ///     NormalizeHeirForRegistration 归化)。于是出现过明明有胞弟、继承人
        ///     却是空的。改成排序键:有在本国的就还是选他,没有才轮到在外的。
        /// </summary>
        public static long SelectEldestEligibleId(
            IEnumerable<HeirFullBrotherCandidate> pCandidates)
        {
            long bestId = -1L;
            bool bestSameRealm = false;
            double bestBirthTime = double.MaxValue;
            if (pCandidates == null) return bestId;

            foreach (HeirFullBrotherCandidate candidate in pCandidates)
            {
                if (!candidate.Eligible || !candidate.SharesBothParents)
                    continue;
                if (bestId >= 0L && candidate.SameRealm != bestSameRealm)
                {
                    if (!candidate.SameRealm) continue;
                }
                else
                {
                    if (candidate.BirthTime > bestBirthTime) continue;
                    if (candidate.BirthTime == bestBirthTime && bestId >= 0L &&
                        candidate.ActorId >= bestId) continue;
                }
                bestId = candidate.ActorId;
                bestSameRealm = candidate.SameRealm;
                bestBirthTime = candidate.BirthTime;
            }
            return bestId;
        }
    }
}
