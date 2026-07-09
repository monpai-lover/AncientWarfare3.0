namespace AncientWarfare3.core.lineage
{
    public static class SlaveFoodQuotaRules
    {
        public static bool ShouldCountSlavesForFoodQuota(bool pHasCity, bool pSlaveryEnabled, bool pForceCount)
        {
            if (!pHasCity) return false;
            return pSlaveryEnabled || pForceCount;
        }
    }
}
