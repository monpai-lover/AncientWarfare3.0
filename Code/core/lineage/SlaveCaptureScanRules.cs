namespace AncientWarfare3.core.lineage
{
    public static class SlaveCaptureScanRules
    {
        public const int MaxWaiterNotificationsPerWorkItem = 4;

        public static int ChunkRadius(int pTileRadius, int pChunkSize)
        {
            int chunkSize = System.Math.Max(1, pChunkSize);
            return pTileRadius <= 0 ? 0 : (pTileRadius + chunkSize - 1) / chunkSize;
        }

        public static int ChunkCount(int pRadius)
        {
            int side = System.Math.Max(0, pRadius) * 2 + 1;
            return side * side;
        }

        public static void OffsetForIndex(int pIndex, int pRadius, out int pX, out int pY)
        {
            int radius = System.Math.Max(0, pRadius);
            int side = radius * 2 + 1;
            int index = System.Math.Max(0, System.Math.Min(pIndex, side * side - 1));
            pX = index % side - radius;
            pY = index / side - radius;
        }

        public static bool ShouldReuseResult(bool pHasEntry, bool pTargetAlive,
            bool pStillHostile, bool pSameIslandAndRadius, double pNow, double pExpiresAt)
        {
            return pHasEntry && pTargetAlive && pStillHostile &&
                   pSameIslandAndRadius && pNow < pExpiresAt;
        }

        public static bool ShouldPause(int pCheckedUnits, int pUnitBudget,
            long pElapsedTicks, long pTickBudget)
        {
            return pCheckedUnits >= System.Math.Max(1, pUnitBudget) ||
                   pElapsedTicks >= System.Math.Max(1L, pTickBudget);
        }
    }
}
