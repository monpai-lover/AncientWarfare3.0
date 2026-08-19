using System;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class WarLoyaltyContent
    {
        private const string NON_CORE_LOYALTY_ID = "aw_non_core_territory";
        private const string BANDIT_PRESSURE_LOYALTY_ID =
            "aw_bandit_pressure";
        private const string LOCAL_CORRUPTION_LOYALTY_ID =
            "aw_local_corruption";

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
            AddIfMissing(new LoyaltyAsset
            {
                id = LOCAL_CORRUPTION_LOYALTY_ID,
                translation_key = "aw_loyalty_local_corruption",
                translation_key_negative =
                    "aw_loyalty_local_corruption_negative",
                calc = CalculateLocalCorruptionPenalty
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

        private static int CalculateLocalCorruptionPenalty(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return 0;
            int score = CorruptionService.ReadCity(pCity).Score;
            return -Math.Max(0, Math.Min(100, score));
        }
    }
}
