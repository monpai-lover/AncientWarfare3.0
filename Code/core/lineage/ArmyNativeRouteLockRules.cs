namespace AncientWarfare3.core.lineage
{
    internal static class ArmyNativeRouteLockRules
    {
        internal static bool ShouldReuse(bool locked,
            bool targetUnchanged, bool endpointUnchanged,
            bool captainSame, bool explicitFailure)
        {
            return locked && targetUnchanged && endpointUnchanged &&
                   captainSame && !explicitFailure;
        }

        internal static bool ShouldInvalidate(bool targetChanged,
            bool transportCompleted, bool routeFailed,
            bool captainReplaced, bool missionEnded, bool worldCleared)
        {
            return targetChanged || transportCompleted || routeFailed ||
                   captainReplaced || missionEnded || worldCleared;
        }
    }
}
