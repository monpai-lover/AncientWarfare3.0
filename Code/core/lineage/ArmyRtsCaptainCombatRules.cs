namespace AncientWarfare3.core.lineage
{
    public static class ArmyRtsCaptainCombatRules
    {
        public static bool ShouldRetainTarget(bool targetAlive,
            bool targetHostile, bool withinEnvelope)
        {
            return targetAlive && targetHostile && withinEnvelope;
        }

        public static bool ShouldRetainMemberTarget(bool targetAlive,
            bool targetHostile, bool sameIsland, bool combatOwned)
        {
            return targetAlive && targetHostile && sameIsland &&
                   combatOwned;
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
                   hasValidCombatTarget;
        }

        public static bool ShouldSuppressVanillaMemberFight(
            bool missionActive, bool actorIsCaptain,
            bool fieldCombatReleased, bool hasValidCombatTarget)
        {
            return missionActive && !actorIsCaptain &&
                   !hasValidCombatTarget;
        }

        public static bool ShouldRestoreVanillaMemberFollow(
            bool suppressVanillaFight, bool isDedicatedMemberCombatTask)
        {
            return suppressVanillaFight && isDedicatedMemberCombatTask;
        }

        public static bool ShouldUseSiegeCombatTask(
            bool siegeCombatActive, bool actorInsideTargetCityCombatZone)
        {
            return siegeCombatActive && actorInsideTargetCityCombatZone;
        }
    }
}
