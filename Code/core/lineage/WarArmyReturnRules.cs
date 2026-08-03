namespace AncientWarfare3.core.lineage
{
    public enum WarArmyReturnOrderDecision
    {
        Continue = 0,
        Complete = 1,
        CancelForMission = 2
    }

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

        public static bool ShouldCancelForMission(bool hasValidMission)
        {
            return hasValidMission;
        }

        public static WarArmyReturnOrderDecision ResolveOrder(
            bool armyAlive, bool insideFriendlySafeCity,
            bool hasValidMission)
        {
            if (ShouldCancelForMission(hasValidMission))
                return WarArmyReturnOrderDecision.CancelForMission;
            return HasArrived(armyAlive, insideFriendlySafeCity)
                ? WarArmyReturnOrderDecision.Complete
                : WarArmyReturnOrderDecision.Continue;
        }
    }
}
