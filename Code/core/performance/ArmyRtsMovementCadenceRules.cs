namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsMovementCadenceRules
    {
        internal static bool ShouldRunDuringSkippedPathBatch(
            bool largeSchedulerActive, bool actorInArmy,
            bool hasCustomPathOwnership)
        {
            return largeSchedulerActive && actorInArmy &&
                   hasCustomPathOwnership;
        }

        internal static bool ShouldRunDuringSkippedMovementBatch(
            bool largeSchedulerActive, bool actorInArmy,
            bool actorIsMoving, bool hasLocalPath,
            bool hasCustomPathOwnership)
        {
            return largeSchedulerActive && actorInArmy && actorIsMoving &&
                   (hasLocalPath || hasCustomPathOwnership);
        }
    }
}
