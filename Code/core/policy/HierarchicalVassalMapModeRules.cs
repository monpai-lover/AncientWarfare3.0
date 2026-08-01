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
        public const float MaximumLabelSize = 16.0f;
        // Square-root area scaling tracks the physical footprint of a zone;
        // the broad bounds prevent tiny labels and continental labels from
        // becoming unreadable at the map camera's normal zoom.
        public const float LabelSizeBase = 0.22f;
        public const float LabelSizeScale = 0.105f;
        private const int CountryLabelGapSpanStep = 24;
        private const int MaximumCountryLabelGap = 4;

        internal static bool ShouldKeepMinimapQuantumAsset(string pAssetId)
        {
            return pAssetId == "armies" ||
                   pAssetId == "boats_big" ||
                   pAssetId == "boats_small" ||
                   pAssetId == "highlight_cursor_zones" ||
                   pAssetId == "selected_kingdom";
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
