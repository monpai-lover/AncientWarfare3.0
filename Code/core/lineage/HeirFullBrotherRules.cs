using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirFullBrotherCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly bool SharesBothParents;
        public readonly double BirthTime;

        public HeirFullBrotherCandidate(long actorId, bool eligible,
            bool sharesBothParents, double birthTime)
        {
            ActorId = actorId;
            Eligible = eligible;
            SharesBothParents = sharesBothParents;
            BirthTime = birthTime;
        }
    }

    public static class HeirFullBrotherRules
    {
        public static long SelectEldestEligibleId(
            IEnumerable<HeirFullBrotherCandidate> pCandidates)
        {
            long bestId = -1L;
            double bestBirthTime = double.MaxValue;
            if (pCandidates == null) return bestId;

            foreach (HeirFullBrotherCandidate candidate in pCandidates)
            {
                if (!candidate.Eligible || !candidate.SharesBothParents)
                    continue;
                if (candidate.BirthTime > bestBirthTime) continue;
                if (candidate.BirthTime == bestBirthTime && bestId >= 0L &&
                    candidate.ActorId >= bestId) continue;
                bestId = candidate.ActorId;
                bestBirthTime = candidate.BirthTime;
            }
            return bestId;
        }
    }
}
