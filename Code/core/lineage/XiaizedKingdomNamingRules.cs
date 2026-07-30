namespace AncientWarfare3.core.lineage
{
    public static class XiaizedKingdomNamingRules
    {
        public static bool ShouldApply(bool originalXia, int xiaizationLevel,
            int maximumLevel, bool markerApplied)
        {
            return !originalXia && !markerApplied && xiaizationLevel >= maximumLevel;
        }

        public static bool ShouldRunOrdinaryRepair(bool originalXia, int xiaizationLevel,
            int maximumLevel, bool markerApplied)
        {
            _ = markerApplied;
            return originalXia || xiaizationLevel < maximumLevel;
        }

        public static bool ShouldDisplayStateSuffix(bool originalXia,
            int xiaizationLevel, int maximumLevel)
        {
            return originalXia || xiaizationLevel >= maximumLevel;
        }
    }
}
