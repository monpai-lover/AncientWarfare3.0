using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditSpawnService
    {
        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanMutate() || pKingdom?.data == null ||
                !PeasantRebelBanditSpawnRules.IsEligibleKingdom(
                    pKingdom.isCiv(),
                    PeasantRebelBanditStrongholdService.HasActiveStronghold(
                        pKingdom),
                    pKingdom.isNeutral(), pKingdom.isRekt())) return;

            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_BANDIT_SPAWN_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;

            var candidates = new List<BanditLoyaltyCityCandidate>();
            var cities = new Dictionary<long, City>();
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    int loyalty;
                    try { loyalty = city.getLoyalty(); }
                    catch { continue; }
                    long cityId = city.getID();
                    if (cityId <= 0) continue;
                    candidates.Add(new BanditLoyaltyCityCandidate(
                        cityId, loyalty, true));
                    cities[cityId] = city;
                }
            }
            catch { return; }

            foreach (BanditLoyaltyCityCandidate ignored in candidates
                         .Where(value => value.Loyalty <
                             PeasantRebelBanditSpawnRules.LoyaltyThreshold)
                         .OrderBy(value => value.Loyalty)
                         .ThenBy(value => value.CityId))
            {
                if (!cities.TryGetValue(ignored.CityId, out City city)) continue;
                if (PeasantRebelBanditStrongholdService.TryCreateDirect(city,
                        out _, out _, out _))
                {
                    pKingdom.data.set(
                        LineageKeys.MANDATE_REBEL_BANDIT_SPAWN_LAST_YEAR,
                        year);
                    return;
                }
            }
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
