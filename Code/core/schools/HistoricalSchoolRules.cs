using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    public static class HistoricalSchoolRules
    {
        public const int MaxDescentsPerEligibleYear = 2;

        public static int WaveForOrder(int pOrder)
        {
            switch (pOrder)
            {
                case 1: return 1;
                case 2: return 2;
                case 3:
                case 4: return 3;
                case 5: return 4;
                case 6: return 5;
                default: return 0;
            }
        }

        public static int WaveOpeningYear(int pWave)
        {
            switch (pWave)
            {
                case 1: return 10;
                case 2: return 35;
                case 3: return 70;
                case 4: return 120;
                case 5: return 180;
                default: return int.MaxValue;
            }
        }

        public static int AdvanceEligibleYear(int pCurrentEligibleYear,
            bool pHasLivingXiaCity)
        {
            return pHasLivingXiaCity ? Math.Max(0, pCurrentEligibleYear) + 1 :
                Math.Max(0, pCurrentEligibleYear);
        }

        public static IReadOnlyList<HistoricalSchoolMasterDefinition> SelectDue(
            int pEligibleYear, HistoricalSchoolDescentLedger pLedger, int pLimit = 2)
        {
            if (pLedger == null || pEligibleYear <= 0 || pLimit <= 0)
                return Array.Empty<HistoricalSchoolMasterDefinition>();
            int limit = Math.Min(MaxDescentsPerEligibleYear, pLimit);
            var nextBySchool = HistoricalSchoolMasterRegistry.All
                .Where(p => !pLedger.IsSpawned(p.Id))
                .GroupBy(p => p.SchoolId, StringComparer.Ordinal)
                .Select(p => p.OrderBy(v => v.Order).ThenBy(v => v.RegistryIndex).First())
                .Where(p => pEligibleYear >= WaveOpeningYear(p.Wave))
                .ToList();
            if (nextBySchool.Count == 0) return Array.Empty<HistoricalSchoolMasterDefinition>();

            var localCounts = nextBySchool.Select(p => p.SchoolId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => pLedger.CountForSchool(p), StringComparer.Ordinal);
            var localLastYears = nextBySchool.Select(p => p.SchoolId)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => pLedger.LastSelectionYear(p), StringComparer.Ordinal);
            var result = new List<HistoricalSchoolMasterDefinition>(limit);
            while (result.Count < limit && nextBySchool.Count > 0)
            {
                HistoricalSchoolMasterDefinition selected = nextBySchool
                    .OrderBy(p => localCounts[p.SchoolId])
                    .ThenBy(p => p.Wave)
                    .ThenBy(p => p.Order)
                    .ThenBy(p => localLastYears[p.SchoolId])
                    .ThenBy(p => p.RegistryIndex)
                    .ThenBy(p => p.Id, StringComparer.Ordinal)
                    .First();
                result.Add(selected);
                nextBySchool.Remove(selected);
                localCounts[selected.SchoolId]++;
                localLastYears[selected.SchoolId] = pEligibleYear;
            }
            return result;
        }
    }
}
