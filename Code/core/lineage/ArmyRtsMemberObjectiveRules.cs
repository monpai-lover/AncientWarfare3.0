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

        internal static bool ShouldReplaceMemberPath(bool hasObjective,
            int recordedTargetTileId, int resolvedTargetTileId,
            bool ownsPath, bool nativeLocalPath)
        {
            _ = recordedTargetTileId;
            _ = resolvedTargetTileId;
            return hasObjective && !ownsPath && !nativeLocalPath;
        }

        internal static bool ShouldRecoverToMissionObjective(
            bool hasActiveMission, bool actorEligible, bool combatActive,
            bool transportActive)
        {
            return hasActiveMission && actorEligible && !combatActive &&
                   !transportActive;
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
