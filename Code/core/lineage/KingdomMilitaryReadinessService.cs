namespace AncientWarfare3.core.lineage
{
    internal static class KingdomMilitaryReadinessService
    {
        public static bool HasReadyStandingCore(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return false;
            bool hasPositiveCore = false;
            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                int required = StandingArmyRules.PeacetimeCore(city.status.warrior_slots);
                if (required <= 0) continue;
                hasPositiveCore = true;
                if (StandingArmyService.CountOrdinaryStanding(city) < required) return false;
            }
            return hasPositiveCore;
        }
    }
}
