using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal readonly struct DeJureNewCityRegionCandidate
    {
        internal readonly long RegionId;
        internal readonly bool HasAdjacentSeat;
        internal readonly int AdjacentSeatCount;
        internal readonly long NearestMemberSquaredDistance;
        internal readonly long SeatSquaredDistance;
        internal readonly bool Eligible;

        internal DeJureNewCityRegionCandidate(long pRegionId,
            bool pHasAdjacentSeat, int pAdjacentSeatCount,
            long pNearestMemberSquaredDistance, long pSeatSquaredDistance,
            bool pEligible)
        {
            RegionId = pRegionId;
            HasAdjacentSeat = pHasAdjacentSeat;
            AdjacentSeatCount = pAdjacentSeatCount;
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
                .Where(p => p.HasAdjacentSeat)
                .OrderBy(p => p.SeatSquaredDistance)
                .ThenBy(p => p.RegionId)
                .Select(p => (DeJureNewCityRegionCandidate?)p)
                .FirstOrDefault();
            return selected?.RegionId ?? -1L;
        }
    }
}
