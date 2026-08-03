namespace AncientWarfare3.core.lineage
{
    public static class RoyalGuardArmyAssignmentRules
    {
        public static bool CanAssign(bool actorIsRoyalGuard, bool targetArmyExists,
            bool targetIsRoyalGuardArmy)
        {
            return !actorIsRoyalGuard || !targetArmyExists || targetIsRoyalGuardArmy;
        }

        public static bool IsGuardArmyIdentity(bool roleMarked,
            bool ordinaryArmy, bool legacyNameMarked,
            bool captainRoyalGuard)
        {
            return roleMarked || legacyNameMarked ||
                   !ordinaryArmy && captainRoyalGuard;
        }
    }
}
