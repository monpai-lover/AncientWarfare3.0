using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct HeirDirectSonCandidate
    {
        public readonly long ActorId;
        public readonly bool Eligible;
        public readonly double BirthTime;
        public readonly bool IsAdult;
        public readonly bool LegitimateBirth;

        public HeirDirectSonCandidate(long actorId, bool eligible,
            double birthTime, bool isAdult, bool legitimateBirth = true)
        {
            ActorId = actorId;
            Eligible = eligible;
            BirthTime = birthTime;
            IsAdult = isAdult;
            LegitimateBirth = legitimateBirth;
        }
    }

    public static class HeirDirectSonRules
    {
        public static long SelectEldestEligibleId(IEnumerable<HeirDirectSonCandidate> pCandidates)
        {
            long bestId = -1L;
            double bestBirth = double.MaxValue;
            bool bestLegitimate = false;
            if (pCandidates == null) return bestId;

            foreach (HeirDirectSonCandidate candidate in pCandidates)
            {
                if (!candidate.Eligible) continue;
                if (bestId >= 0 && candidate.LegitimateBirth !=
                    bestLegitimate)
                {
                    if (!candidate.LegitimateBirth) continue;
                }
                else
                {
                    if (candidate.BirthTime > bestBirth) continue;
                    if (candidate.BirthTime == bestBirth && bestId >= 0 &&
                        candidate.ActorId >= bestId) continue;
                }
                bestId = candidate.ActorId;
                bestBirth = candidate.BirthTime;
                bestLegitimate = candidate.LegitimateBirth;
            }

            return bestId;
        }

        public static bool NeedsRefresh(long cachedHeirId, bool cachedEligible,
            bool cachedRelationshipValid, long eldestEligibleDirectSonId)
        {
            if (!cachedEligible) return true;
            if (eldestEligibleDirectSonId >= 0)
                return cachedHeirId != eldestEligibleDirectSonId;
            return !cachedRelationshipValid;
        }

        public static bool IsCachedRelationshipSignatureValid(long cachedHeirId, long currentKingId,
            long signedHeirId, long signedKingId)
        {
            return cachedHeirId >= 0 && currentKingId >= 0 &&
                   cachedHeirId == signedHeirId && currentKingId == signedKingId;
        }

        public static bool NeedsEventDrivenRefresh(bool force,
            bool cachedEligible, bool cachedRelationshipValid,
            bool successionDirty)
        {
            return force || !cachedEligible || !cachedRelationshipValid ||
                   successionDirty;
        }

        public static bool ShouldReconcile(int currentYear, int lastYear, bool successionPending)
        {
            return !successionPending && currentYear != lastYear;
        }
    }
}
