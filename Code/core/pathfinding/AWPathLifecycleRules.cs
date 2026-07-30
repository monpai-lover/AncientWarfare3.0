// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.pathfinding
{
    public readonly struct AWPathRequestKey : IEquatable<AWPathRequestKey>
    {
        public AWPathRequestKey(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit)
            : this(pTarget, pWater, pBlocks, pLava, pRegionLimit,
                pBoundedMilitaryWater: false,
                pMaximumConsecutiveWaterTiles: 0)
        {
        }

        public AWPathRequestKey(int pTarget, bool pWater, bool pBlocks,
            bool pLava, int pRegionLimit, bool pBoundedMilitaryWater,
            int pMaximumConsecutiveWaterTiles)
        {
            TargetTileId = pTarget;
            PathOnWater = pWater;
            WalkOnBlocks = pBlocks;
            WalkOnLava = pLava;
            RegionLimit = Math.Max(0, pRegionLimit);
            BoundedMilitaryWater = pBoundedMilitaryWater;
            MaximumConsecutiveWaterTiles = pBoundedMilitaryWater
                ? Math.Max(1, pMaximumConsecutiveWaterTiles)
                : 0;
        }

        public int TargetTileId { get; }
        public bool PathOnWater { get; }
        public bool WalkOnBlocks { get; }
        public bool WalkOnLava { get; }
        public int RegionLimit { get; }
        public bool BoundedMilitaryWater { get; }
        public int MaximumConsecutiveWaterTiles { get; }

        public bool Matches(int pTarget, bool pWater, bool pBlocks, bool pLava,
            int pRegionLimit)
        {
            return Matches(pTarget, pWater, pBlocks, pLava, pRegionLimit,
                pBoundedMilitaryWater: false,
                pMaximumConsecutiveWaterTiles: 0);
        }

        public bool Matches(int pTarget, bool pWater, bool pBlocks,
            bool pLava, int pRegionLimit, bool pBoundedMilitaryWater,
            int pMaximumConsecutiveWaterTiles)
        {
            return TargetTileId == pTarget && PathOnWater == pWater &&
                   WalkOnBlocks == pBlocks && WalkOnLava == pLava &&
                   RegionLimit == Math.Max(0, pRegionLimit) &&
                   BoundedMilitaryWater == pBoundedMilitaryWater &&
                   MaximumConsecutiveWaterTiles ==
                   (pBoundedMilitaryWater
                       ? Math.Max(1, pMaximumConsecutiveWaterTiles)
                       : 0);
        }

        public bool Equals(AWPathRequestKey pOther)
        {
            return Matches(pOther.TargetTileId, pOther.PathOnWater, pOther.WalkOnBlocks,
                pOther.WalkOnLava, pOther.RegionLimit,
                pOther.BoundedMilitaryWater,
                pOther.MaximumConsecutiveWaterTiles);
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathRequestKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = TargetTileId;
                hash = hash * 397 ^ (PathOnWater ? 1 : 0);
                hash = hash * 397 ^ (WalkOnBlocks ? 1 : 0);
                hash = hash * 397 ^ (WalkOnLava ? 1 : 0);
                hash = hash * 397 ^ RegionLimit;
                hash = hash * 397 ^ (BoundedMilitaryWater ? 1 : 0);
                hash = hash * 397 ^ MaximumConsecutiveWaterTiles;
                return hash;
            }
        }
    }

    public enum AWPathSlotAction
    {
        Enqueue,
        ReplacePending,
        StoreAfterRunning
    }

    public enum AWPathWorkClass
    {
        Operational = 0,
        EssentialTravel = 1,
        Ambient = 2
    }

    internal static class AWPathWorkClassRules
    {
        internal const int MaximumConsecutiveOperational = 8;
        internal const int MaximumConsecutiveNonAmbient = 16;

        internal static AWPathWorkClass Classify(bool pWarrior,
            bool pHasArmy, bool pBoat, bool pTransport,
            bool pSchoolJourney)
        {
            if (pWarrior || pHasArmy || pBoat || pTransport)
                return AWPathWorkClass.Operational;
            return pSchoolJourney
                ? AWPathWorkClass.EssentialTravel
                : AWPathWorkClass.Ambient;
        }

        internal static AWPathWorkClass Next(int pOperationalQueued,
            int pEssentialQueued, int pAmbientQueued,
            int pConsecutiveOperational, int pConsecutiveNonAmbient)
        {
            if (pAmbientQueued > 0 && pConsecutiveNonAmbient >=
                MaximumConsecutiveNonAmbient)
                return AWPathWorkClass.Ambient;
            if (pEssentialQueued > 0 && pConsecutiveOperational >=
                MaximumConsecutiveOperational)
                return AWPathWorkClass.EssentialTravel;
            if (pOperationalQueued > 0) return AWPathWorkClass.Operational;
            if (pEssentialQueued > 0)
                return AWPathWorkClass.EssentialTravel;
            return AWPathWorkClass.Ambient;
        }
    }

    public sealed class AWLatestPathSlotRules
    {
        private bool _queued;
        private bool _running;

        public int MaximumQueuedNodes { get; private set; }

        public AWPathSlotAction Submit(bool pHasPending, bool pHasRunning)
        {
            _queued |= pHasPending;
            _running |= pHasRunning;
            if (_running) return AWPathSlotAction.StoreAfterRunning;
            if (_queued) return AWPathSlotAction.ReplacePending;
            _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1);
            return AWPathSlotAction.Enqueue;
        }

        public void WorkerStarted()
        {
            _queued = false;
            _running = true;
        }

        public bool WorkerFinished()
        {
            _running = false;
            _queued = true;
            MaximumQueuedNodes = Math.Max(MaximumQueuedNodes, 1);
            return true;
        }
    }

    public static class AWPathLifecycleRules
    {
        private const string CityFoodTaskId = "try_to_eat_city_food";

        public const int MaximumConsecutivePriorityRequests = 8;
        public const double HighPriorityPendingTimeoutSeconds = 2d;
        public const double EssentialTravelPendingTimeoutSeconds = 4d;
        public const double NormalPendingTimeoutSeconds = 8d;
        public const double AcceptedNoProgressTimeoutSeconds = 30d;
        public const double HighPriorityWaitingPollSeconds = 0.1d;
        public const double EssentialTravelWaitingPollSeconds = 0.15d;
        public const double NormalWaitingPollSeconds = 0.25d;
        public const int MaximumSmoothPathStepsPerUpdate = 24;

        public static bool ShouldServeNormalQueue(int pPriorityCount,
            int pNormalCount, int pConsecutivePriorityRequests)
        {
            if (pNormalCount <= 0) return false;
            return pPriorityCount <= 0 ||
                   pConsecutivePriorityRequests >=
                   MaximumConsecutivePriorityRequests;
        }

        public static bool ShouldInspectCustomPathState(bool hasRetryContext,
            bool hasTerminalPoll, bool hasTransportContext)
        {
            return hasRetryContext || hasTerminalPoll || hasTransportContext;
        }

        public static bool ShouldUseCustomSmoothMovement(
            bool hasCustomPathState, bool hasVanillaLocalPath,
            bool hasVanillaGlobalPath)
        {
            return hasCustomPathState && !hasVanillaLocalPath &&
                   !hasVanillaGlobalPath;
        }

        public static bool ShouldExpirePendingRequest(double startedAt,
            double now, bool highPriority)
        {
            return ShouldExpirePendingRequest(startedAt, now,
                highPriority ? AWPathWorkClass.Operational :
                    AWPathWorkClass.Ambient);
        }

        public static bool ShouldExpirePendingRequest(double startedAt,
            double now, AWPathWorkClass pWorkClass)
        {
            if (startedAt < 0d || now < startedAt) return false;
            double timeout = pWorkClass switch
            {
                AWPathWorkClass.Operational =>
                    HighPriorityPendingTimeoutSeconds,
                AWPathWorkClass.EssentialTravel =>
                    EssentialTravelPendingTimeoutSeconds,
                _ => NormalPendingTimeoutSeconds
            };
            return now - startedAt >= timeout;
        }

        public static bool ShouldExpireAcceptedRequest(double startedAt,
            double now)
        {
            return startedAt >= 0d && now >= startedAt &&
                   now - startedAt >= AcceptedNoProgressTimeoutSeconds;
        }

        public static bool ShouldPollWaiting(double now, double nextPollAt)
        {
            return nextPollAt <= 0d || now >= nextPollAt;
        }

        public static double WaitingPollInterval(bool highPriority)
        {
            return WaitingPollInterval(highPriority
                ? AWPathWorkClass.Operational : AWPathWorkClass.Ambient);
        }

        public static double WaitingPollInterval(AWPathWorkClass pWorkClass)
        {
            return pWorkClass switch
            {
                AWPathWorkClass.Operational =>
                    HighPriorityWaitingPollSeconds,
                AWPathWorkClass.EssentialTravel =>
                    EssentialTravelWaitingPollSeconds,
                _ => NormalWaitingPollSeconds
            };
        }

        public static bool ShouldContinueBoundedSegment(int regionLimit,
            int currentTileId, int targetTileId)
        {
            return regionLimit > 0 && currentTileId >= 0 &&
                   targetTileId >= 0 && currentTileId != targetTileId;
        }

        public static bool ShouldKeepMovementOwnership(AWPathPollKind pPollKind,
            bool hasRetryContext, bool retryPending,
            bool hasLiveActorTarget)
        {
            if (!hasLiveActorTarget) return false;
            if (pPollKind == AWPathPollKind.Waiting ||
                pPollKind == AWPathPollKind.StepReady)
                return true;
            return hasRetryContext && retryPending;
        }

        public static bool ShouldContinueBehaviourAfterTerminalFailure(
            string pTaskId)
        {
            return ShouldBypassDecorativePath(pTaskId);
        }

        public static bool ShouldBypassDecorativePath(string pTaskId)
        {
            return string.Equals(pTaskId, CityFoodTaskId,
                StringComparison.Ordinal);
        }

        public static int RetryLimit(AWPathFailureReason pReason)
        {
            switch (pReason)
            {
                case AWPathFailureReason.StepBlocked:
                case AWPathFailureReason.UnsafeStep:
                    return 4;
                case AWPathFailureReason.PortalUnavailable:
                case AWPathFailureReason.TransportFailed:
                case AWPathFailureReason.Timeout:
                    return 2;
                case AWPathFailureReason.GeneratorException:
                    return 1;
                default:
                    return 0;
            }
        }

        public static float RetryDelay(int pAttempt)
        {
            if (pAttempt <= 1) return 0.3f;
            return Math.Min(2f, 0.3f * (1 << Math.Min(3, pAttempt - 1)));
        }

        public static bool AcceptWorldGeneration(int pRequestGeneration,
            int pCurrentGeneration)
        {
            return pRequestGeneration == pCurrentGeneration;
        }
    }

    public sealed class AWPathOwnershipIndex
    {
        private readonly HashSet<long> _actorIds = new HashSet<long>();

        public int Count => _actorIds.Count;
        public bool Contains(long pActorId) => _actorIds.Contains(pActorId);
        public bool Add(long pActorId) => pActorId >= 0 && _actorIds.Add(pActorId);
        public bool Remove(long pActorId) => _actorIds.Remove(pActorId);
        public void Clear() => _actorIds.Clear();
    }
}
