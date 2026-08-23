using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionReadModelService
    {
        internal static IReadOnlyList<RegionalGovernmentReadModel> Build(
            Kingdom pKingdom, string pRegionTitle, string pGovernorTitle,
            string pLocalLevelTitle)
        {
            if (pKingdom?.data == null || pKingdom.isRekt())
                return Array.Empty<RegionalGovernmentReadModel>();
            var result = new List<RegionalGovernmentReadModel>();
            foreach (DeJureRegion legal in DeJureRegionStore.ActiveRegions())
            {
                List<City> allMembers = (legal.MemberCityIds ??
                    new List<long>()).Select(p => World.world?.cities?.get(p))
                    .Where(p => p?.data != null && !p.isRekt() &&
                        !PeasantRebelBanditStrongholdService.
                            IsStrongholdCity(p)).ToList();
                var counts = new Dictionary<long, int>();
                foreach (City city in allMembers)
                {
                    long kingdomId = city.kingdom?.data?.id ?? -1L;
                    counts[kingdomId] = counts.TryGetValue(kingdomId,
                        out int current) ? current + 1 : 1;
                }
                Dictionary<long, List<City>> groups = allMembers
                    .Where(p => p.kingdom?.data != null)
                    .GroupBy(p => p.kingdom.data.id)
                    .ToDictionary(p => p.Key, p => p.ToList());
                if (!groups.TryGetValue(pKingdom.id, out List<City> ownMembers) ||
                    ownMembers.Count == 0) continue;
                List<long> members = ownMembers.Select(p => p.data.id)
                    .OrderBy(p => p).ToList();
                long effectiveSeatId = RegionalEffectiveSeatRules.SelectEffectiveSeat(
                    legal.SeatCityId,
                    ownMembers.Select(p => new RegionalSeatCandidate(
                        p.data.id, SafePopulation(p),
                        DevelopmentMapModeService.GetCityScore(p))).ToArray());
                City effectiveSeat = ownMembers.FirstOrDefault(p =>
                    p.data.id == effectiveSeatId);
                var model = new RegionalGovernmentReadModel
                {
                    KingdomId = pKingdom.id,
                    RegionId = legal.RegionId,
                    SeatCityId = legal.SeatCityId,
                    LegalSeatCityId = legal.SeatCityId,
                    EffectiveSeatCityId = effectiveSeatId,
                    RegionName = DeJureRegionStore.ResolveDisplayName(legal),
                    RegionTitle = pRegionTitle ?? string.Empty,
                    GovernorTitle = pGovernorTitle ?? string.Empty,
                    LocalLevelTitle = pLocalLevelTitle ?? string.Empty,
                    GovernorActorId = LocalGovernorIdentityRules.ResolveRegionalGovernorActorId(
                        effectiveSeat != null,
                        effectiveSeat?.leader?.data?.id ?? -1L,
                        effectiveSeat?.leader != null && effectiveSeat.leader.isAlive() &&
                        !effectiveSeat.leader.isRekt()),
                    MemberCityIds = members,
                    LocalGovernmentCityIds = members.ToList(),
                    TotalMemberCount = allMembers.Count,
                    ControlledMemberCount = members.Count,
                    ForeignMemberCount = Math.Max(0, allMembers.Count - members.Count),
                    IsSeatControlled = effectiveSeat != null,
                    HasForeignDeJureMembers = allMembers.Any(p =>
                        p.kingdom != pKingdom),
                    ControllerMemberCounts = counts
                };
                result.Add(model);
            }
            return result.OrderBy(p => p.SeatCityId).ThenBy(p => p.KingdomId)
                .ToArray();
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

    }
}
