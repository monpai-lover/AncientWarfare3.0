namespace AncientWarfare3.core.performance
{
    public static class AuthorityDeferredDrainRules
    {
        public static int ResolveItemLimit(int pPendingCount)
        {
            if (pPendingCount <= 0) return 0;
            if (pPendingCount <= 2) return 1;
            if (pPendingCount <= 8) return 2;
            if (pPendingCount <= 32) return 4;
            return 8;
        }
    }
}
