namespace AncientWarfare3.core.db
{
    public static class HistoricalWriteModeRules
    {
        public static bool ShouldStartWorker(bool pDatabaseEnabled)
        {
            return pDatabaseEnabled;
        }

        public static bool ShouldAttemptAsyncWrite(bool pDatabaseEnabled,
            bool pWorkerAvailable)
        {
            return pDatabaseEnabled && pWorkerAvailable;
        }

        public static bool ShouldCompareShadow(bool pShadowEnabled,
            bool pWriteAccepted)
        {
            return pShadowEnabled && pWriteAccepted;
        }

        public static bool ShouldRecoverRequiredWorker(bool pDatabaseEnabled,
            int pPendingRequiredWrites, bool pWorkerAvailable)
        {
            return pDatabaseEnabled && pPendingRequiredWrites > 0 &&
                   !pWorkerAvailable;
        }

        public static bool ShouldRequireWorkerForFlush(bool pDatabaseEnabled,
            bool pWorkerAvailable)
        {
            return pDatabaseEnabled && !pWorkerAvailable;
        }
    }
}
