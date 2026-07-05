namespace AncientWarfare3.core.lineage
{
    public static class CityMaintenanceThrottleRules
    {
        public static bool ShouldRun(int pNow, int pLastRun, int pInterval)
        {
            if (pInterval <= 0) return true;
            if (pLastRun < 0) return true;
            if (pNow < pLastRun) return true;
            return pNow - pLastRun >= pInterval;
        }
    }
}
