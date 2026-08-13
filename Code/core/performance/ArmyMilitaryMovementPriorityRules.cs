namespace AncientWarfare3.core.performance
{
    internal static class ArmyMilitaryMovementPriorityRules
    {
        internal static int ResolveP0SliceCount(int registeredCount,
            int simulationBatchSize)
        {
            return System.Math.Max(0, registeredCount);
        }

        internal static bool CanAdmitOrdinaryActorWork(bool p0SlicePending)
        {
            return !p0SlicePending;
        }

        internal static bool IsActiveRtsObjectiveOwner(bool controllerActive,
            bool ownsObjective)
        {
            return controllerActive && ownsObjective;
        }

        internal static bool ShouldRunP0(bool largeSchedulerActive,
            bool ownsRtsObjective, bool isLandGuardFollow)
        {
            return largeSchedulerActive &&
                   (ownsRtsObjective || isLandGuardFollow);
        }
    }
}
