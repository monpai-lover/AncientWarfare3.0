namespace AncientWarfare3.core.performance
{
    internal static class ActorScalePerformanceRules
    {
        internal static bool ShouldUseWarOnlyExplanation(
            int liveActors, int kingdomCount, int mapTileCount,
            int activeWars)
        {
            return activeWars > 0;
        }

        internal static bool ShouldPrepareEnemySearchOffThread(
            bool activeActorStage, bool enemySearchJobPresent)
        {
            return activeActorStage && enemySearchJobPresent;
        }

        internal static bool ShouldHoldGlobalEnemyCacheLock(
            int preparationWorkerCount)
        {
            return false;
        }

        internal static bool ShouldParallelizePresentationCapture(
            int actorCount, int workerCount)
        {
            if (workerCount <= 1) return false;
            int minimumBatch = System.Math.Max(256, workerCount * 64);
            return actorCount >= minimumBatch;
        }
    }
}
