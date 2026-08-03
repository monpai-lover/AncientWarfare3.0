using System;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapModeRules
    {
        public const string POWER_ID = "aw3_hierarchical_vassal_map";
        // Keep projected labels readable while preserving a visible size
        // difference between small, medium and large kingdom zones.
        public const float MinimumLabelSize = 0.35f;
        public const float SmallTerritoryMinimumLabelSize = 0.08f;
        // A city name is rendered in world-space TextMesh units. Values below
        // this floor become sub-pixel after the font bounds normalization.
        public const float CityLabelMinimumSize = 0.35f;
        // City labels keep their area-based scaling, but the previous cap of
        // two world units made ordinary city names nearly invisible.
        public const float CityLabelMaximumSize = 10.0f;
        public const float MaximumLabelSize = 40.0f;
        public const float MapLabelVisualScale = 2.0f;
        public const string CountryLabelMinimapSortingLayer = "MapOverlay";
        public const string CountryLabelMainMapSortingLayer = "EffectsBack";
        public const int CountryLabelMinimapSortingOrder = 32760;
        public const int CountryLabelMainMapSortingOrder = -100;
        // Square-root area scaling tracks the physical footprint of a zone;
        // the broad bounds prevent tiny labels and continental labels from
        // becoming unreadable at the map camera's normal zoom.
        public const float LabelSizeBase = 0.22f;
        public const float LabelSizeScale = 0.105f;
        private const int CountryLabelOutlinePassCount = 8;
        private const int CityLabelOutlinePassCount = 1;
        private const int CountryLabelGapSpanStep = 24;
        private const int MaximumCountryLabelGap = 4;

        internal static int GetLabelOutlinePassCount(bool pCountry)
        {
            return pCountry
                ? CountryLabelOutlinePassCount
                : CityLabelOutlinePassCount;
        }

        internal static bool ShouldRedrawZoneLayer(object pSnapshot,
            object pAsset, object pLastSnapshot, object pLastAsset)
        {
            return !ReferenceEquals(pSnapshot, pLastSnapshot) ||
                   !ReferenceEquals(pAsset, pLastAsset);
        }

        internal static bool ShouldUseLocalOwnershipRefresh(
            long pOldKingdomId, long pNewKingdomId)
        {
            return pOldKingdomId != pNewKingdomId &&
                   (pOldKingdomId >= 0L || pNewKingdomId >= 0L);
        }

        internal static bool ShouldRestoreMixedZoneWater(
            bool pNativeDrawPassActive, int pGroundTileCount,
            int pTotalTileCount)
        {
            return pNativeDrawPassActive && pTotalTileCount > 0 &&
                   pGroundTileCount > 0 &&
                   pGroundTileCount < pTotalTileCount;
        }

        internal static bool ShouldAllowNativeZoneClear(
            bool pMapModeActive, bool pLifecycleOverride)
        {
            return !pMapModeActive || pLifecycleOverride;
        }

        internal static bool ShouldKeepMinimapQuantumAsset(string pAssetId)
        {
            return pAssetId == "armies" ||
                   pAssetId == "boats_big" ||
                   pAssetId == "boats_small" ||
                   pAssetId == "highlight_cursor_zones" ||
                   pAssetId == "selected_kingdom";
        }

        internal static string ResolveCountryLabelSortingLayer(
            bool pMinimap)
        {
            return pMinimap
                ? CountryLabelMinimapSortingLayer
                : CountryLabelMainMapSortingLayer;
        }

        internal static int ResolveCountryLabelSortingOrder(bool pMinimap)
        {
            return pMinimap
                ? CountryLabelMinimapSortingOrder
                : CountryLabelMainMapSortingOrder;
        }

        internal static string FormatCountryLabel(string pDisplayName,
            int pHorizontalSpan)
        {
            return pDisplayName?.Trim() ?? string.Empty;
        }

        internal static int CalculateCountryLabelGapLevel(
            string pDisplayName, int pHorizontalSpan)
        {
            string value = pDisplayName?.Trim() ?? string.Empty;
            if (value.Length != 2) return 0;
            return Math.Max(1, Math.Min(MaximumCountryLabelGap,
                (Math.Max(1, pHorizontalSpan) +
                 CountryLabelGapSpanStep - 1) / CountryLabelGapSpanStep));
        }

        internal static float DarkenCountryColorChannel(float pValue)
        {
            return Math.Max(0f, Math.Min(1f, pValue)) * 0.8f;
        }

        public static int CompareTitles(KingdomTitle pLeft,
            KingdomTitle pRight)
        {
            return ((int)pRight).CompareTo((int)pLeft);
        }

        public static bool CanDrill(bool pHasDirectVassals)
        {
            return pHasDirectVassals;
        }
    }
}
