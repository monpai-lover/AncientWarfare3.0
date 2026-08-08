namespace AncientWarfare3.core.lineage
{
    public static class DeferredRuntimeWorkRules
    {
        public const int MaximumConsecutiveCriticalRuntimeWork = 3;
        public const int MaximumConsecutiveRuntimeWork = 3;
        public const int MaximumItemsPerAuthorityFrame = 1;

        public static int ResolveItemsPerAuthorityFrame(
            int pPendingCount)
        {
            return pPendingCount <= 0
                ? 0
                : MaximumItemsPerAuthorityFrame;
        }

        public static bool ShouldStartFrameDrain(int pLastDrainFrame,
            int pCurrentFrame)
        {
            return pCurrentFrame >= 0 &&
                   pLastDrainFrame != pCurrentFrame;
        }

        public static bool ShouldPrioritizeCriticalRuntimeWork(
            bool pCriticalRuntimePending, bool pRuntimePending,
            bool pPersistentPending, int pConsecutiveCriticalRuntimeWork)
        {
            if (!pCriticalRuntimePending) return false;
            if (!pRuntimePending && !pPersistentPending) return true;
            return pConsecutiveCriticalRuntimeWork <
                   MaximumConsecutiveCriticalRuntimeWork;
        }

        public static bool ShouldPrioritizeRuntimeWork(
            bool pRuntimePending, bool pPersistentPending,
            int pConsecutiveRuntimeWork)
        {
            if (!pRuntimePending) return false;
            if (!pPersistentPending) return true;
            return pConsecutiveRuntimeWork <
                   MaximumConsecutiveRuntimeWork;
        }

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

        public static string FormatFailure(string pKey,
            System.Exception pError)
        {
            string key = string.IsNullOrEmpty(pKey)
                ? "<ordered>"
                : pKey;
            string type = pError?.GetType().FullName ?? "<unknown>";
            string message = pError?.Message ?? "<no message>";
            string detail = pError?.ToString() ?? "<no exception>";
            return "Deferred work failed: key=" + key +
                   " type=" + type + " message=" + message +
                   System.Environment.NewLine + detail;
        }
    }
}
