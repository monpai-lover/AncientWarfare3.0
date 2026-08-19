using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class RegionalGovernmentAggregationService
    {
        private static readonly Dictionary<long,
            IReadOnlyList<RegionalGovernmentReadModel>> Cache =
            new Dictionary<long, IReadOnlyList<RegionalGovernmentReadModel>>();

        internal static IReadOnlyList<RegionalGovernmentReadModel> Build(
            Kingdom pKingdom, string pRegionTitle = null,
            string pGovernorTitle = null)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return Array.Empty<RegionalGovernmentReadModel>();
            CustomCourtRuntime.RegionalTitles(pKingdom,
                out string configuredRegionTitle,
                out string configuredGovernorTitle);
            if (string.IsNullOrWhiteSpace(pRegionTitle))
                pRegionTitle = configuredRegionTitle;
            if (string.IsNullOrWhiteSpace(pGovernorTitle))
                pGovernorTitle = configuredGovernorTitle;
            if (Cache.TryGetValue(pKingdom.id, out IReadOnlyList<
                    RegionalGovernmentReadModel> cached)) return cached;

            var cities = new List<City>();
            var facts = new List<RegionalGovernmentCityFact>();
            try
            {
                foreach (City city in pKingdom.getCities() ??
                         Array.Empty<City>())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom) continue;
                    cities.Add(city);
                    var neighbors = new List<long>();
                    IEnumerable<City> neighborSource =
                        city.neighbours_cities_kingdom ??
                        (IEnumerable<City>)Array.Empty<City>();
                    foreach (City neighbor in neighborSource)
                        if (neighbor?.data != null && !neighbor.isRekt() &&
                            neighbor.kingdom == pKingdom)
                            neighbors.Add(neighbor.data.id);
                    facts.Add(new RegionalGovernmentCityFact
                    {
                        KingdomId = pKingdom.id,
                        CityId = city.data.id,
                        CityName = city.data.name ?? string.Empty,
                        Development = DevelopmentMapModeService.GetCityScore(city),
                        Population = SafePopulation(city),
                        NeighborCityIds = neighbors.Distinct().ToArray()
                    });
                }
            }
            catch { return Array.Empty<RegionalGovernmentReadModel>(); }

            IReadOnlyList<RegionalGovernmentFact> groups =
                RegionalGovernmentRules.Build(facts, pRegionTitle);
            var result = new List<RegionalGovernmentReadModel>(groups.Count);
            foreach (RegionalGovernmentFact group in groups)
            {
                City seat = cities.FirstOrDefault(city =>
                    city?.data?.id == group.SeatCityId);
                result.Add(new RegionalGovernmentReadModel
                {
                    KingdomId = pKingdom.id,
                    SeatCityId = group.SeatCityId,
                    RegionTitle = string.IsNullOrWhiteSpace(pRegionTitle)
                        ? RegionalGovernmentRules.DefaultRegionTitle
                        : pRegionTitle.Trim(),
                    GovernorTitle = string.IsNullOrWhiteSpace(pGovernorTitle)
                        ? "郡守" : pGovernorTitle.Trim(),
                    RegionName = RegionalGovernmentRules.RegionName(
                        group.SeatCityName, pRegionTitle),
                    GovernorActorId = seat?.leader?.data?.id ?? -1L,
                    MemberCityIds = group.MemberCityIds.ToList(),
                    LocalGovernmentCityIds = group.MemberCityIds.ToList()
                });
            }
            cached = result.OrderBy(region => region.SeatCityId).ToArray();
            Cache[pKingdom.id] = cached;
            return cached;
        }

        internal static bool TryFindRegion(Kingdom pKingdom, long pCityId,
            out RegionalGovernmentReadModel pRegion)
        {
            pRegion = null;
            foreach (RegionalGovernmentReadModel region in Build(pKingdom))
                if (region.MemberCityIds.Contains(pCityId))
                {
                    pRegion = region;
                    return true;
                }
            return false;
        }

        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data != null) Cache.Remove(pKingdom.id);
        }

        internal static void Clear()
        {
            Cache.Clear();
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }
    }
}
