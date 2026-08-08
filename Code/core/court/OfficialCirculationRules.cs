using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public sealed class GovernorRotationFacts
    {
        public GovernorRotationFacts(long actorId, long currentCityId,
            long nativeCityId, int termEndYear)
        {
            ActorId = actorId;
            CurrentCityId = currentCityId;
            NativeCityId = nativeCityId;
            TermEndYear = termEndYear;
        }

        public long ActorId { get; }
        public long CurrentCityId { get; }
        public long NativeCityId { get; }
        public int TermEndYear { get; }
    }

    public sealed class GovernorRotationAssignment
    {
        public GovernorRotationAssignment(long actorId, long currentCityId,
            long destinationCityId)
        {
            ActorId = actorId;
            CurrentCityId = currentCityId;
            DestinationCityId = destinationCityId;
        }

        public long ActorId { get; }
        public long CurrentCityId { get; }
        public long DestinationCityId { get; }
    }

    public static class OfficialCirculationRules
    {
        public const int MaximumRotationsPerKingdomYear = 8;

        public static bool IsRotatingCityOffice(string pOfficeId,
            bool xiaCirculationUnlocked)
        {
            if (pOfficeId == CourtOfficeId.WestMayor) return true;
            return pOfficeId == CourtOfficeId.Governor &&
                   xiaCirculationUnlocked;
        }

        public static bool CanServeCity(long pNativeCityId,
            long pCurrentTermCityId, long pCandidateCityId)
        {
            if (pCandidateCityId < 0) return false;
            if (pNativeCityId >= 0 && pCandidateCityId == pNativeCityId)
                return false;
            return pCurrentTermCityId < 0 ||
                   pCandidateCityId != pCurrentTermCityId;
        }

        public static bool ShouldRotateGovernor(bool pGovernor,
            bool pTermDue, int pRealmCityCount)
        {
            return pGovernor && pTermDue && pRealmCityCount > 1;
        }

        public static bool TryBuildRotationPlan(
            IReadOnlyList<GovernorRotationFacts> pGovernors,
            out IReadOnlyList<GovernorRotationAssignment> pPlan)
        {
            pPlan = Array.Empty<GovernorRotationAssignment>();
            if (pGovernors == null || pGovernors.Count < 2)
                return false;

            List<GovernorRotationFacts> governors = pGovernors
                .Where(p => p != null && p.ActorId >= 0L &&
                            p.CurrentCityId >= 0L)
                .OrderBy(p => p.TermEndYear)
                .ThenBy(p => p.ActorId)
                .ToList();
            if (governors.Count != pGovernors.Count ||
                governors.Select(p => p.ActorId).Distinct().Count() != governors.Count ||
                governors.Select(p => p.CurrentCityId).Distinct().Count() != governors.Count)
                return false;

            long[] destinations = governors.Select(p => p.CurrentCityId)
                .OrderBy(p => p).ToArray();
            long[] selected = new long[governors.Count];
            int[] destinationOwners = Enumerable.Repeat(-1,
                destinations.Length).ToArray();
            for (int governorIndex = 0;
                 governorIndex < governors.Count; governorIndex++)
            {
                var visitedDestinations = new bool[destinations.Length];
                if (!TryMatch(governors, destinations, governorIndex,
                        selected, destinationOwners, visitedDestinations))
                    return false;
            }

            var result = new List<GovernorRotationAssignment>(governors.Count);
            for (int i = 0; i < governors.Count; i++)
                result.Add(new GovernorRotationAssignment(governors[i].ActorId,
                    governors[i].CurrentCityId, selected[i]));
            pPlan = result;
            return true;
        }

        private static bool TryMatch(
            IReadOnlyList<GovernorRotationFacts> pGovernors,
            IReadOnlyList<long> pDestinations, int pGovernorIndex,
            long[] pSelected, int[] pDestinationOwners,
            bool[] pVisitedDestinations)
        {
            GovernorRotationFacts governor = pGovernors[pGovernorIndex];
            for (int i = 0; i < pDestinations.Count; i++)
            {
                long destination = pDestinations[i];
                if (pVisitedDestinations[i] ||
                    !CanServeCity(governor.NativeCityId,
                        governor.CurrentCityId, destination)) continue;
                pVisitedDestinations[i] = true;
                int owner = pDestinationOwners[i];
                if (owner >= 0 && !TryMatch(pGovernors, pDestinations,
                        owner, pSelected, pDestinationOwners,
                        pVisitedDestinations)) continue;
                pDestinationOwners[i] = pGovernorIndex;
                pSelected[pGovernorIndex] = destination;
                return true;
            }
            return false;
        }
    }
}
