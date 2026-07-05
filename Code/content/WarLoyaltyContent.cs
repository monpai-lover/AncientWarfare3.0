using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class WarLoyaltyContent
    {
        private const string NON_CORE_LOYALTY_ID = "aw_non_core_territory";

        public static void Init()
        {
            if (AssetManager.loyalty_library == null) return;
            try
            {
                if (AssetManager.loyalty_library.get(NON_CORE_LOYALTY_ID) != null) return;
            }
            catch { }

            AssetManager.loyalty_library.add(new LoyaltyAsset
            {
                id = NON_CORE_LOYALTY_ID,
                translation_key = "aw_loyalty_non_core_territory",
                calc = CalculateNonCorePenalty
            });
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
    }
}
