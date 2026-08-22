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
                int recruitmentQuota = CalculateRecruitmentQuota(city,
                    pKingdom);
                if (PeasantRebelBanditStrongholdService.TryCreateDirect(city,
                        out _, out _, out _, out _, pAllowClaimRedirect: true,
                        pRecruitmentQuota: recruitmentQuota))
                {
                    pKingdom.data.set(
                        LineageKeys.MANDATE_REBEL_BANDIT_SPAWN_LAST_YEAR,
                        year);
                    return;
                }
            }
        }

        private static int CalculateRecruitmentQuota(City pCity,
            Kingdom pOrigin)
        {
            if (pCity?.data == null || pOrigin?.data == null) return 0;
            bool famine;
            try { famine = !pCity.hasAnyFood(); }
            catch { famine = false; }
            bool highCorruption = CorruptionService.ReadCity(pCity).Score >=
                CorruptionRules.HighThreshold;
            if (!famine && !highCorruption) return 0;

            int adults = 0;
            try
            {
                foreach (Actor actor in pCity.units)
                {
                    if (actor?.data == null || actor.isRekt() ||
                        actor.kingdom != pOrigin ||
                        !PeasantRebelBanditSpawnRules.CanRecruitResident(
                            actor.isAdult(),
                            actor.profession_asset?.is_civilian == true,
                            actor.isKing(), actor.isCityLeader(),
                            HeirService.IsCurrentHeir(pOrigin, actor))) continue;
                    adults++;
                }
            }
            catch { return 0; }

            int population;
            try { population = Math.Max(0, pCity.getPopulationPeople()); }
            catch { population = adults; }
            return PeasantRebelBanditSpawnRules.CalculateAnnualRecruitment(
                adults, famine, highCorruption, population);
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
