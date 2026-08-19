using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRefugeeThreatService
    {
        internal sealed class CityThreatInput
        {
            public long CityId { get; set; }
            public int Population { get; set; }
            public int Eligible { get; set; }
            public int MinimumPopulation { get; set; }
            public int CityBudget { get; set; }
            public int WorldBudget { get; set; }
            public WarRefugeeThreatFacts Facts { get; set; }
        }

        internal sealed class CityThreatResult
        {
            public long CityId;
            public int Quota;
            public int Permille;
        }

        internal static IReadOnlyList<CityThreatResult> ProcessMonthly(
            IEnumerable<CityThreatInput> pCities)
        {
            var result = new List<CityThreatResult>();
            if (pCities == null) return result;
            foreach (CityThreatInput city in pCities)
            {
                if (city == null || city.CityId < 0L) continue;
                int permille = WarRefugeeRules.DeparturePermille(city.Facts,
                    city.CityId);
                result.Add(new CityThreatResult
                {
                    CityId = city.CityId,
                    Permille = permille,
                    Quota = WarRefugeeRules.DepartureQuota(city.Population,
                        city.Eligible, city.MinimumPopulation, city.CityBudget,
                        city.WorldBudget, permille)
                });
            }
            return result;
        }
    }
}
