using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.county
{
    public static class CountyZonePartitionRules
    {
        public const int MaximumZonesPerCounty = 25;
        public const int MaximumCountiesPerCity = 3;

        public static IReadOnlyList<IReadOnlyList<long>> Partition(
            IEnumerable<long> pZoneIds,
            IReadOnlyDictionary<long, IReadOnlyList<long>> pAdjacency)
        {
            long[] zones = (pZoneIds ?? Array.Empty<long>()).Distinct()
                .OrderBy(p => p).ToArray();
            var result = new List<IReadOnlyList<long>>();
            if (zones.Length == 0) return result;
            int countyCount = Math.Min(MaximumCountiesPerCity,
                (zones.Length + MaximumZonesPerCounty - 1) /
                MaximumZonesPerCounty);
            var ordered = new List<long>(zones.Length);
            if (pAdjacency == null)
            {
                ordered.AddRange(zones);
            }
            else
            {
                // Preserve spatial continuity where possible, but build one
                // complete ordering first.  This allows the final county
                // count to be capped at three and the complete city to be
                // divided evenly even when zone adjacency has gaps.
                var remaining = new HashSet<long>(zones);
                while (remaining.Count > 0)
                {
                    var queue = new Queue<long>();
                    var queued = new HashSet<long>();
                    queue.Enqueue(remaining.Min());
                    while (queue.Count > 0)
                    {
                        long zone = queue.Dequeue();
                        if (!remaining.Remove(zone)) continue;
                        ordered.Add(zone);
                        if (!pAdjacency.TryGetValue(zone,
                                out IReadOnlyList<long> neighbours)) continue;
                        foreach (long neighbour in neighbours.OrderBy(p => p))
                            if (remaining.Contains(neighbour) &&
                                queued.Add(neighbour)) queue.Enqueue(neighbour);
                    }
                }
            }
            int baseSize = ordered.Count / countyCount;
            int remainder = ordered.Count % countyCount;
            int offset = 0;
            for (int index = 0; index < countyCount; index++)
            {
                int size = baseSize + (index < remainder ? 1 : 0);
                result.Add(ordered.Skip(offset).Take(size).ToArray());
                offset += size;
            }
            return result;
        }
    }

    public static class CountyNameRules
    {
        public static string Build(string pCityName, int pOrdinal,
            string pHistoricalName)
        {
            string baseName = string.IsNullOrWhiteSpace(pHistoricalName)
                ? pCityName : pHistoricalName;
            baseName = (baseName ?? "\u57ce\u5e02").Trim();
            const string countySuffix = "\u53bf";
            if (!baseName.EndsWith(countySuffix, StringComparison.Ordinal))
                baseName += countySuffix;
            return pOrdinal <= 0 ? baseName : baseName + (pOrdinal + 1);
        }

        public static string PreserveManual(string pCurrent, string pCityName,
            int pOrdinal)
        {
            return string.IsNullOrWhiteSpace(pCurrent)
                ? Build(pCityName, pOrdinal, null) : pCurrent;
        }
    }
}
