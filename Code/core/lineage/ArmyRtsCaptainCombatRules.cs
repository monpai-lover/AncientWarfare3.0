namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsCaptainCombatRules
    {
        public static bool ShouldRetainTarget(bool targetAlive,
            bool targetHostile, bool withinEnvelope)
        {
            return targetAlive && targetHostile && withinEnvelope;
        }

        public static bool ShouldEnterCombat(bool alreadyInCombat,
            int engagedCombatants, int liveCombatants, bool captainEngaged)
        {
            return ArmyRtsFieldCombatRules.ShouldReleaseToFieldCombat(
                alreadyInCombat, engagedCombatants, liveCombatants,
                captainEngaged);
        }

        public static bool ShouldUseMemberCombatTask(bool missionActive,
            bool actorIsCaptain, bool fieldCombatReleased,
            bool hasValidCombatTarget)
        {
            return missionActive && !actorIsCaptain &&
                   (fieldCombatReleased || hasValidCombatTarget);
        }

        public static bool ShouldSuppressVanillaMemberFight(
            bool missionActive, bool actorIsCaptain,
            bool fieldCombatReleased, bool hasValidCombatTarget)
        {
            return missionActive && !actorIsCaptain &&
                   !fieldCombatReleased && !hasValidCombatTarget;
        }

        public static bool ShouldRestoreVanillaMemberFollow(
            bool suppressVanillaFight, bool isDedicatedMemberCombatTask)
        {
            return suppressVanillaFight && !isDedicatedMemberCombatTask;
        }
    }
}
