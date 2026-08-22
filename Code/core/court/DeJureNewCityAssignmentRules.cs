using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal readonly struct DeJureNewCityRegionCandidate
    {
        internal readonly long RegionId;
        internal readonly bool HasAdjacentSeat;
        internal readonly int AdjacentMemberCount;
        internal readonly long NearestMemberSquaredDistance;
        internal readonly long SeatSquaredDistance;
        internal readonly bool Eligible;

        internal DeJureNewCityRegionCandidate(long pRegionId,
            bool pHasAdjacentSeat, int pAdjacentMemberCount,
            long pNearestMemberSquaredDistance, long pSeatSquaredDistance,
            bool pEligible)
        {
            RegionId = pRegionId;
            HasAdjacentSeat = pHasAdjacentSeat;
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
            return Select(pCandidates, 0);
        }

        internal static long Select(
            IEnumerable<DeJureNewCityRegionCandidate> pCandidates,
            int pSelector)
        {
            List<DeJureNewCityRegionCandidate> eligible = (pCandidates ??
                Array.Empty<DeJureNewCityRegionCandidate>())
                .Where(p => p.Eligible && p.RegionId >= 0L)
                .ToList();
            if (eligible.Count == 0) return -1L;

            List<DeJureNewCityRegionCandidate> adjacentSeats = eligible
                .Where(p => p.HasAdjacentSeat)
                .OrderBy(p => p.RegionId)
                .ToList();
            if (adjacentSeats.Count > 0)
            {
                int index = pSelector == int.MinValue
                    ? 0
                    : (int)((uint)pSelector % (uint)adjacentSeats.Count);
                return adjacentSeats[index].RegionId;
            }

            return eligible
                .OrderBy(p => p.NearestMemberSquaredDistance)
                .ThenBy(p => p.SeatSquaredDistance)
                .ThenBy(p => p.RegionId)
                .First().RegionId;
        }
    }
}
