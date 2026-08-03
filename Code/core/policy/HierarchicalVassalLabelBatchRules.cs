namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalLabelBatchRules
    {
        internal static bool CanAccept(long pBatchWorldGeneration,
            long pCurrentWorldGeneration, long pBatchSourceGeneration,
            long pCurrentSourceGeneration, bool pBatchSuperseded)
        {
            return !pBatchSuperseded &&
                pBatchWorldGeneration == pCurrentWorldGeneration &&
                pBatchSourceGeneration == pCurrentSourceGeneration;
        }

        internal static long NextSourceGeneration(long pCurrent)
        {
            return pCurrent == long.MaxValue ? 1L : pCurrent + 1L;
        }

        internal static bool CanRetry(int pFailureCount)
        {
            return pFailureCount >= 0 && pFailureCount < 2;
        }
    }
}
