using System;

namespace AncientWarfare3.core.lineage
{
    public enum ArmyFollowerTargetResult
    {
        Unavailable,
        Hold,
        Move
    }

    public enum ArmyFollowerStepResult
    {
        Unavailable,
        Hold,
        Throttled,
        ReconnectRequired,
        Stepped
    }

    public enum ArmySharedRouteInstallStatus
    {
        Unavailable = 0,
        NotAttempted = 1,
        RouteEmpty = 2,
        ProviderPending = 3,
        TransportOwned = 4,
        BuildFailed = 5,
        MovementRejected = 6,
        Installed = 7,
        Following = 8,
        Arrived = 9,
        ReconnectRequired = 10,
        StaleInstalled = 11
    }

    public enum ArmyProviderRouteTargetSource
    {
        Unavailable = 0,
        FormationLane = 1,
        ValidatedCenterline = 2
    }

    public static class ArmySharedPathRules
    {
        public const int MaximumTrailSteps = 256;
        public const int LocalReconnectRadius = 8;
        public const int ProviderFormationEnvelopeRadius = 2;

        public static long ClampCursor(long cursor,
            long oldestSequence, long newestSequence)
        {
            long oldest = Math.Max(0L, oldestSequence);
            long newest = Math.Max(oldest, newestSequence);
            return Math.Max(oldest, Math.Min(newest, cursor));
        }

        public static long MaximumSequenceForRow(long newestSequence,
            long oldestSequence, int rowBehind)
        {
            long oldest = Math.Max(0L, oldestSequence);
            long newest = Math.Max(oldest, newestSequence);
            long behind = Math.Max(0, rowBehind);
            return Math.Max(oldest, newest - behind);
        }

        public static int FormationRowLag(int formationRow,
            int localRadius)
        {
            int radius = Math.Max(0, localRadius);
            return formationRow >= 0
                ? formationRow
                : radius + Math.Abs(formationRow);
        }

        public static long AdvanceCursor(long cursor,
            long oldestSequence, long maximumSequence,
            bool reachedCurrentTarget, bool transportActive)
        {
            long current = Math.Max(cursor, Math.Max(0L, oldestSequence));
            if (transportActive || !reachedCurrentTarget ||
                current >= maximumSequence || current == long.MaxValue)
                return current;
            return current + 1L;
        }

        public static bool ShouldPauseLandCursor(bool activeVoyage)
        {
            return activeVoyage;
        }

        public static bool ShouldRebaseAfterTransport(
            bool landTrailWasPaused, bool activeVoyage)
        {
            return landTrailWasPaused && !activeVoyage;
        }

        public static bool ShouldUseLocalReconnect(
            bool directStepSucceeded, float targetDistanceSquared)
        {
            return !directStepSucceeded && targetDistanceSquared > 0f &&
                   targetDistanceSquared <=
                   LocalReconnectRadius * LocalReconnectRadius;
        }

        public static ArmyFollowerStepResult ResolveDirectStepResult(
            bool hasValidActor, bool hasValidTarget,
            float targetDistanceSquared, bool hasSafeDirectStep,
            bool correctionReady, bool budgetAvailable)
        {
            if (!hasValidActor || !hasValidTarget ||
                targetDistanceSquared < 0f ||
                targetDistanceSquared >
                LocalReconnectRadius * LocalReconnectRadius)
                return ArmyFollowerStepResult.Unavailable;
            if (targetDistanceSquared <= 0f)
                return ArmyFollowerStepResult.Hold;
            if (!hasSafeDirectStep)
                return ArmyFollowerStepResult.ReconnectRequired;
            if (!correctionReady || !budgetAvailable)
                return ArmyFollowerStepResult.Throttled;
            return ArmyFollowerStepResult.Stepped;
        }

        public static bool ShouldUseLocalReconnect(
            ArmyFollowerStepResult pResult)
        {
            return pResult == ArmyFollowerStepResult.ReconnectRequired;
        }

        public static bool ShouldUseLongRangeFollowerRoute(
            bool targetValid, float targetDistanceSquared)
        {
            return targetValid && targetDistanceSquared >
                   LocalReconnectRadius * LocalReconnectRadius;
        }

        public static bool ShouldSubmitIndependentFollowerRecoveryRoute(
            bool sharedRouteAvailable, bool recoveryTargetAvailable,
            bool combatActive, bool transportActive)
        {
            return !sharedRouteAvailable && recoveryTargetAvailable &&
                   !combatActive && !transportActive;
        }

        public static bool ShouldPreserveInFlightMovement(
            ArmyFollowerStepResult pResult, bool actorIsMoving = false)
        {
            return pResult == ArmyFollowerStepResult.Throttled ||
                   pResult == ArmyFollowerStepResult.Hold && actorIsMoving;
        }

        public static bool ShouldHoldAtSharedRouteDesired(
            ArmySharedRouteInstallStatus pStatus,
            bool formationTargetReached)
        {
            return pStatus == ArmySharedRouteInstallStatus.Arrived &&
                   formationTargetReached;
        }

        public static bool ShouldTrimRecordedRoute(bool usesProvider,
            int routeStepCount, int maximumRecordedSteps)
        {
            return !usesProvider && routeStepCount >=
                   Math.Max(1, maximumRecordedSteps);
        }

        public static bool CanInstallCompleteProviderRoute(
            bool providerComplete, int routeStepCount,
            bool routeContainsTransportStep, bool transportActive)
        {
            return providerComplete && routeStepCount > 0 &&
                   !routeContainsTransportStep && !transportActive;
        }

        public static bool ShouldReuseInstalledSharedRoute(
            int installedRevision, int availableRevision,
            int localPathCount, bool actorFollowingLocalPath,
            bool atInstalledEndpoint)
        {
            return installedRevision >= 0 &&
                   installedRevision == availableRevision &&
                   (atInstalledEndpoint ||
                    localPathCount > 0 && actorFollowingLocalPath);
        }

        public static ArmySharedRouteInstallStatus
            ResolveCurrentInstallStatus(bool providerAvailable,
                bool transportActive, bool hasMatchingRevision,
                bool atInstalledEndpoint, int localPathCount,
                bool actorFollowingLocalPath,
                ArmySharedRouteInstallStatus recordedStatus)
        {
            if (!providerAvailable)
                return ArmySharedRouteInstallStatus.Unavailable;
            if (transportActive)
                return ArmySharedRouteInstallStatus.TransportOwned;
            if (!hasMatchingRevision) return recordedStatus;
            if (atInstalledEndpoint)
                return ArmySharedRouteInstallStatus.Arrived;
            return localPathCount > 0 && actorFollowingLocalPath
                ? ArmySharedRouteInstallStatus.Following
                : ArmySharedRouteInstallStatus.StaleInstalled;
        }

        public static bool ShouldRecoverStaleInstalledRoute(
            ArmySharedRouteInstallStatus status, bool combatActive,
            bool transportActive)
        {
            return status == ArmySharedRouteInstallStatus.StaleInstalled &&
                   !combatActive && !transportActive;
        }

        public static bool ShouldInstallCompleteRouteForActor(
            bool providerRouteReady, bool transportActive,
            bool hasMatchingRevision, bool atInstalledEndpoint,
            bool followingInstalledPath)
        {
            if (!providerRouteReady || transportActive) return false;
            if (!hasMatchingRevision) return true;
            return !atInstalledEndpoint && !followingInstalledPath;
        }

        public static bool ShouldUseLocalCorrectionUntilSharedRoute(
            bool providerComplete, int routeStepCount,
            bool routeContainsTransportStep, bool transportActive)
        {
            return !CanInstallCompleteProviderRoute(providerComplete,
                routeStepCount, routeContainsTransportStep,
                transportActive);
        }

        public static int StableRouteLane(int slot)
        {
            int normalized = Math.Max(1, slot) - 1;
            int lane = normalized % 3;
            return lane == 0 ? 0 : lane == 1 ? 1 : -1;
        }

        public static ArmyProviderRouteTargetSource
            ResolveProviderRouteTargetSource(bool formationLaneAvailable,
                bool providerCenterValidated)
        {
            if (formationLaneAvailable)
                return ArmyProviderRouteTargetSource.FormationLane;
            return providerCenterValidated
                ? ArmyProviderRouteTargetSource.ValidatedCenterline
                : ArmyProviderRouteTargetSource.Unavailable;
        }

        public static bool ShouldPublishProviderReconnectTarget(
            ArmySharedRouteInstallStatus pStatus,
            bool reconnectTargetAvailable)
        {
            return reconnectTargetAvailable &&
                   pStatus ==
                   ArmySharedRouteInstallStatus.ReconnectRequired;
        }

        public static bool AreRouteTilesAdjacent(int deltaX, int deltaY)
        {
            int x = Math.Abs(deltaX);
            int y = Math.Abs(deltaY);
            return x <= 1 && y <= 1 && x + y > 0;
        }

        public static bool ShouldAppendAdjacentProviderStep(
            bool providerStepIsAdjacent, bool providerStepValidated)
        {
            return providerStepIsAdjacent && providerStepValidated;
        }

        public static bool ShouldAppendProviderBridgeStep(
            bool providerStepValidated, bool candidateBlocked,
            bool candidateLava, bool candidateHasWalls,
            bool reducesDistance, bool withinFormationEnvelope)
        {
            return providerStepValidated && !candidateBlocked &&
                   !candidateLava && !candidateHasWalls &&
                   reducesDistance && withinFormationEnvelope;
        }

        public static bool ShouldAdvanceInstalledProviderSwim(
            bool sharedRouteInstalled, bool actorMilitary,
            bool nextTileOcean, bool damagedByOcean,
            bool alreadyInLiquid)
        {
            return sharedRouteInstalled && actorMilitary &&
                   nextTileOcean && damagedByOcean &&
                   !alreadyInLiquid;
        }

        public static int RequiredAdjacentSteps(int startX, int startY,
            int endX, int endY)
        {
            long x = Math.Abs((long)endX - startX);
            long y = Math.Abs((long)endY - startY);
            long steps = Math.Max(x, y);
            return steps >= int.MaxValue ? int.MaxValue : (int)steps;
        }

        public static ArmyFollowerTargetResult ResolveFollowerTargetSource(
            ArmyFollowerTargetResult sharedPathResult,
            bool formationTargetAvailable, bool formationTargetReached)
        {
            if (sharedPathResult != ArmyFollowerTargetResult.Unavailable)
                return sharedPathResult;
            if (!formationTargetAvailable)
                return ArmyFollowerTargetResult.Unavailable;
            return formationTargetReached
                ? ArmyFollowerTargetResult.Hold
                : ArmyFollowerTargetResult.Move;
        }
    }
}
