namespace AncientWarfare3.core.lineage
{
    public static class ArmyAiSafetyRules
    {
        public static bool ShouldSkipCityAttackAction(
            bool pHasActor,
            bool pHasCity,
            bool pHasAttackZone,
            bool pHasArmy,
            bool pHasCurrentTile,
            bool pHasCurrentZone)
        {
            return !pHasActor ||
                   !pHasCity ||
                   !pHasAttackZone ||
                   !pHasArmy ||
                   !pHasCurrentTile;
        }

        public static bool ShouldRouteCrossIslandAttackThroughAw3(
            bool pHasAttackTarget,
            bool pSameIsland,
            bool pAw3OwnsPathfinding)
        {
            return pHasAttackTarget && !pSameIsland &&
                   pAw3OwnsPathfinding;
        }
    }
}
