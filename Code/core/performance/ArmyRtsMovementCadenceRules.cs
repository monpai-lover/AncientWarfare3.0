namespace AncientWarfare3.core.performance
{
    internal static class ArmyRtsMovementCadenceRules
    {
        internal static bool ShouldRunDuringSkippedPathBatch(
            bool largeSchedulerActive, bool actorInArmy,
            bool hasCustomPathOwnership, bool hasActiveSharedRoute)
        {
            return largeSchedulerActive && actorInArmy &&
                   (hasCustomPathOwnership || hasActiveSharedRoute);
        }

        internal static bool ShouldRunDuringSkippedMovementBatch(
            bool largeSchedulerActive, bool actorInArmy,
            bool actorIsMoving, bool hasLocalPath,
            bool hasCustomPathOwnership, bool hasActiveSharedRoute)
        {
            return largeSchedulerActive && actorInArmy && actorIsMoving &&
                   (hasCustomPathOwnership ||
                    (hasLocalPath && hasActiveSharedRoute));
        }
    }
}
