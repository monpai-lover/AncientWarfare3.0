using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MassUprisingCityFact
    {
        internal long CityId { get; }
        internal long CultureId { get; }
        internal int Loyalty { get; }
        internal bool CapitalProtected { get; }
        internal IReadOnlyList<long> NeighbourIds { get; }

        internal MassUprisingCityFact(long pCityId, long pCultureId,
            int pLoyalty, bool pCapitalProtected,
            IEnumerable<long> pNeighbourIds)
        {
            CityId = pCityId;
            CultureId = pCultureId;
            Loyalty = pLoyalty;
            CapitalProtected = pCapitalProtected;
            NeighbourIds = (pNeighbourIds ?? Enumerable.Empty<long>())
                .Where(id => id > 0L).Distinct().OrderBy(id => id).ToList();
        }
    }

    internal sealed class MassUprisingCluster
    {
        internal long CultureId { get; }
        internal IReadOnlyList<long> CityIds { get; }
        internal bool HasCore { get; }

        internal MassUprisingCluster(long pCultureId,
            IEnumerable<long> pCityIds, bool pHasCore)
        {
            CultureId = pCultureId;
            CityIds = (pCityIds ?? Enumerable.Empty<long>())
                .Where(id => id > 0L).Distinct().OrderBy(id => id).ToList();
            HasCore = pHasCore;
        }
    }

    internal static class MassUprisingClusterRules
    {
        internal const int CandidateThreshold = 0;
        internal const int CoreThreshold = -50;

        internal static bool IsCapitalProtected(bool pCapitalProtected)
        {
            return pCapitalProtected;
        }

        internal static bool IsCandidate(int pLoyalty,
            bool pCapitalProtected)
        {
            return !pCapitalProtected && pLoyalty < CandidateThreshold;
        }

        internal static bool IsCore(int pLoyalty)
        {
            return pLoyalty < CoreThreshold;
        }

        internal static List<MassUprisingCluster> BuildClusters(
            IEnumerable<MassUprisingCityFact> pFacts)
        {
            var facts = (pFacts ?? Enumerable.Empty<MassUprisingCityFact>())
                .Where(fact => fact != null && fact.CityId > 0L &&
                    fact.CultureId > 0L &&
                    IsCandidate(fact.Loyalty, fact.CapitalProtected))
                .GroupBy(fact => fact.CityId)
                .Select(group => group.First())
                .OrderBy(fact => fact.CityId)
                .ToDictionary(fact => fact.CityId);
            var visited = new HashSet<long>();
            var result = new List<MassUprisingCluster>();
            foreach (MassUprisingCityFact seed in facts.Values
                         .OrderBy(fact => fact.CityId))
            {
                if (!visited.Add(seed.CityId)) continue;
                var queue = new Queue<long>();
                var cityIds = new List<long>();
                queue.Enqueue(seed.CityId);
                while (queue.Count > 0)
                {
                    long currentId = queue.Dequeue();
                    MassUprisingCityFact current = facts[currentId];
                    cityIds.Add(currentId);
                    foreach (long neighbourId in current.NeighbourIds)
                    {
                        if (!facts.TryGetValue(neighbourId,
                                out MassUprisingCityFact neighbour) ||
                            neighbour.CultureId != seed.CultureId ||
                            visited.Contains(neighbourId)) continue;
                        visited.Add(neighbourId);
                        queue.Enqueue(neighbourId);
                    }
                    foreach (MassUprisingCityFact neighbour in facts.Values
                                 .Where(fact => fact.CultureId ==
                                     seed.CultureId &&
                                     fact.NeighbourIds.Contains(currentId)))
                    {
                        if (visited.Add(neighbour.CityId))
                            queue.Enqueue(neighbour.CityId);
                    }
                }
                bool hasCore = cityIds.Any(id => IsCore(facts[id].Loyalty));
                if (hasCore)
                    result.Add(new MassUprisingCluster(seed.CultureId,
                        cityIds, true));
            }
            return result.OrderBy(cluster => cluster.CityIds.First()).ToList();
        }

        internal static string ClusterKey(long pCultureId,
            MassUprisingCluster pCluster)
        {
            if (pCluster == null || pCluster.CityIds.Count == 0)
                return Math.Max(0L, pCultureId) + ":";
            return Math.Max(0L, pCultureId) + ":" +
                string.Join(",", pCluster.CityIds);
        }
    }
}
