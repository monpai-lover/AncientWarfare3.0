using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal readonly struct DeJureNewCityRegionCandidate
    {
        internal readonly long RegionId;
        internal readonly bool HasAdjacentMember;
        internal readonly int AdjacentMemberCount;
        internal readonly long NearestMemberSquaredDistance;
        internal readonly long SeatSquaredDistance;
        internal readonly bool Eligible;

        internal DeJureNewCityRegionCandidate(long pRegionId,
            bool pHasAdjacentMember, int pAdjacentMemberCount,
            long pNearestMemberSquaredDistance, long pSeatSquaredDistance,
            bool pEligible)
        {
            RegionId = pRegionId;
            HasAdjacentMember = pHasAdjacentMember;
            AdjacentMemberCount = pAdjacentMemberCount;
            NearestMemberSquaredDistance = pNearestMemberSquaredDistance;
            SeatSquaredDistance = pSeatSquaredDistance;
            Eligible = pEligible;
        }
    }

    internal static class DeJureNewCityAssignmentRules
    {
        internal static long Select(
            IEnumerable<DeJureNewCityRegionCandidate> pCandidates)
        {
            DeJureNewCityRegionCandidate? selected = (pCandidates ??
                Array.Empty<DeJureNewCityRegionCandidate>())
                .Where(p => p.Eligible && p.RegionId >= 0L)
                .Where(p => p.HasAdjacentMember)
                .OrderByDescending(p => p.AdjacentMemberCount)
                .ThenBy(p => p.NearestMemberSquaredDistance)
                .ThenBy(p => p.SeatSquaredDistance)
                .ThenBy(p => p.RegionId)
                .Select(p => (DeJureNewCityRegionCandidate?)p)
                .FirstOrDefault();
            return selected?.RegionId ?? -1L;
        }
    }
}
