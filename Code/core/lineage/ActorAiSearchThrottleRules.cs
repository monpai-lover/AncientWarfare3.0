namespace AncientWarfare3.core.lineage
{
    public static class ActorAiSearchThrottleRules
    {
        public static bool ShouldSearch(double pNow, double pNextAllowed)
        {
            return pNextAllowed < 0.0 || pNow >= pNextAllowed;
        }

        public static double NextAllowedAfterMiss(double pNow, double pCooldown)
        {
            return pNow + (pCooldown > 0.0 ? pCooldown : 0.0);
        }
    }
}
