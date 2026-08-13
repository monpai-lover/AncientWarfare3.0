namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsMemberObjectiveRules
    {
        internal static long ResolveTargetCityId(long missionTargetCityId,
            long routeFailureTargetCityId)
        {
            return missionTargetCityId;
        }

        internal static bool ShouldSubmitMemberPath(bool hasObjective,
            bool ownsPath, bool nativeLocalPath, bool pathPending)
        {
            return hasObjective && !ownsPath && !nativeLocalPath &&
                   !pathPending;
        }

        internal static bool ShouldOwnMemberObjective(bool missionActive,
            bool isCaptain, bool actorEligible, bool immediateCombat,
            bool transportActive)
        {
            return missionActive && !isCaptain && actorEligible &&
                   !immediateCombat && !transportActive;
        }
    }
}
