namespace AncientWarfare3.core.pathfinding
{
    public enum PreparedNativePathCommitDecision
    {
        Commit,
        RetryLater,
        Drop
    }

    public readonly struct PreparedNativePathFacts
    {
        public PreparedNativePathFacts(
            bool actorExists,
            bool actorAlive,
            bool actorIdMatches,
            bool batchExists,
            bool currentTileValid,
            bool targetTileValid,
            bool currentRegionValid,
            bool targetRegionValid,
            int currentTileId,
            int preparedCurrentTileId,
            int currentTargetTileId,
            int preparedTargetTileId,
            int currentPathIndex,
            int preparedPathIndex,
            bool currentHasGlobalPath,
            bool preparedHadGlobalPath)
        {
            ActorExists = actorExists;
            ActorAlive = actorAlive;
            ActorIdMatches = actorIdMatches;
            BatchExists = batchExists;
            CurrentTileValid = currentTileValid;
            TargetTileValid = targetTileValid;
            CurrentRegionValid = currentRegionValid;
            TargetRegionValid = targetRegionValid;
            CurrentTileId = currentTileId;
            PreparedCurrentTileId = preparedCurrentTileId;
            CurrentTargetTileId = currentTargetTileId;
            PreparedTargetTileId = preparedTargetTileId;
            CurrentPathIndex = currentPathIndex;
            PreparedPathIndex = preparedPathIndex;
            CurrentHasGlobalPath = currentHasGlobalPath;
            PreparedHadGlobalPath = preparedHadGlobalPath;
        }

        public bool ActorExists { get; }
        public bool ActorAlive { get; }
        public bool ActorIdMatches { get; }
        public bool BatchExists { get; }
        public bool CurrentTileValid { get; }
        public bool TargetTileValid { get; }
        public bool CurrentRegionValid { get; }
        public bool TargetRegionValid { get; }
        public int CurrentTileId { get; }
        public int PreparedCurrentTileId { get; }
        public int CurrentTargetTileId { get; }
        public int PreparedTargetTileId { get; }
        public int CurrentPathIndex { get; }
        public int PreparedPathIndex { get; }
        public bool CurrentHasGlobalPath { get; }
        public bool PreparedHadGlobalPath { get; }
    }

    public static class PreparedNativePathCommitRules
    {
        public static PreparedNativePathCommitDecision Decide(
            PreparedNativePathFacts pFacts)
        {
            if (!pFacts.ActorExists || !pFacts.ActorAlive ||
                !pFacts.ActorIdMatches || !pFacts.BatchExists)
                return PreparedNativePathCommitDecision.Drop;
            if (!pFacts.CurrentTileValid || !pFacts.TargetTileValid ||
                !pFacts.CurrentRegionValid || !pFacts.TargetRegionValid)
                return PreparedNativePathCommitDecision.Drop;
            if (pFacts.CurrentTileId != pFacts.PreparedCurrentTileId ||
                pFacts.CurrentTargetTileId != pFacts.PreparedTargetTileId ||
                pFacts.CurrentPathIndex != pFacts.PreparedPathIndex ||
                pFacts.CurrentHasGlobalPath != pFacts.PreparedHadGlobalPath)
                return PreparedNativePathCommitDecision.RetryLater;
            return PreparedNativePathCommitDecision.Commit;
        }
    }
}
