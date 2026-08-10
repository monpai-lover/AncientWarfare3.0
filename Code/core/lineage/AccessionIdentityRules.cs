using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct AccessionCapitalCandidateFact
    {
        public readonly long CityId;
        public readonly bool OwnedByKingdom;
        public readonly bool Alive;
        public readonly bool IsSuccessorHome;
        public readonly int Population;
        public readonly int Zones;

        public AccessionCapitalCandidateFact(long pCityId,
            bool pOwnedByKingdom, bool pAlive, bool pIsSuccessorHome,
            int pPopulation, int pZones)
        {
            CityId = pCityId;
            OwnedByKingdom = pOwnedByKingdom;
            Alive = pAlive;
            IsSuccessorHome = pIsSuccessorHome;
            Population = pPopulation;
            Zones = pZones;
        }
    }

    public static class AccessionIdentityRules
    {
        public static bool ShouldDeferForInitialKingdomCreation(
            bool pUsesManagedSuccession, bool pHasCurrentKing,
            bool pHasCapital, bool pCandidateJoinedKingdom)
        {
            return pUsesManagedSuccession && !pHasCurrentKing &&
                   !pHasCapital && pCandidateJoinedKingdom;
        }

        public static bool ShouldFinalizeDeferredFounding(
            bool pUsesManagedSuccession, bool pHasLivingKing,
            bool pHasValidCapital, bool pKingJoinedKingdom,
            bool pKingLivesInCapital, bool pMonarchyEstablished,
            bool pIsRepublic, bool pIsRepublicLeader)
        {
            return pUsesManagedSuccession && pHasLivingKing &&
                   pHasValidCapital && pKingJoinedKingdom &&
                   pKingLivesInCapital && !pMonarchyEstablished &&
                   !pIsRepublic && !pIsRepublicLeader;
        }

        public static int ResolveDeferredRetryDelay(int pAttempts)
        {
            if (pAttempts <= 0) return 1;
            int shift = pAttempts - 1;
            if (shift > 5) shift = 5;
            return 1 << shift;
        }

        public static long SelectCapitalRepairCandidateId(
            IEnumerable<AccessionCapitalCandidateFact> pCandidates)
        {
            if (pCandidates == null) return -1L;
            long bestId = -1L;
            long bestScore = long.MinValue;
            foreach (AccessionCapitalCandidateFact candidate in pCandidates)
            {
                if (candidate.CityId < 0 || !candidate.OwnedByKingdom ||
                    !candidate.Alive)
                    continue;
                if (candidate.IsSuccessorHome) return candidate.CityId;
                long score = (long)System.Math.Max(0, candidate.Population) *
                             4L + System.Math.Max(0, candidate.Zones);
                if (bestId >= 0 && (score < bestScore ||
                    score == bestScore && candidate.CityId >= bestId))
                    continue;
                bestId = candidate.CityId;
                bestScore = score;
            }
            return bestId;
        }
    }
}
