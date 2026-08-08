using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.performance
{
    public static class AuthorityDeferredDrainRules
    {
        public static int ResolveItemLimit(int pPendingCount)
        {
            return DeferredRuntimeWorkRules.ResolveItemsPerAuthorityFrame(
                pPendingCount);
        }
    }
}
