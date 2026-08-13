namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsMovementCadenceRules
    {
        internal static bool ShouldRunDuringSkippedPathBatch(
            bool largeSchedulerActive, bool militaryPriorityActive)
        {
            return ArmyMilitaryMovementPriorityRules.ShouldRunP0(
                largeSchedulerActive, militaryPriorityActive,
                isLandGuardFollow: false);
        }

        internal static bool ShouldRunDuringSkippedMovementBatch(
            bool largeSchedulerActive, bool militaryPriorityActive)
        {
            return ArmyMilitaryMovementPriorityRules.ShouldRunP0(
                largeSchedulerActive, militaryPriorityActive,
                isLandGuardFollow: false);
        }
    }
}
