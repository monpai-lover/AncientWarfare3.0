namespace AncientWarfare3.core.performance
{
    internal static class ArmyMilitaryMovementPriorityRules
    {
        internal static bool ShouldRunP0(bool largeSchedulerActive,
            bool ownsRtsObjective, bool isLandGuardFollow)
        {
            return largeSchedulerActive &&
                   (ownsRtsObjective || isLandGuardFollow);
        }
    }
}
