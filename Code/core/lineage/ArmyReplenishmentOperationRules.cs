namespace AncientWarfare3.core.lineage
{
    public static class ArmyReplenishmentOperationRules
    {
        public const int SchemaVersion = 1;
        public const double DurationWorldSeconds = 20d;
        public const int MaximumOperationsPerCycle = 8;

        public static int ResolveApprovedShortage(int existingApproved,
            int requestedShortage)
        {
            return existingApproved > 0
                ? existingApproved
                : System.Math.Max(0, requestedShortage);
        }

        public static double ResolveDeadline(double start,
            double persistedDeadline)
        {
            double maximum = start + DurationWorldSeconds;
            return System.Math.Min(maximum,
                persistedDeadline < start ? maximum : persistedDeadline);
        }

        public static int AllowedCumulative(int approved, double start,
            double now)
        {
            if (approved <= 0 || now <= start) return 0;
            if (now >= start + DurationWorldSeconds) return approved;
            double progress = System.Math.Min(1d,
                (now - start) / DurationWorldSeconds);
            return System.Math.Min(approved,
                (int)System.Math.Floor(approved * progress + 0.000001d));
        }

        public static int BatchRequest(int approved, int enlisted,
            int liveShortage, double start, double now)
        {
            return System.Math.Min(System.Math.Max(0, liveShortage),
                System.Math.Max(0, approved - enlisted));
        }

        public static bool ShouldFinishEarly(int liveShortage)
        {
            return liveShortage <= 0;
        }

        public static bool HasConsumableSourceReserve(bool poolFrozen,
            int availableCount)
        {
            return poolFrozen && availableCount > 0;
        }

        public static bool HasConsumableSourceReserve(
            ArmyMobilizationPhase phase, int availableCount)
        {
            return ArmyMobilizationRules.CanConsume(phase) &&
                   availableCount > 0;
        }

        public static bool ShouldReleaseDeparture(int living, int target)
        {
            return ArmyMobilizationRules.IsDeploymentReady(living, target);
        }

        public static bool ShouldFinish(bool pShortageResolved,
            bool pReservesConfirmedExhausted, bool pDeadlineReached)
        {
            return pShortageResolved || pReservesConfirmedExhausted ||
                   pDeadlineReached;
        }

        public static int ClampEnlisted(int approved, int persistedEnlisted)
        {
            return System.Math.Max(0, System.Math.Min(
                System.Math.Max(0, approved), persistedEnlisted));
        }

        public static bool ShouldResumeAttack(int living, int minimum)
        {
            return System.Math.Max(0, living) >=
                   System.Math.Max(1, minimum);
        }

        public static bool ShouldMergeSecondary(int living, int minimum,
            bool ordinary, bool primaryExists)
        {
            return ordinary && primaryExists && living > 0 &&
                   !ShouldResumeAttack(living, minimum);
        }

        public static bool MustMaintainAttack(int totalOrdinary, int minimum,
            bool validEnemyTarget)
        {
            return validEnemyTarget &&
                   ShouldResumeAttack(totalOrdinary, minimum);
        }

        public static bool CanUseReservePool(bool ordinaryArmy,
            bool royalGuardArmy)
        {
            return ordinaryArmy && !royalGuardArmy;
        }

        public static bool ShouldClearIneligibleOperation(
            bool hasPersistedOperation, bool canUseReservePool)
        {
            return hasPersistedOperation && !canUseReservePool;
        }

        public static bool ShouldStartOperation(bool liveShortage,
            bool reserveAvailable)
        {
            return liveShortage && reserveAvailable;
        }

        public static bool ShouldRestartAfterConfirmedExhaustion(
            bool reserveAvailable, bool exhaustionConfirmed)
        {
            _ = exhaustionConfirmed;
            return reserveAvailable;
        }

    }
}
