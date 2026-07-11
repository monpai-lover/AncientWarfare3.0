using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirDirectSonCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly double BirthTime;
        public readonly bool IsAdult;

        public HeirDirectSonCandidate(long actorId, bool eligible, double birthTime, bool isAdult)
        {
            ActorId = actorId;
            Eligible = eligible;
            BirthTime = birthTime;
            IsAdult = isAdult;
        }
    }

    public static class HeirDirectSonRules
    {
        public static long SelectEldestEligibleId(IEnumerable<HeirDirectSonCandidate> pCandidates)
        {
            long bestId = -1L;
            double bestBirth = double.MaxValue;
            if (pCandidates == null) return bestId;

            foreach (HeirDirectSonCandidate candidate in pCandidates)
            {
                if (!candidate.Eligible) continue;
                if (candidate.BirthTime > bestBirth) continue;
                if (candidate.BirthTime == bestBirth && bestId >= 0 && candidate.ActorId >= bestId) continue;
                bestId = candidate.ActorId;
                bestBirth = candidate.BirthTime;
            }

            return bestId;
        }

        public static bool NeedsRefresh(long cachedHeirId, bool cachedEligible,
            long eldestEligibleDirectSonId)
        {
            if (!cachedEligible) return true;
            return eldestEligibleDirectSonId >= 0 && cachedHeirId != eldestEligibleDirectSonId;
        }
    }
}
