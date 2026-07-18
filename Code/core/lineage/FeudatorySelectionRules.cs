using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class FeudatoryCityCandidate
    {
        private readonly long[] _neighborIds;

        public FeudatoryCityCandidate(long pCityId, bool pEligible,
            int pFrontierScore, IReadOnlyList<long> pNeighborIds)
        {
            CityId = pCityId;
            Eligible = pEligible;
            FrontierScore = pFrontierScore;
            int count = pNeighborIds?.Count ?? 0;
            _neighborIds = new long[count];
            for (int i = 0; i < count; i++) _neighborIds[i] = pNeighborIds[i];
        }

        public long CityId { get; }
        public bool Eligible { get; }
        public int FrontierScore { get; }
        public IReadOnlyList<long> NeighborIds => _neighborIds;
    }

    public static class FeudatorySelectionRules
    {
        public static long SelectSeat(
            IReadOnlyList<FeudatoryCityCandidate> pCandidates)
        {
            FeudatoryCityCandidate best = null;
            int count = pCandidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                FeudatoryCityCandidate candidate = pCandidates[i];
                if (candidate == null || !candidate.Eligible) continue;
                if (best == null || Better(candidate, best)) best = candidate;
            }
            return best?.CityId ?? -1L;
        }

        public static long[] SelectConnected(
            IReadOnlyList<FeudatoryCityCandidate> pCandidates, long pSeatId,
            int pMaximum)
        {
            int limit = Math.Max(0, Math.Min(FeudatoryRules.MaximumCities,
                pMaximum));
            if (limit == 0) return Array.Empty<long>();

            FeudatoryCityCandidate seat = Find(pCandidates, pSeatId);
            if (seat == null || !seat.Eligible) return Array.Empty<long>();

            var selected = new List<long>(limit) { pSeatId };
            var selectedIds = new HashSet<long> { pSeatId };
            while (selected.Count < limit)
            {
                FeudatoryCityCandidate best = null;
                int count = pCandidates?.Count ?? 0;
                for (int i = 0; i < count; i++)
                {
                    FeudatoryCityCandidate candidate = pCandidates[i];
                    if (candidate == null || !candidate.Eligible ||
                        selectedIds.Contains(candidate.CityId) ||
                        !Touches(candidate, selectedIds))
                        continue;
                    if (best == null || Better(candidate, best)) best = candidate;
                }
                if (best == null) break;
                selected.Add(best.CityId);
                selectedIds.Add(best.CityId);
            }
            return selected.ToArray();
        }

        private static FeudatoryCityCandidate Find(
            IReadOnlyList<FeudatoryCityCandidate> pCandidates, long pCityId)
        {
            int count = pCandidates?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                FeudatoryCityCandidate candidate = pCandidates[i];
                if (candidate?.CityId == pCityId) return candidate;
            }
            return null;
        }

        private static bool Touches(FeudatoryCityCandidate pCandidate,
            HashSet<long> pSelected)
        {
            for (int i = 0; i < pCandidate.NeighborIds.Count; i++)
                if (pSelected.Contains(pCandidate.NeighborIds[i])) return true;
            return false;
        }

        private static bool Better(FeudatoryCityCandidate pLeft,
            FeudatoryCityCandidate pRight)
        {
            if (pLeft.FrontierScore != pRight.FrontierScore)
                return pLeft.FrontierScore > pRight.FrontierScore;
            return pLeft.CityId < pRight.CityId;
        }
    }
}
