namespace AncientWarfare3.core.lineage
{
    public static class KingdomMilitaryReadinessRules
    {
        public const int MaxCitiesPerWorkItem = 8;

        public static bool IsReady(
            bool pScanComplete,
            int pObservedCityCount,
            int pCurrentCityCount,
            int pPositiveCoreCities,
            int pUnreadyCoreCities,
            bool pTemporaryLeviesActive)
        {
            if (pTemporaryLeviesActive || !pScanComplete) return false;
            if (pObservedCityCount != pCurrentCityCount) return false;
            return pPositiveCoreCities > 0 && pUnreadyCoreCities <= 0;
        }
    }
}
