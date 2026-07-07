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

        public static bool ShouldRunStaggered(int pNow, int pLastRun, int pInterval, long pObjectId)
        {
            if (pInterval <= 0) return true;
            if (pNow < pLastRun) return true;

            int slot = PositiveModulo(pObjectId, pInterval);
            bool onSlot = PositiveModulo(pNow, pInterval) == slot;
            if (pLastRun < 0) return onSlot;
            if (pNow - pLastRun < pInterval) return false;
            return onSlot || pNow - pLastRun >= pInterval * 2;
        }

        private static int PositiveModulo(long pValue, int pModulo)
        {
            long result = pValue % pModulo;
            if (result < 0) result += pModulo;
            return (int)result;
        }
    }
}
