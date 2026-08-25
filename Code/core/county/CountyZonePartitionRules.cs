using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.county
{
    public static class CountyZonePartitionRules
    {
        public const int MaximumZonesPerCounty = 25;

        public static IReadOnlyList<IReadOnlyList<long>> Partition(
            IEnumerable<long> pZoneIds,
            IReadOnlyDictionary<long, IReadOnlyList<long>> pAdjacency)
        {
            long[] zones = (pZoneIds ?? Array.Empty<long>()).Distinct()
                .OrderBy(p => p).ToArray();
            var result = new List<IReadOnlyList<long>>();
            if (zones.Length == 0) return result;
            if (zones.Length <= MaximumZonesPerCounty)
            {
                result.Add(zones);
                return result;
            }
            if (pAdjacency == null)
            {
                for (int offset = 0; offset < zones.Length;
                    offset += MaximumZonesPerCounty)
                    result.Add(zones.Skip(offset)
                        .Take(MaximumZonesPerCounty).ToArray());
                return result;
            }

            var remaining = new HashSet<long>(zones);
            while (remaining.Count > 0)
            {
                long seed = remaining.Min();
                var county = new List<long>(MaximumZonesPerCounty);
                var queue = new Queue<long>();
                var queued = new HashSet<long>();
                queue.Enqueue(seed);
                queued.Add(seed);
                while (queue.Count > 0 && county.Count < MaximumZonesPerCounty)
                {
                    long zone = queue.Dequeue();
                    if (!remaining.Remove(zone)) continue;
                    county.Add(zone);
                    if (!pAdjacency.TryGetValue(zone,
                            out IReadOnlyList<long> neighbours)) continue;
                    foreach (long neighbour in neighbours.OrderBy(p => p))
                        if (remaining.Contains(neighbour) && queued.Add(neighbour))
                            queue.Enqueue(neighbour);
                }
                if (county.Count == 0)
                {
                    county.Add(seed);
                    remaining.Remove(seed);
                }
                result.Add(county);
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
