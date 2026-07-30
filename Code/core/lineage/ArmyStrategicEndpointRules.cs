namespace AncientWarfare3.core.lineage
{
    public static class ArmyStrategicEndpointRules
    {
        public const int MaximumCandidateTiles = 256;

        public static bool CanUseCandidate(bool tileValid, bool ground,
            bool liquid, bool ocean, bool lava, bool blocked, bool walled,
            bool cityCenter, bool belongsToTargetCity,
            bool adjacentToTargetCity, bool belongsToOtherCity,
            bool onTargetIsland)
        {
            return tileValid && ground && !liquid && !ocean && !lava &&
                   !blocked && !walled && !cityCenter && onTargetIsland &&
                   !belongsToOtherCity && belongsToTargetCity;
        }

        public static bool IsBetterCandidate(int candidateTier,
            long candidateDistanceSquared, int candidateTileId,
            int currentTier, long currentDistanceSquared,
            int currentTileId)
        {
            if (candidateTileId < 0) return false;
            if (currentTileId < 0) return true;
            if (candidateTier != currentTier)
                return candidateTier < currentTier;
            if (candidateDistanceSquared != currentDistanceSquared)
                return candidateDistanceSquared < currentDistanceSquared;
            return candidateTileId < currentTileId;
        }
    }
}
