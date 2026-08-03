namespace AncientWarfare3.core.lineage
{
    public static class HistoryWriteTimingRules
    {
        public static bool ShouldRecord(bool pGameLoaded,
            bool pSmoothLoaderLoading)
        {
            return pGameLoaded && !pSmoothLoaderLoading;
        }

        public static bool ResolveSuppressedWriteResult(
            string pProjectionKey)
        {
            return string.IsNullOrWhiteSpace(pProjectionKey);
        }
    }
}
