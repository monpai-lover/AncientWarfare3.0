using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtHierarchyLayoutRules
    {
        public static IReadOnlyDictionary<string, int> BuildRanks(
            IEnumerable<CustomCourtOffice> offices,
            IEnumerable<CustomCourtEdge> edges)
        {
            var officeById = (offices ?? Array.Empty<CustomCourtOffice>())
                .Where(office => office != null &&
                    !string.IsNullOrEmpty(office.Id))
                .GroupBy(office => office.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.Ordinal);
            var ranks = officeById.ToDictionary(pair => pair.Key,
                pair => BaseRank(pair.Value), StringComparer.Ordinal);
            var adjacency = officeById.Keys.ToDictionary(id => id,
                _ => new List<string>(), StringComparer.Ordinal);
            var indegree = officeById.Keys.ToDictionary(id => id, _ => 0,
                StringComparer.Ordinal);

            foreach (CustomCourtEdge edge in edges ??
                     Array.Empty<CustomCourtEdge>())
            {
                if (edge == null ||
                    edge.Kind != CustomCourtEdgeKind.Management ||
                    !officeById.ContainsKey(edge.FromOfficeId) ||
                    !officeById.ContainsKey(edge.ToOfficeId) ||
                    adjacency[edge.FromOfficeId].Contains(edge.ToOfficeId))
                    continue;
                adjacency[edge.FromOfficeId].Add(edge.ToOfficeId);
                indegree[edge.ToOfficeId]++;
            }

            var ready = indegree.Where(pair => pair.Value == 0)
                .Select(pair => pair.Key)
                .OrderBy(id => ranks[id])
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToList();
            while (ready.Count > 0)
            {
                string manager = ready[0];
                ready.RemoveAt(0);
                foreach (string subordinate in adjacency[manager]
                             .OrderBy(id => id, StringComparer.Ordinal))
                {
                    ranks[subordinate] = Math.Max(ranks[subordinate],
                        ranks[manager] + 1);
                    indegree[subordinate]--;
                    if (indegree[subordinate] != 0) continue;
                    ready.Add(subordinate);
                    ready = ready.OrderBy(id => ranks[id])
                        .ThenBy(id => id, StringComparer.Ordinal).ToList();
                }
            }
            return ranks;
        }

        private static int BaseRank(CustomCourtOffice office)
        {
            if (office?.Layer == CourtOfficeLayer.Military) return 40;
            if (office?.Layer == CourtOfficeLayer.City ||
                office?.Layer == CourtOfficeLayer.Feudatory) return 50;
            if (office == null || office.Grade <= 10) return 10;
            if (office.Grade <= 20) return 20;
            return 30;
        }
    }
}
