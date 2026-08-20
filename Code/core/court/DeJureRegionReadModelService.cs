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
            var controlled = (pKingdom.getCities() ?? Array.Empty<City>())
                .Where(p => p?.data != null && !p.isRekt() &&
                            p.kingdom == pKingdom &&
                            !PeasantRebelBanditStrongholdService.
                                IsStrongholdCity(p))
                .ToDictionary(p => p.data.id);
            if (controlled.Count == 0) return Array.Empty<RegionalGovernmentReadModel>();

            var result = new List<RegionalGovernmentReadModel>();
            foreach (DeJureRegion legal in DeJureRegionStore.ActiveRegions())
            {
                List<long> members = (legal.MemberCityIds ?? new List<long>())
                    .Where(controlled.ContainsKey).OrderBy(p => p).ToList();
                if (members.Count == 0) continue;
                List<City> allMembers = (legal.MemberCityIds ??
                    new List<long>()).Select(p => World.world?.cities?.get(p))
                    .Where(p => p?.data != null && !p.isRekt() &&
                        !PeasantRebelBanditStrongholdService.
                            IsStrongholdCity(p)).ToList();
                City seat = allMembers.FirstOrDefault(p =>
                    p.data.id == legal.SeatCityId);
                var counts = new Dictionary<long, int>();
                foreach (City city in allMembers)
                {
                    long kingdomId = city.kingdom?.data?.id ?? -1L;
                    counts[kingdomId] = counts.TryGetValue(kingdomId,
                        out int current) ? current + 1 : 1;
                }
                var model = new RegionalGovernmentReadModel
                {
                    KingdomId = pKingdom.id,
                    RegionId = legal.RegionId,
                    SeatCityId = legal.SeatCityId,
                    RegionName = legal.RegionName ?? string.Empty,
                    RegionTitle = pRegionTitle ?? string.Empty,
                    GovernorTitle = pGovernorTitle ?? string.Empty,
                    LocalLevelTitle = pLocalLevelTitle ?? string.Empty,
                    GovernorActorId = LocalGovernorIdentityRules.ResolveRegionalGovernorActorId(
                        seat?.kingdom == pKingdom,
                        seat?.leader?.data?.id ?? -1L,
                        seat?.leader != null && seat.leader.isAlive() &&
                        !seat.leader.isRekt()),
                    MemberCityIds = members,
                    LocalGovernmentCityIds = members.ToList(),
                    TotalMemberCount = allMembers.Count,
                    ControlledMemberCount = members.Count,
                    ForeignMemberCount = Math.Max(0, allMembers.Count - members.Count),
                    IsSeatControlled = seat?.kingdom == pKingdom,
                    HasForeignDeJureMembers = allMembers.Any(p =>
                        p.kingdom != pKingdom),
                    ControllerMemberCounts = counts
                };
                result.Add(model);
            }
            return result.OrderBy(p => p.SeatCityId).ToArray();
        }

    }
}
