namespace AncientWarfare3.core.performance
{
    internal static class ArmyMilitaryMovementPriorityRules
    {
        internal static int ResolveP0ChunkCount(int remainingCount,
            int batchSize)
        {
            if (remainingCount <= 0) return 0;
            return System.Math.Min(remainingCount,
                System.Math.Max(1, batchSize));
        }

        internal static int ResolveP0PriorityRank(bool isRoyalGuard)
        {
            return isRoyalGuard ? 0 : 1;
        }

        internal static bool ShouldYieldToTransport(bool insideBoat,
            bool customTransportOwned, bool vanillaTaxiOwned)
        {
            return insideBoat || customTransportOwned || vanillaTaxiOwned;
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

        internal static bool ShouldRetainCombatP0(
            bool largeSchedulerActive, bool militaryOwnerActive,
            bool immediateCombat)
        {
            return largeSchedulerActive && militaryOwnerActive &&
                   immediateCombat;
        }

        internal static bool ShouldResumeNativeCombatAfterEnemyAcquisition(
            bool hadAttackTargetBeforeSearch,
            bool hasAttackTargetAfterSearch,
            bool behaviourSkippedBySearch)
        {
            return !hadAttackTargetBeforeSearch &&
                   hasAttackTargetAfterSearch && behaviourSkippedBySearch;
        }

        internal static bool ShouldAdvanceNewFightTaskInSameP0(
            bool hasAttackTarget, bool isFightingTask,
            int actionIndexBeforeAi, int actionIndexAfterAi,
            bool behaviourSkipped, bool alreadyMoving)
        {
            return hasAttackTarget && isFightingTask &&
                   actionIndexBeforeAi == 0 && actionIndexAfterAi == 1 &&
                   !behaviourSkipped && !alreadyMoving;
        }

        internal static bool ShouldAdvanceMemberCombatApproachInSameP0(
            bool hasAttackTarget, bool isMemberCombatTask,
            int actionIndexBeforeAi, int actionIndexAfterAi,
            bool behaviourSkipped, bool alreadyMoving)
        {
            return hasAttackTarget && isMemberCombatTask &&
                   actionIndexBeforeAi == 0 && actionIndexAfterAi == 1 &&
                   !behaviourSkipped && !alreadyMoving;
        }
    }
}
