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
                .Where(p => p.Eligible && p.RegionId >= 0L &&
                    p.AdjacentMemberCount > 0)
                .ToList();
            if (eligible.Count == 0) return -1L;
            return eligible
                .OrderByDescending(p => p.AdjacentMemberCount)
                .ThenBy(p => p.SeatSquaredDistance)
                .ThenBy(p => p.RegionId)
                .First().RegionId;
        }
    }

    internal static class DeJureRegionContinuityRules
    {
        internal static IReadOnlyList<long> SelectConnectedMembers(
            long pSeatCityId, IEnumerable<long> pMemberCityIds,
            IReadOnlyDictionary<long, IReadOnlyCollection<long>> pAdjacency,
            int pCapacity)
        {
            if (pSeatCityId < 0L || pCapacity <= 0)
                return Array.Empty<long>();
            var members = new HashSet<long>((pMemberCityIds ??
                Array.Empty<long>()).Where(id => id >= 0L));
            if (!members.Contains(pSeatCityId)) return Array.Empty<long>();

            var result = new List<long>(Math.Min(pCapacity, members.Count));
            var queued = new HashSet<long> { pSeatCityId };
            var queue = new Queue<long>();
            queue.Enqueue(pSeatCityId);
            while (queue.Count > 0 && result.Count < pCapacity)
            {
                long current = queue.Dequeue();
                result.Add(current);
                if (pAdjacency == null ||
                    !pAdjacency.TryGetValue(current, out
                        IReadOnlyCollection<long> neighbors)) continue;
                foreach (long neighbor in (neighbors ?? Array.Empty<long>())
                         .Where(members.Contains).OrderBy(id => id))
                    if (queued.Add(neighbor)) queue.Enqueue(neighbor);
            }
            return result;
        }
    }
}
