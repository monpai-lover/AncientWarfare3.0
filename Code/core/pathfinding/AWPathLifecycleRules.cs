// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

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

    public readonly struct AWPathReuseKey : IEquatable<AWPathReuseKey>
    {
        public AWPathReuseKey(long actorId, int startRegion,
            AWPathRequestKey request, long terrainRevision,
            long worldGeneration, bool insideBoat)
        {
            ActorId = actorId;
            StartRegion = startRegion;
            Request = request;
            TerrainRevision = terrainRevision;
            WorldGeneration = worldGeneration;
            InsideBoat = insideBoat;
        }

        public long ActorId { get; }
        public int StartRegion { get; }
        public AWPathRequestKey Request { get; }
        public long TerrainRevision { get; }
        public long WorldGeneration { get; }
        public bool InsideBoat { get; }

        public bool Equals(AWPathReuseKey pOther)
        {
            return ActorId == pOther.ActorId &&
                   StartRegion == pOther.StartRegion &&
                   Request.Equals(pOther.Request) &&
                   TerrainRevision == pOther.TerrainRevision &&
                   WorldGeneration == pOther.WorldGeneration &&
                   InsideBoat == pOther.InsideBoat;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathReuseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ActorId.GetHashCode();
                hash = hash * 397 ^ StartRegion;
                hash = hash * 397 ^ Request.GetHashCode();
                hash = hash * 397 ^ TerrainRevision.GetHashCode();
                hash = hash * 397 ^ WorldGeneration.GetHashCode();
                hash = hash * 397 ^ (InsideBoat ? 1 : 0);
                return hash;
            }
        }
    }

    public static class AWPathRequestReuseRules
    {
        public const int MaximumCompletedCapacity = 2048;

        public static bool CanReuse(AWPathReuseKey pExisting,
            AWPathReuseKey pRequested, long ageTicks,
            long maximumAgeTicks)
        {
            return ageTicks >= 0L && maximumAgeTicks >= 0L &&
                   ageTicks <= maximumAgeTicks &&
                   pExisting.ActorId == pRequested.ActorId &&
                   pExisting.Request.Equals(pRequested.Request) &&
                   pExisting.WorldGeneration == pRequested.WorldGeneration &&
                   pExisting.InsideBoat == pRequested.InsideBoat;
        }

        public static int ClampCompletedCapacity(int pCapacity)
        {
            return Math.Max(0, Math.Min(MaximumCompletedCapacity,
                pCapacity));
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
        private const float VanillaEmptyVectorX = -100000f;
        private const float VanillaEmptyVectorY = -10000f;

        public const int MaximumConsecutivePriorityRequests = 8;
        public const double AcceptedNoProgressTimeoutSeconds = 30d;
        public const double HighPriorityWaitingPollSeconds = 0.1d;
        public const double EssentialTravelWaitingPollSeconds = 0.15d;
        public const double NormalWaitingPollSeconds = 0.25d;
        public const int MaximumSmoothPathStepsPerUpdate = 24;

        public static bool IsValidMovementTarget(float pX, float pY)
        {
            if (float.IsNaN(pX) || float.IsNaN(pY) ||
                float.IsInfinity(pX) || float.IsInfinity(pY))
                return false;
            return pX != VanillaEmptyVectorX ||
                   pY != VanillaEmptyVectorY;
        }

        public static bool IsInsideMap(float pX, float pY,
            float pWidth, float pHeight)
        {
            if (float.IsNaN(pX) || float.IsNaN(pY) ||
                float.IsNaN(pWidth) || float.IsNaN(pHeight) ||
                float.IsInfinity(pX) || float.IsInfinity(pY) ||
                float.IsInfinity(pWidth) || float.IsInfinity(pHeight))
                return false;
            return pWidth > 0f && pHeight > 0f &&
                   pX >= 0f && pY >= 0f &&
                   pX < pWidth && pY < pHeight;
        }

        public static bool ShouldReportMovementAnomaly(bool currentOutside,
            bool nextOutside, bool nextTargetEmpty, bool actorMoving)
        {
            return currentOutside || nextOutside ||
                   nextTargetEmpty && actorMoving;
        }

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
            bool largeSchedulerEnabled, bool hasCustomPathState,
            bool hasVanillaLocalPath, bool hasVanillaGlobalPath)
        {
            return largeSchedulerEnabled && hasCustomPathState &&
                   !hasVanillaLocalPath && !hasVanillaGlobalPath;
        }

        public static bool ShouldAdvanceSmoothMovement(bool actorMoving,
            bool nextStepValid)
        {
            return actorMoving && nextStepValid;
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
            // Each Actor owns one latest queued slot, so worker backlog cannot
            // accumulate stale requests that need wall-clock expiry.
            return false;
        }

        public static bool ShouldExpireAcceptedRequest(double startedAt,
            double now)
        {
            return startedAt >= 0d && now >= startedAt &&
                   now - startedAt >= AcceptedNoProgressTimeoutSeconds;
        }

        public static double ResolveWorkerWatchdogBaseline(
            double currentBaseline, double now)
        {
            return currentBaseline >= 0d ? currentBaseline : now;
        }

        public static bool ShouldPollWaiting(double now, double nextPollAt)
        {
            return nextPollAt <= 0d || now >= nextPollAt;
        }

        public static bool ShouldPollEverySimulationPass(bool schedulerActive,
            bool customPathOwned)
        {
            // The large scheduler owns movement cadence. Its Actors must
            // observe ready segments on the next simulation pass instead of
            // waiting for a wall-clock retry timer.
            return schedulerActive && customPathOwned;
        }

        public static bool ShouldResetActorAfterPathSubmission(
            bool accepted, bool reused, bool wasMoving)
        {
            return accepted && !reused && !wasMoving;
        }

        public static bool ShouldClearPreviousPathAfterSubmission(
            bool accepted, bool reused)
        {
            return accepted && !reused;
        }

        public static bool HasUsableMovementBatch(bool actorExists,
            bool actorAlive, bool batchExists, bool movementQueueExists)
        {
            return actorExists && actorAlive && batchExists &&
                   movementQueueExists;
        }

        public static float NormalizeMovementElapsed(float elapsed,
            float fallback, bool schedulerActive)
        {
            if (!schedulerActive) return Math.Max(0f, elapsed);
            if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) ||
                elapsed <= 0f)
                return float.IsNaN(fallback) || float.IsInfinity(fallback)
                    ? FixedSchedulerElapsedSeconds
                    : Math.Max(FixedSchedulerElapsedSeconds, fallback);
            return elapsed;
        }

        private const float FixedSchedulerElapsedSeconds = 0.02f;

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

        public static int ResolveEmittedStepCount(bool segmented,
            int regionLimit, int plannedStepCount, int segmentTargetSteps)
        {
            int planned = Math.Max(0, plannedStepCount);
            if (!segmented || regionLimit <= 0) return planned;
            return Math.Min(planned, Math.Max(1, segmentTargetSteps));
        }

        public static bool AcceptTraversalRevision(long requestRevision,
            long currentRevision)
        {
            return requestRevision == currentRevision;
        }

        public static bool ShouldContinuePathSegment(int currentTileId,
            int targetTileId)
        {
            return currentTileId >= 0 && targetTileId >= 0 &&
                   currentTileId != targetTileId;
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
        private readonly ConcurrentDictionary<long, byte> _actorIds =
            new ConcurrentDictionary<long, byte>();

        public int Count => _actorIds.Count;
        public bool Contains(long pActorId) => _actorIds.ContainsKey(pActorId);
        public bool Add(long pActorId) =>
            pActorId >= 0 && _actorIds.TryAdd(pActorId, 0);
        public bool Remove(long pActorId) =>
            _actorIds.TryRemove(pActorId, out _);
        public void Clear() => _actorIds.Clear();
    }

    public sealed class AWPathActorGateLease : IDisposable
    {
        private AWPathActorGateIndex _owner;
        private readonly object _state;

        internal AWPathActorGateLease(AWPathActorGateIndex pOwner,
            object pState, object pGate)
        {
            _owner = pOwner;
            _state = pState;
            Gate = pGate;
        }

        internal object State => _state;
        internal object Gate { get; }

        public void Retire()
        {
            Volatile.Read(ref _owner)?.Retire(this);
        }

        public void Dispose()
        {
            AWPathActorGateIndex owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(this);
        }
    }

    public sealed class AWPathActorGateIndex
    {
        private sealed class GateState
        {
            internal GateState(long pActorId)
            {
                ActorId = pActorId;
            }

            internal readonly long ActorId;
            internal readonly object Gate = new object();
            internal readonly object MetadataGate = new object();
            internal int ActiveLeases;
            internal int Retired;
        }

        private readonly ConcurrentDictionary<long, GateState> _gates =
            new ConcurrentDictionary<long, GateState>();
        private int _clearing;
        private int _activeAdmissions;
        private int _activeLeases;

        public int Count => _gates.Count;

        public bool TryAcquire(long pActorId,
            out AWPathActorGateLease pLease)
        {
            pLease = null;
            if (pActorId < 0L || Volatile.Read(ref _clearing) != 0)
                return false;

            Interlocked.Increment(ref _activeAdmissions);
            GateState state = null;
            bool reserved = false;
            try
            {
                while (true)
                {
                    if (Volatile.Read(ref _clearing) != 0) return false;
                    state = _gates.GetOrAdd(pActorId,
                        id => new GateState(id));
                    bool retry = false;
                    lock (state.MetadataGate)
                    {
                        if (Volatile.Read(ref _clearing) != 0)
                            return false;
                        if (state.Retired != 0)
                        {
                            if (state.ActiveLeases == 0)
                            {
                                RemoveRetiredGate(state);
                                retry = true;
                            }
                            else
                            {
                                Volatile.Write(ref state.Retired, 0);
                            }
                        }
                        if (!retry)
                        {
                            state.ActiveLeases++;
                            Interlocked.Increment(ref _activeLeases);
                            reserved = true;
                        }
                    }
                    if (!retry) break;
                }

                Monitor.Enter(state.Gate);
                pLease = new AWPathActorGateLease(this, state,
                    state.Gate);
                reserved = false;
                return true;
            }
            catch
            {
                if (reserved) ReleaseReservedLease(state);
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activeAdmissions);
            }
        }

        public bool BeginClear()
        {
            if (Interlocked.CompareExchange(ref _clearing, 1, 0) != 0)
                return false;

            SpinWait wait = new SpinWait();
            while (Volatile.Read(ref _activeAdmissions) != 0 ||
                   Volatile.Read(ref _activeLeases) != 0)
                wait.SpinOnce();

            _gates.Clear();
            return true;
        }

        public void EndClear()
        {
            Volatile.Write(ref _clearing, 0);
        }

        internal void Retire(AWPathActorGateLease pLease)
        {
            if (!(pLease?.State is GateState state)) return;
            lock (state.MetadataGate)
            {
                Volatile.Write(ref state.Retired, 1);
                RemoveRetiredGate(state);
            }
        }

        internal void Release(AWPathActorGateLease pLease)
        {
            if (!(pLease?.State is GateState state)) return;
            Monitor.Exit(state.Gate);
            lock (state.MetadataGate)
            {
                if (state.ActiveLeases > 0) state.ActiveLeases--;
                RemoveRetiredGate(state);
            }
            Interlocked.Decrement(ref _activeLeases);
        }

        private void ReleaseReservedLease(GateState pState)
        {
            lock (pState.MetadataGate)
            {
                if (pState.ActiveLeases > 0) pState.ActiveLeases--;
                RemoveRetiredGate(pState);
            }
            Interlocked.Decrement(ref _activeLeases);
        }

        private void RemoveRetiredGate(GateState pState)
        {
            if (Volatile.Read(ref pState.Retired) == 0 ||
                pState.ActiveLeases != 0)
                return;
            ((ICollection<KeyValuePair<long, GateState>>)_gates).Remove(
                new KeyValuePair<long, GateState>(pState.ActorId, pState));
        }
    }
}
