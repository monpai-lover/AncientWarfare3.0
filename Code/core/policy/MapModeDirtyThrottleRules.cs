namespace AncientWarfare3.core.policy
{
    public static class MapModeDirtyThrottleRules
    {
        public static bool ShouldDirty(bool pActive, double pNow, double pLastDirty, double pMinInterval)
        {
            if (!pActive) return false;
            if (pMinInterval <= 0) return true;
            if (pLastDirty < 0) return true;
            if (pNow < pLastDirty) return true;
            return pNow - pLastDirty >= pMinInterval;
        }
    }
}
