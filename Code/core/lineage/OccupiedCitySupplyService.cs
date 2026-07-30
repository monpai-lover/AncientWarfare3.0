using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class OccupiedCitySupplyService
    {
        public static bool CanProvideToRealm(City pCity, Kingdom pRealm)
        {
            bool valid;
            bool ownerMatches;
            try
            {
                valid = pCity?.data != null && !pCity.isRekt() &&
                        pCity.isAlive();
                ownerMatches = valid && pRealm?.data != null &&
                               !pRealm.isRekt() &&
                               pCity.kingdom == pRealm;
            }
            catch
            {
                return false;
            }

            bool enemyFrozenControl = ownerMatches &&
                WarScoreService.IsCityFrozenControlledByEnemySide(
                    pCity, pRealm);
            return OccupiedCitySupplyRules.CanProvideToRealm(valid,
                ownerMatches, enemyFrozenControl);
        }

        internal static void OnFrozenControlChanged(City pCity)
        {
            if (pCity?.data == null) return;
            CityEconomyService.OnRealmSupplyChanged(pCity);
            WartimeGarrisonService.OnRealmSupplyChanged(pCity);
        }
    }
}
