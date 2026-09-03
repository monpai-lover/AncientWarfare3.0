namespace AncientWarfare3.core.performance
{
    internal static class ArmyMilitaryMovementPriorityRules
    {
        internal static int ResolveP0ChunkCount(int remainingCount,
            int batchSize)
        {
            _ = batchSize;
            return System.Math.Max(0, remainingCount);
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

        internal static bool IsActiveReturnObjectiveOwner(bool returnActive,
            bool actorBelongsToArmy)
        {
            return returnActive && actorBelongsToArmy;
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

        internal static bool ShouldEnterCombatP0(
            bool hasImmediateAttackTarget,
            bool hasValidBehaviourTarget)
        {
            return hasImmediateAttackTarget || hasValidBehaviourTarget;
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

        internal static bool ShouldAdvanceFollowerMoveInSameP0(
            bool isFollowerTask, int actionIndexBeforeAi,
            int actionIndexAfterAi, bool hasBehaviourTileTarget,
            bool behaviourSkipped, bool alreadyMoving)
        {
            return isFollowerTask && actionIndexBeforeAi == 0 &&
                   actionIndexAfterAi == 1 && hasBehaviourTileTarget &&
                   !behaviourSkipped && !alreadyMoving;
        }

        internal static bool ShouldRunSelfLanding(bool inLiquid,
            bool insideBoat, bool waterCreature,
            bool intentionalTransportOwned)
        {
            return inLiquid && !insideBoat && !waterCreature &&
                   !intentionalTransportOwned;
        }

        /// <summary>
        ///     搁浅在水里的陆生单位必须留在 P0 里，哪怕军事目标已经没了。
        ///
        ///     自救上岸(<c>TryRunSelfLandingP0</c>)只在 P0 通道里跑，而 P0 入口
        ///     原先只认「有进行中的 RTS 任务」或「正在回城」。任务一结束，人还
        ///     泡在水里就被摘出索引 —— 自救逻辑再也不会被调用，士兵就一直漂着。
        ///
        ///     水生生物、船上的人、被运输接管的人不在此列：他们本来就该待在水里，
        ///     或者已经有别的所有者在管。
        /// </summary>
        internal static bool ShouldRetainP0ForSelfLanding(bool inLiquid,
            bool insideBoat, bool waterCreature)
        {
            return inLiquid && !insideBoat && !waterCreature;
        }

        internal static bool ShouldAdvanceSelfLandingInSameP0(
            bool isSelfLandingTask, int actionIndexBeforeAi,
            int actionIndexAfterAi, bool hasBehaviourTileTarget,
            bool behaviourSkipped, bool alreadyMoving)
        {
            return isSelfLandingTask && actionIndexBeforeAi == 0 &&
                   actionIndexAfterAi == 1 && hasBehaviourTileTarget &&
                   !behaviourSkipped && !alreadyMoving;
        }
    }
}
