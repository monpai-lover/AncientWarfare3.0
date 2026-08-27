using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class RegionalGovernmentAggregationService
    {
        private static readonly Dictionary<long,
            IReadOnlyList<RegionalGovernmentReadModel>> Cache =
            new Dictionary<long, IReadOnlyList<RegionalGovernmentReadModel>>();
        private static readonly Dictionary<long,
            Dictionary<long, RegionalGovernmentReadModel>> RegionByCityCache =
            new Dictionary<long, Dictionary<long, RegionalGovernmentReadModel>>();

        internal static IReadOnlyList<RegionalGovernmentReadModel> Build(
            Kingdom pKingdom, string pRegionTitle = null,
            string pGovernorTitle = null)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return Array.Empty<RegionalGovernmentReadModel>();
            CustomCourtRuntime.RegionalTitles(pKingdom,
                out string configuredRegionTitle,
                out string configuredGovernorTitle,
                out string configuredLocalLevelTitle);
            if (string.IsNullOrWhiteSpace(pRegionTitle))
                pRegionTitle = configuredRegionTitle;
            if (string.IsNullOrWhiteSpace(pGovernorTitle))
                pGovernorTitle = configuredGovernorTitle;
            if (Cache.TryGetValue(pKingdom.id,
                    out IReadOnlyList<RegionalGovernmentReadModel> cached))
            {
                RefreshCachedGovernorActors(pKingdom, cached);
                return cached;
            }
            IReadOnlyList<RegionalGovernmentReadModel> legal =
                DeJureRegionReadModelService.Build(pKingdom, pRegionTitle,
                    pGovernorTitle, configuredLocalLevelTitle);
            if (legal.Count > 0)
            {
                SetCached(pKingdom.id, legal);
                return legal;
            }
            if (!DeJureRegionRetirementRules.ShouldUseInferredRegions(
                    hasActiveLegalRegions: false,
                    hasExplicitRetirement: DeJureRegionStore.
                        HasExplicitRegionRetirement(pKingdom.id)))
            {
                IReadOnlyList<RegionalGovernmentReadModel> empty =
                    Array.Empty<RegionalGovernmentReadModel>();
                SetCached(pKingdom.id, empty);
                return empty;
            }

            var cities = new List<City>();
            var facts = new List<RegionalGovernmentCityFact>();
            try
            {
                foreach (City city in pKingdom.getCities() ??
                         Array.Empty<City>())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom != pKingdom ||
                        !DeJureRegionStore.IsEligibleCityId(city.data.id))
                        continue;
                    cities.Add(city);
                    var neighbors = new List<long>();
                    IEnumerable<City> neighborSource =
                        city.neighbours_cities_kingdom ??
                        (IEnumerable<City>)Array.Empty<City>();
                    foreach (City neighbor in neighborSource)
                        if (neighbor?.data != null && !neighbor.isRekt() &&
                            neighbor.kingdom == pKingdom &&
                            DeJureRegionStore.IsEligibleCityId(
                                neighbor.data.id))
                            neighbors.Add(neighbor.data.id);
                    facts.Add(new RegionalGovernmentCityFact
                    {
                        KingdomId = pKingdom.id,
                        CityId = city.data.id,
                        CityName = DeJureRegionStore.
                            ResolveCountyNameForPresentation(city),
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
                    LegalSeatCityId = group.SeatCityId,
                    EffectiveSeatCityId = group.SeatCityId,
                    RegionTitle = string.IsNullOrWhiteSpace(pRegionTitle)
                        ? RegionalGovernmentRules.DefaultRegionTitle
                        : pRegionTitle.Trim(),
                    GovernorTitle = string.IsNullOrWhiteSpace(pGovernorTitle)
                        ? "州牧" : pGovernorTitle.Trim(),
                    LocalLevelTitle = configuredLocalLevelTitle,
                    RegionName = RegionalGovernmentRules.RegionName(
                        group.SeatCityName, pRegionTitle),
                    GovernorActorId = LocalGovernorIdentityRules.ResolveRegionalGovernorActorId(
                        seat?.kingdom == pKingdom,
                        seat?.leader?.data?.id ?? -1L,
                        seat?.leader != null && seat.leader.isAlive() &&
                        !seat.leader.isRekt() && seat.leader.isCityLeader()),
                    MemberCityIds = group.MemberCityIds.ToList(),
                    LocalGovernmentCityIds = group.MemberCityIds.ToList()
                });
            }
            cached = result.OrderBy(region => region.SeatCityId).ToArray();
            SetCached(pKingdom.id, cached);
            return cached;
        }

        internal static bool TryFindRegion(Kingdom pKingdom, long pCityId,
            out RegionalGovernmentReadModel pRegion)
        {
            pRegion = null;
            if (pKingdom?.data == null || pCityId < 0L) return false;
            Build(pKingdom);
            return RegionByCityCache.TryGetValue(pKingdom.id,
                    out Dictionary<long, RegionalGovernmentReadModel> index) &&
                index.TryGetValue(pCityId, out pRegion);
        }

        internal static void Invalidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            Cache.Remove(pKingdom.id);
            RegionByCityCache.Remove(pKingdom.id);
        }

        internal static void Clear()
        {
            Cache.Clear();
            RegionByCityCache.Clear();
        }

        private static void SetCached(long pKingdomId,
            IReadOnlyList<RegionalGovernmentReadModel> pRegions)
        {
            Cache[pKingdomId] = pRegions ??
                Array.Empty<RegionalGovernmentReadModel>();
            var index = new Dictionary<long, RegionalGovernmentReadModel>();
            foreach (RegionalGovernmentReadModel region in Cache[pKingdomId])
            {
                if (region?.MemberCityIds == null) continue;
                foreach (long cityId in region.MemberCityIds)
                    if (cityId >= 0L && !index.ContainsKey(cityId))
                        index[cityId] = region;
            }
            RegionByCityCache[pKingdomId] = index;
        }

        private static void RefreshCachedGovernorActors(Kingdom pKingdom,
            IReadOnlyList<RegionalGovernmentReadModel> pRegions)
        {
            if (pKingdom?.data == null || pRegions == null) return;
            foreach (RegionalGovernmentReadModel region in pRegions)
            {
                if (region == null || region.EffectiveSeatCityId < 0L)
                    continue;
                City seat = null;
                try { seat = World.world?.cities?.get(
                    region.EffectiveSeatCityId); }
                catch { }
                bool controlled = seat?.data != null && !seat.isRekt() &&
                    seat.kingdom == pKingdom;
                bool live = controlled && seat.leader != null &&
                    seat.leader.data != null;
                if (live)
                {
                    try { live = seat.leader.isAlive() &&
                        !seat.leader.isRekt() && seat.leader.isCityLeader(); }
                    catch { live = false; }
                }
                region.GovernorActorId =
                    LocalGovernorIdentityRules.ResolveRegionalGovernorActorId(
                        controlled, seat?.leader?.data?.id ?? -1L, live);
            }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }
    }
}
