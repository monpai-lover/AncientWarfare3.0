namespace AncientWarfare3.core.lineage
{
    public static class WarriorArmyMembershipRules
    {
        public static bool ShouldReconcileActor(bool actorValid,
            bool alive, bool warrior, bool hasArmy, bool hasKingdom)
        {
            return actorValid && alive && warrior && !hasArmy && hasKingdom;
        }

        public static bool IsEligibleTargetArmy(bool armyValid, bool alive,
            bool ordinaryArmy, long actorKingdomId, long armyKingdomId)
        {
            return armyValid && alive && ordinaryArmy &&
                   actorKingdomId >= 0L && actorKingdomId == armyKingdomId;
        }

        public static bool ShouldQueueAfterArmyChange(bool actorValid,
            bool alive, bool warrior, bool hasArmy)
        {
            return actorValid && alive && warrior && !hasArmy;
        }

        public static int ResolveActorBudget(int pendingCount)
        {
            if (pendingCount <= 0) return 0;
            return System.Math.Min(32, pendingCount);
        }
    }
}
