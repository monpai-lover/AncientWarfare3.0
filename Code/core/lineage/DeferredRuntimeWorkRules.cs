namespace AncientWarfare3.core.lineage
{
    public static class DeferredRuntimeWorkRules
    {
        public static bool ShouldStopDrain(int pProcessed, int pMaxItems,
            long pElapsedTicks, long pBudgetTicks)
        {
            return pProcessed >= System.Math.Max(1, pMaxItems) ||
                   pElapsedTicks >= System.Math.Max(1L, pBudgetTicks);
        }

        public static bool ShouldRetry(int pAttempts, int pMaxAttempts)
        {
            return pAttempts < System.Math.Max(1, pMaxAttempts);
        }

        public static string CoalescingKey(string pKind, long pObjectId)
        {
            return (pKind ?? "") + ":" + pObjectId;
        }
    }
}
