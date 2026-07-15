namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardArmyAssignmentRules
    {
        public static bool CanAssign(bool actorIsRoyalGuard, bool targetArmyExists,
            bool targetIsRoyalGuardArmy)
        {
            return !actorIsRoyalGuard || !targetArmyExists || targetIsRoyalGuardArmy;
        }
    }
}
