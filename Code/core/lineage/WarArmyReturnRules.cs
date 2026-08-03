namespace AncientWarfare3.core.lineage
{
    public static class WarArmyReturnRules
    {
        public static bool ShouldBeginReturn(bool armyAlive,
            long currentMissionWarId, long endedWarId)
        {
            return armyAlive && endedWarId >= 0L &&
                   currentMissionWarId == endedWarId;
        }

        public static bool HasArrived(bool armyAlive,
            bool insideFriendlySafeCity)
        {
            return !armyAlive || insideFriendlySafeCity;
        }
    }
}
