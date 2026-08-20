using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public readonly struct DeJureWarGoalCityCandidate
    {
        public DeJureWarGoalCityCandidate(long pCityId, int pCost,
            bool pRegionMember, bool pDefenderOwned, bool pOccupied)
        {
            CityId = pCityId;
            Cost = Math.Max(1, pCost);
            RegionMember = pRegionMember;
            DefenderOwned = pDefenderOwned;
            Occupied = pOccupied;
        }

        public long CityId { get; }
        public int Cost { get; }
        public bool RegionMember { get; }
        public bool DefenderOwned { get; }
        public bool Occupied { get; }
    }

    public static class DeJureWarGoalSettlementRules
    {
        public static int[] SelectAffordable(
            IReadOnlyList<DeJureWarGoalCityCandidate> pCandidates,
            int pAvailableWarScore, int pMaximumTerms)
        {
            if (pCandidates == null || pCandidates.Count == 0 ||
                pAvailableWarScore <= 0 || pMaximumTerms <= 0)
                return Array.Empty<int>();
            var indices = new List<int>(pCandidates.Count);
            for (int i = 0; i < pCandidates.Count; i++)
            {
                DeJureWarGoalCityCandidate candidate = pCandidates[i];
                if (candidate.CityId >= 0L && candidate.RegionMember &&
                    candidate.DefenderOwned && candidate.Occupied)
                    indices.Add(i);
            }
            indices.Sort((left, right) =>
            {
                int cost = pCandidates[right].Cost.CompareTo(
                    pCandidates[left].Cost);
                return cost != 0 ? cost : pCandidates[left].CityId
                    .CompareTo(pCandidates[right].CityId);
            });
            int available = Math.Min(
                WarGoalSettlementRules.MaximumRequiredScore,
                pAvailableWarScore);
            var selected = new List<int>(Math.Min(indices.Count,
                pMaximumTerms));
            for (int i = 0; i < indices.Count &&
                            selected.Count < pMaximumTerms; i++)
            {
                int index = indices[i];
                int cost = pCandidates[index].Cost;
                if (cost > available) continue;
                selected.Add(index);
                available -= cost;
            }
            return selected.ToArray();
        }
    }
}
