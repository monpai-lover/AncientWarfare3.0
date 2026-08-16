using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class WarLoyaltyContent
    {
        private const string NON_CORE_LOYALTY_ID = "aw_non_core_territory";
        private const string BANDIT_PRESSURE_LOYALTY_ID =
            "aw_bandit_pressure";

        public static void Init()
        {
            if (AssetManager.loyalty_library == null) return;
            AddIfMissing(new LoyaltyAsset
            {
                id = NON_CORE_LOYALTY_ID,
                translation_key = "aw_loyalty_non_core_territory",
                calc = CalculateNonCorePenalty
            });
            AddIfMissing(new LoyaltyAsset
            {
                id = BANDIT_PRESSURE_LOYALTY_ID,
                translation_key = "aw_loyalty_bandit_pressure",
                calc = CalculateBanditPressurePenalty
            });
        }

        private static void AddIfMissing(LoyaltyAsset pAsset)
        {
            try
            {
                if (AssetManager.loyalty_library.get(pAsset.id) != null)
                    return;
            }
            catch { }
            AssetManager.loyalty_library.add(pAsset);
        }

        private static int CalculateNonCorePenalty(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return 0;
            bool ownedNonCore = WarTerritoryService.IsOwnedNonCore(pCity.kingdom, pCity);
            bool isCapital = false;
            try { isCapital = pCity.isCapitalCity(); }
            catch { }
            return NonCoreLoyaltyRules.CalculatePenalty(ownedNonCore, isCapital);
        }

        private static int CalculateBanditPressurePenalty(City pCity)
        {
            return PeasantRebelBanditPressureRules.LoyaltyPenalty(
                active: PeasantRebelBanditPressureService.
                    IsPressureTarget(pCity),
                targetMatches: true);
        }
    }
}
