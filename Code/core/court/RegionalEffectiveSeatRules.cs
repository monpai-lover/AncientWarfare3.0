using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    internal readonly struct RegionalSeatCandidate
    {
        internal RegionalSeatCandidate(long pCityId, int pPopulation,
            float pEconomicScore)
        {
            CityId = pCityId;
            Population = pPopulation;
            EconomicScore = pEconomicScore;
        }

        internal long CityId { get; }
        internal int Population { get; }
        internal float EconomicScore { get; }
    }

    internal static class RegionalEffectiveSeatRules
    {
        internal static long SelectEffectiveSeat(long pLegalSeatCityId,
            IReadOnlyCollection<RegionalSeatCandidate> pControlledMembers)
        {
            if (pControlledMembers == null || pControlledMembers.Count == 0)
                return -1L;
            if (pControlledMembers.Any(p => p.CityId == pLegalSeatCityId))
                return pLegalSeatCityId;
            return pControlledMembers
                .OrderByDescending(p => p.Population)
                .ThenByDescending(p => p.EconomicScore)
                .ThenBy(p => p.CityId)
                .Select(p => p.CityId)
                .First();
        }
    }
}
