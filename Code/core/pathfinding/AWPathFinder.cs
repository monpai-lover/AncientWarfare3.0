// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using ThreadPriority = System.Threading.ThreadPriority;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.pathfinding
{
    public interface IAWPathGenerator
    {
        void Generate(AWPathRequest pRequest, CancellationToken pCancellation);
    }

    public interface IAWPathSegmentGenerator : IAWPathGenerator
    {
        AWPathGenerationResult GenerateSegment(AWPathRequest pRequest,
            CancellationToken pCancellation, int pMaximumSteps);
    }

    public enum AWPathSubmissionDisposition
    {
        Reused,
        Submitted,
        ReplacedPending,
        ReplacedRunning,
        Rejected
    }

    internal enum AWPathRecoveryScheduleResult : byte
    {
        NotOwned,
        Scheduled,
        AlreadyPending,
        Exhausted
    }

    internal readonly struct AWPathQueueSnapshot
    {
        internal AWPathQueueSnapshot(int pOperationalQueued,
            int pEssentialQueued, int pAmbientQueued,
            int pOperationalActive, int pEssentialActive,
            int pAmbientActive)
        {
            OperationalQueued = pOperationalQueued;
            EssentialQueued = pEssentialQueued;
            AmbientQueued = pAmbientQueued;
            OperationalActive = pOperationalActive;
            EssentialActive = pEssentialActive;
            AmbientActive = pAmbientActive;
        }

        internal int OperationalQueued { get; }
        internal int EssentialQueued { get; }
        internal int AmbientQueued { get; }
        internal int OperationalActive { get; }
        internal int EssentialActive { get; }
        internal int AmbientActive { get; }
    }

    internal readonly struct AWPathSessionState
    {
        internal AWPathSessionState(bool pHasRequest, bool pHasQueued,
            bool pHasRunning, bool pIsLatestQueued,
            bool pIsLatestRunning, AWPathRequestState pRequestState,
            AWPathFailureReason pFailureReason, bool pHasPendingSteps)
        {
            HasRequest = pHasRequest;
            HasQueued = pHasQueued;
            HasRunning = pHasRunning;
            IsLatestQueued = pIsLatestQueued;
            IsLatestRunning = pIsLatestRunning;
            RequestState = pRequestState;
            FailureReason = pFailureReason;
            HasPendingSteps = pHasPendingSteps;
        }

        internal static AWPathSessionState None => default;
        internal bool HasRequest { get; }
        internal bool HasQueued { get; }
        internal bool HasRunning { get; }
        internal bool IsLatestQueued { get; }
        internal bool IsLatestRunning { get; }
        internal AWPathRequestState RequestState { get; }
        internal AWPathFailureReason FailureReason { get; }
        internal bool HasPendingSteps { get; }

        internal AWPathPollKind DiagnosticPollKind
        {
            get
            {
                if (!HasRequest) return AWPathPollKind.NoRequest;
                if (HasPendingSteps) return AWPathPollKind.StepReady;
                return RequestState switch
                {
                    AWPathRequestState.Succeeded => AWPathPollKind.Completed,
                    AWPathRequestState.Failed => AWPathPollKind.Failed,
                    AWPathRequestState.Cancelled => AWPathPollKind.Cancelled,
                    _ => AWPathPollKind.Waiting
                };
            }
        }
    }

    public sealed class AWPathFinder : IDisposable
    {
        // Matches Cultiway's actor lifecycle boundary. Disposal can race a
        // final movement callback while the finder is replacing a session.
        internal static readonly object ActorSyncLock = new object();
        private const int SegmentStepBudget = 24;
        private const int SegmentLowWatermark = 8;
        private IAWPathGenerator _generator;
        private readonly AWPathDiagnostics _diagnostics;
        private readonly Func<AWPathAgentKey, AWPathRequest,
            AWPathRequest> _recoveryRequestFactory;
        private readonly ConcurrentDictionary<AWPathAgentKey, PathSessionRecord> _sessions =
            new ConcurrentDictionary<AWPathAgentKey, PathSessionRecord>();
        private readonly ConcurrentDictionary<AWPathAgentKey, long> _submissionTokens =
            new ConcurrentDictionary<AWPathAgentKey, long>();
        // Keep the latest immutable request inputs after a terminal session is
        // cleaned up. Cultiway uses this snapshot for an explicit recovery
        // request instead of asking the worker or movement callback to rebuild
        // ownership state.
        private readonly ConcurrentDictionary<AWPathAgentKey,
            AWPathRequestRecoverySnapshot> _lastRequests =
            new ConcurrentDictionary<AWPathAgentKey,
                AWPathRequestRecoverySnapshot>();
        // Compatibility for the existing AW3 actor-facing API. The lookup is
        // only an adapter; all ownership and cleanup decisions use AgentKey.
        private readonly ConcurrentDictionary<long, AWPathAgentKey> _legacyKeys =
            new ConcurrentDictionary<long, AWPathAgentKey>();
        private readonly ConcurrentDictionary<AWPathAgentKey, byte> _openCursors =
            new ConcurrentDictionary<AWPathAgentKey, byte>();
        private readonly ConcurrentQueue<AWPathRecoveryTicket> _pendingRecoveries =
            new ConcurrentQueue<AWPathRecoveryTicket>();
        private readonly List<AWPathScheduledRecovery> _scheduledRecoveries =
            new List<AWPathScheduledRecovery>();
        private readonly Dictionary<AWPathAgentKey, AWPathRecoveryState>
            _recoveryStates = new Dictionary<AWPathAgentKey, AWPathRecoveryState>();
        private readonly object _requestGate = new object();
        private readonly ConcurrentQueue<AWScheduledPathWork> _starvedQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly ConcurrentQueue<AWScheduledPathWork> _initialQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly ConcurrentQueue<AWScheduledPathWork> _continuationQueue =
            new ConcurrentQueue<AWScheduledPathWork>();
        private readonly SemaphoreSlim _queueSignal = new SemaphoreSlim(0);
        private Thread[] _workers = Array.Empty<Thread>();
        private int _started;
        private int _stopping;
        private int _queueDepth;
        private int _consecutiveStarvedWork;
        private long _staleWorkCount;
        private long _nextSubmissionToken;

        public AWPathFinder(IAWPathGenerator pGenerator)
            : this(pGenerator, null)
        {
        }

        internal AWPathFinder(IAWPathGenerator pGenerator,
            AWPathDiagnostics pDiagnostics)
            : this(pGenerator, pDiagnostics, null)
        {
        }

        internal AWPathFinder(IAWPathGenerator pGenerator,
            AWPathDiagnostics pDiagnostics,
            Func<AWPathAgentKey, AWPathRequest, AWPathRequest>
                pRecoveryRequestFactory)
        {
            _generator = pGenerator ?? throw new ArgumentNullException(nameof(pGenerator));
            _diagnostics = pDiagnostics;
            _recoveryRequestFactory = pRecoveryRequestFactory;
        }

        public int ActiveCount => _sessions.Count;
        public int QueueDepth => Math.Max(0, Volatile.Read(ref _queueDepth));
        public int WorkerCount => _workers.Length;
        public long StaleWorkCount => Interlocked.Read(ref _staleWorkCount);

        public void UseGenerator(IAWPathGenerator pGenerator)
        {
            if (pGenerator == null) throw new ArgumentNullException(
                nameof(pGenerator));
            if (Volatile.Read(ref _started) != 0)
                throw new InvalidOperationException(
                    "The path generator cannot change after workers start.");
            _generator = pGenerator;
        }

        public void Initialize()
        {
#if AW3_RULES_TESTS
            Start(1);
#else
            Start(AWPerformanceSettings.ActorPathfindingWorkerCount);
#endif
        }

        public void Shutdown()
        {
            StopAndDrain();
        }

#if !AW3_RULES_TESTS
        public bool CanAcceptRequest(Actor pActor, WorldTile pTarget,
            out AWPathFailureReason pFailureReason)
        {
            if (pActor?.data == null || pActor.asset == null)
            {
                pFailureReason = AWPathFailureReason.InvalidActor;
                return false;
            }
            if (pActor.current_tile?.data == null)
            {
                pFailureReason = AWPathFailureReason.InvalidStart;
                return false;
            }
            if (pTarget?.data == null ||
                (pActor.asset.is_boat && !pTarget.isGoodForBoat()))
            {
                pFailureReason = AWPathFailureReason.InvalidTarget;
                return false;
            }
            pFailureReason = AWPathFailureReason.None;
            return true;
        }
#endif

        public string GetDiagnostics()
        {
            AWPathQueueSnapshot queues = SnapshotQueues();
            var builder = new StringBuilder(256);
            builder.Append("sessions=").Append(ActiveCount)
                .Append(" queue=").Append(QueueDepth)
                .Append(" workers=").Append(WorkerCount)
                .Append(" stale=").Append(StaleWorkCount)
                .Append(" operational_queued=").Append(queues.OperationalQueued)
                .Append(" essential_queued=").Append(queues.EssentialQueued)
                .Append(" ambient_queued=").Append(queues.AmbientQueued);
            return builder.ToString();
        }

        /// <summary>
        /// Main-thread lifecycle boundary copied from Cultiway's PathFinder.Tick.
        /// Recovery submission is deliberately centralized here; actor movement
        /// callbacks only report failures and never submit a second retry.
        /// </summary>
        public void Tick()
        {
#if AW3_RULES_TESTS
            return;
#else
            if (Volatile.Read(ref _stopping) != 0 ||
                !AncientWarfare3.core.performance.AWSimulationTime.IsBound)
                return;
            double now = AncientWarfare3.core.performance.AWSimulationTime.Now;
            lock (_requestGate)
            {
                while (_pendingRecoveries.TryDequeue(
                    out AWPathRecoveryTicket ticket))
                {
                    if (!_sessions.TryGetValue(ticket.AgentKey,
                            out PathSessionRecord record) ||
                        !ReferenceEquals(record.Latest, ticket.ExpectedTask) ||
                        record.Latest?.Request?.Stream == null ||
                        !IsTerminal(record.Latest.Request.Stream.State))
                        continue;
                    _scheduledRecoveries.Add(new AWPathScheduledRecovery(
                        ticket.AgentKey, ticket.ExpectedTask,
                        ticket.Reason, ticket.Attempt, now + ticket.DelaySeconds));
                }

                for (int index = _scheduledRecoveries.Count - 1;
                     index >= 0; index--)
                {
                    AWPathScheduledRecovery retry = _scheduledRecoveries[index];
                    if (now < retry.DueTime) continue;
                    _scheduledRecoveries.RemoveAt(index);
                    ActivateRecoveryLocked(retry);
                }
            }
#endif
        }

        internal AWPathQueueSnapshot SnapshotQueues()
        {
            lock (_requestGate) return SnapshotQueuesLocked();
        }

        public void Start(int pWorkers)
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            int count = Math.Max(1, Math.Min(8, pWorkers));
            _workers = new Thread[count];
            for (int i = 0; i < count; i++)
            {
                var thread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "AW3 Path Worker " + (i + 1),
                    Priority = ThreadPriority.Normal
                };
                _workers[i] = thread;
                thread.Start();
            }
        }

        public bool Request(AWPathRequest pRequest, out bool pReused)
        {
            bool accepted = Request(pRequest,
                out AWPathSubmissionDisposition disposition);
            pReused = disposition == AWPathSubmissionDisposition.Reused;
            return accepted;
        }

        public bool Request(AWPathRequest pRequest,
            out AWPathSubmissionDisposition pDisposition)
        {
            return RequestCore(pRequest, pAllowReuse: true,
                out pDisposition);
        }

#if !AW3_RULES_TESTS
        public AWPathSubmissionResult RequestPathDetailed(Actor pActor,
            WorldTile pTarget, bool pPathOnWater, bool pWalkOnBlocks,
            bool pWalkOnLava, int pLimitRegions)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.current_tile?.data == null)
                return new AWPathSubmissionResult(
                    AWPathSubmissionKind.Rejected,
                    pActor?.data == null
                        ? AWPathFailureReason.InvalidActor
                        : AWPathFailureReason.InvalidTarget);

            AWTraversalGeneration generation =
                AWPathfindingBootstrap.Cache?.Pin();
            if (generation == null)
                return new AWPathSubmissionResult(
                    AWPathSubmissionKind.Rejected,
                    AWPathFailureReason.StaleTraversal);
            try
            {
                AWPathAgentKey agentKey = ResolveLegacyKey(pActor.data.id);
                AWPathRequestOptions options = new AWPathRequestOptions(
                    pPathOnWater, pWalkOnBlocks, pWalkOnLava,
                    pLimitRegions);
                bool physicalTransport = AWDockTransportService.TryResolveRoute(
                    pActor.current_tile, pTarget,
                    out AWDockRouteCandidate route);
                var request = new AWPathRequest(agentKey,
                    pActor.current_tile.data.tile_id, pTarget.data.tile_id,
                    options, AWPathMovementBridge.CaptureProfile(pActor),
                    generation,
                    AncientWarfare3.core.performance.AWSimulationTime.IsBound
                        ? AncientWarfare3.core.performance.AWSimulationTime.Now
                        : UnityEngine.Time.realtimeSinceStartupAsDouble,
                    AWPathWorkClass.Ambient,
                    AWPathfindingBootstrap.Cache.SourceRevision,
                    agentKey.World.Generation, pActor.is_inside_boat,
                    physicalTransport);
                if (physicalTransport)
                    request.SetTransportRoute(
                        AWTransportRouteSnapshot.FromRoute(route));
                bool accepted = RequestCore(request, pAllowReuse: true,
                    out AWPathSubmissionDisposition disposition);
                if (!accepted)
                    return new AWPathSubmissionResult(
                        AWPathSubmissionKind.Rejected);
                long token = TryGetCurrentSubmissionToken(agentKey,
                    out long currentToken) ? currentToken : 0L;
                return new AWPathSubmissionResult(ToSubmissionKind(disposition),
                    pSubmissionToken: token);
            }
            finally
            {
                generation.Dispose();
            }
        }

        public bool RequestPath(Actor pActor, WorldTile pTarget,
            bool pPathOnWater, bool pWalkOnBlocks, bool pWalkOnLava,
            int pLimitRegions)
        {
            return RequestPathDetailed(pActor, pTarget, pPathOnWater,
                pWalkOnBlocks, pWalkOnLava, pLimitRegions).Accepted;
        }
#endif

        public AWPathSubmissionResult RequestPathDetailed(
            AWPathRequest pRequest)
        {
            if (pRequest == null || !pRequest.AgentKey.IsValid)
                return new AWPathSubmissionResult(
                    AWPathSubmissionKind.Rejected,
                    AWPathFailureReason.InvalidActor);
            bool accepted = RequestCore(pRequest, pAllowReuse: true,
                out AWPathSubmissionDisposition disposition);
            if (!accepted)
                return new AWPathSubmissionResult(
                    AWPathSubmissionKind.Rejected);
            long token = TryGetCurrentSubmissionToken(pRequest.AgentKey,
                out long currentToken) ? currentToken : 0L;
            return new AWPathSubmissionResult(ToSubmissionKind(disposition),
                pSubmissionToken: token);
        }

        public bool RequestPath(AWPathRequest pRequest)
        {
            return RequestPathDetailed(pRequest).Accepted;
        }

#if !AW3_RULES_TESTS
        public void RequestDirectPath(Actor pActor, WorldTile pTarget)
        {
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.current_tile?.data == null) return;
            AWTraversalGeneration generation =
                AWPathfindingBootstrap.Cache?.Pin();
            if (generation == null) return;
            try
            {
                AWPathAgentKey agentKey = ResolveLegacyKey(pActor.data.id);
                var request = new AWPathRequest(agentKey,
                    pActor.current_tile.data.tile_id, pTarget.data.tile_id,
                    new AWPathRequestOptions(true, true, true, 0),
                    AWPathMovementBridge.CaptureProfile(pActor), generation,
                    AncientWarfare3.core.performance.AWSimulationTime.IsBound
                        ? AncientWarfare3.core.performance.AWSimulationTime.Now
                        : UnityEngine.Time.realtimeSinceStartupAsDouble,
                    AWPathWorkClass.Operational,
                    AWPathfindingBootstrap.Cache.SourceRevision,
                    agentKey.World.Generation, pActor.is_inside_boat,
                    AWDockTransportService.TryResolveRoute(
                        pActor.current_tile, pTarget, out _));
                request.Stream.AddStep(new AWPathStep(pTarget.data.tile_id,
                    AWMovementMethod.Walk, AWTraversalEstimate.Direct));
                request.Stream.Complete();
                SubmitDirect(request, out _);
            }
            finally
            {
                generation.Dispose();
            }
        }
#endif

        // SubmitNew is used after the caller has performed the reuse check.
        // Keeping this path separate matches the master request lifecycle and
        // avoids repeating the same locked lookup for every new request.
        internal bool SubmitNew(AWPathRequest pRequest,
            out AWPathSubmissionDisposition pDisposition)
        {
            return RequestCore(pRequest, pAllowReuse: false,
                out pDisposition);
        }

        // Direct correction paths are already complete when they are
        // submitted. Keep them out of the worker queues entirely; the
        // movement bridge can consume the ready step immediately and the
        // session cannot schedule a continuation after it is consumed.
        private bool SubmitDirect(AWPathRequest pRequest,
            out AWPathSubmissionDisposition pDisposition)
        {
            pDisposition = AWPathSubmissionDisposition.Rejected;
            if (pRequest == null || Volatile.Read(ref _started) == 0 ||
                Volatile.Read(ref _stopping) != 0)
            {
                Reject(pRequest, pDisposition);
                return false;
            }

            long currentWorldGeneration;
#if AW3_RULES_TESTS
            currentWorldGeneration = pRequest.ReuseKey.WorldGeneration;
#else
            currentWorldGeneration =
                AncientWarfare3.core.asyncwork.AWAsyncRuntime.WorldGeneration;
#endif
            AWPathFailureReason validation =
                AWPathRequestValidationRules.Validate(pRequest,
                    currentWorldGeneration);
            if (validation != AWPathFailureReason.None)
            {
                Reject(pRequest, pDisposition, validation);
                return false;
            }

            lock (_requestGate)
            {
                AWPathAgentKey agentKey = pRequest.AgentKey;
                _legacyKeys[pRequest.ActorId] = agentKey;
                _recoveryStates.Remove(agentKey);
                RecordLastRequestLocked(agentKey, pRequest);

                if (_sessions.TryGetValue(agentKey,
                        out PathSessionRecord existing))
                {
                    bool existingActive = existing.Running != null ||
                        existing.Latest?.Request?.Stream?.HasPendingSteps == true;
                    _openCursors.TryRemove(agentKey, out _);
                    _sessions.TryRemove(agentKey, out _);
                    CancelRecordLocked(existing,
                        AWPathFailureReason.CancelledByNewRequest);
                    pDisposition = existingActive
                        ? AWPathSubmissionDisposition.ReplacedRunning
                        : AWPathSubmissionDisposition.ReplacedPending;
                }

#if !AW3_RULES_TESTS
                AWPathfindingProfiler.AWPathfindingProfilerMeasurement taskCreateMeasurement =
                    AWPathfindingProfiler.Start();
#endif
                var task = new PathfindingTask(pRequest);
#if !AW3_RULES_TESTS
                taskCreateMeasurement.Complete(
                    AWPathfindingBenchmarkMetric.TaskCreate);
#endif
                var record = new PathSessionRecord(agentKey, task,
                    pHasMoreSegments: false);
#if !AW3_RULES_TESTS
                record.BenchmarkSession = taskCreateMeasurement.Session;
#endif
                if (!_sessions.TryAdd(agentKey, record))
                {
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorkerIfNotStarted();
                    Reject(null, pDisposition);
                    return false;
                }

                // No worker will ever claim this completed request.
                task.ReleaseWorkerIfNotStarted();
                if (pDisposition == AWPathSubmissionDisposition.Rejected)
                    pDisposition = AWPathSubmissionDisposition.Submitted;
                RecordSubmissionLocked(agentKey);
                _diagnostics?.OnSubmission(pRequest.WorkClass,
                    pDisposition);
                return true;
            }
        }

        private bool RequestCore(AWPathRequest pRequest, bool pAllowReuse,
            out AWPathSubmissionDisposition pDisposition)
        {
#if AW3_RULES_TESTS
            return RequestCoreImpl(pRequest, pAllowReuse,
                out pDisposition);
#else
            AWPathfindingProfiler.AWPathfindingProfilerMeasurement measurement =
                AWPathfindingProfiler.Start();
            AWPathSubmissionDisposition disposition =
                AWPathSubmissionDisposition.Rejected;
            try
            {
                bool accepted = RequestCoreImpl(pRequest, pAllowReuse,
                    out disposition);
                pDisposition = disposition;
                return accepted;
            }
            finally
            {
#if !AW3_RULES_TESTS
                if (disposition != AWPathSubmissionDisposition.Reused &&
                    disposition != AWPathSubmissionDisposition.Rejected)
                    AWPathfindingProfiler.RecordInstant(measurement.Session,
                        AWPathfindingBenchmarkMetric.ReuseMiss);
#endif
                measurement.Complete(disposition ==
                    AWPathSubmissionDisposition.Reused
                    ? AWPathfindingBenchmarkMetric.Reuse
                    : disposition == AWPathSubmissionDisposition.Rejected
                        ? AWPathfindingBenchmarkMetric.CancelEmpty
                        : AWPathfindingBenchmarkMetric.Create);
            }
#endif
        }

        private bool RequestCoreImpl(AWPathRequest pRequest, bool pAllowReuse,
            out AWPathSubmissionDisposition pDisposition)
        {
            pDisposition = AWPathSubmissionDisposition.Rejected;
            if (pRequest == null || Volatile.Read(ref _started) == 0 ||
                Volatile.Read(ref _stopping) != 0)
            {
                Reject(pRequest, pDisposition);
                return false;
            }

            long currentWorldGeneration;
#if AW3_RULES_TESTS
            currentWorldGeneration = pRequest.ReuseKey.WorldGeneration;
#else
            currentWorldGeneration =
                AncientWarfare3.core.asyncwork.AWAsyncRuntime.WorldGeneration;
#endif
            AWPathFailureReason validation =
                AWPathRequestValidationRules.Validate(pRequest,
                    currentWorldGeneration);
            if (validation != AWPathFailureReason.None)
            {
                Reject(pRequest, pDisposition, validation);
                return false;
            }

            lock (_requestGate)
            {
                AWPathAgentKey agentKey = pRequest.AgentKey;
                _legacyKeys[pRequest.ActorId] = agentKey;
                _recoveryStates.Remove(agentKey);
                RecordLastRequestLocked(agentKey, pRequest);
                if (_sessions.TryGetValue(agentKey,
                        out PathSessionRecord existing))
                {
                    PathfindingTask latest = existing.Latest;
                    if (pAllowReuse && CanReuse(agentKey, latest,
                            pRequest.ReuseKey))
                    {
                        pRequest.Dispose();
                        pDisposition = AWPathSubmissionDisposition.Reused;
                        if (existing.Running != null)
                            _diagnostics?.OnReusedRunning();
                        _diagnostics?.OnSubmission(latest.Request.WorkClass,
                            pDisposition);
                        return true;
                    }

                    // A completed worker may have already released its
                    // thread slot while its stream still owns ready steps;
                    // replacing that live stream is still a running-session
                    // replacement from the caller's ownership perspective.
                    bool existingActive = existing.Running != null ||
                        existing.Latest?.Request?.Stream?.HasPendingSteps == true;
                    _openCursors.TryRemove(agentKey, out _);
                    pDisposition = existingActive
                        ? AWPathSubmissionDisposition.ReplacedRunning
                        : AWPathSubmissionDisposition.ReplacedPending;
                    ReplaceLocked(existing, pRequest);
                    RecordSubmissionLocked(agentKey);
                    _diagnostics?.OnSubmission(pRequest.WorkClass,
                        pDisposition);
                    return true;
                }

#if !AW3_RULES_TESTS
                AWPathfindingProfiler.AWPathfindingProfilerMeasurement taskCreateMeasurement =
                    AWPathfindingProfiler.Start();
#endif
                var task = new PathfindingTask(pRequest);
#if !AW3_RULES_TESTS
                taskCreateMeasurement.Complete(
                    AWPathfindingBenchmarkMetric.TaskCreate);
#endif
                var record = new PathSessionRecord(agentKey, task);
#if !AW3_RULES_TESTS
                record.BenchmarkSession = taskCreateMeasurement.Session;
#endif
                if (!_sessions.TryAdd(agentKey, record))
                {
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorkerIfNotStarted();
                    Reject(null, pDisposition);
                    return false;
                }
                record.Queued = task;
                if (!ScheduleLocked(record, PriorityFor(pRequest.WorkClass)))
                {
                    _sessions.TryRemove(agentKey, out _);
                    task.Cancel(AWPathFailureReason.CancelledByNewRequest);
                    task.ReleaseOwner();
                    task.ReleaseWorkerIfNotStarted();
                    Reject(null, pDisposition);
                    return false;
                }
                pDisposition = AWPathSubmissionDisposition.Submitted;
                _openCursors.TryRemove(agentKey, out _);
                RecordSubmissionLocked(agentKey);
                _diagnostics?.OnSubmission(pRequest.WorkClass, pDisposition);
                return true;
            }
        }

        public bool TryReuse(AWPathReuseKey pReuseKey)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pReuseKey.AgentKey,
                        out PathSessionRecord record) ||
                    !CanReuse(pReuseKey.AgentKey, record.Latest,
                        pReuseKey)) return false;
                if (record.Running != null) _diagnostics?.OnReusedRunning();
                _diagnostics?.OnSubmission(record.Latest.Request.WorkClass,
                    AWPathSubmissionDisposition.Reused);
                return true;
            }
        }

        // Actor-facing compatibility overload used by the movement bridge.
        // The key remains the authoritative identity and request fingerprint;
        // the explicit actor id only prevents an accidental cross-actor reuse.
        public bool TryReuse(long pActorId, AWPathReuseKey pReuseKey)
        {
            if (pReuseKey.ActorId != pActorId) return false;
            return TryReuse(pReuseKey);
        }

        public bool IsWaitingForWorker(long pActorId)
        {
            return IsWaitingForWorker(ResolveLegacyKey(pActorId));
        }

        public bool IsWaitingForWorker(AWPathAgentKey pAgentKey)
        {
            return _sessions.TryGetValue(pAgentKey, out PathSessionRecord record) &&
                   record.Queued != null && ReferenceEquals(record.Latest,
                       record.Queued);
        }

        public bool IsWorkerRunning(long pActorId)
        {
            return IsWorkerRunning(ResolveLegacyKey(pActorId));
        }

        public bool IsWorkerRunning(AWPathAgentKey pAgentKey)
        {
            return _sessions.TryGetValue(pAgentKey, out PathSessionRecord record) &&
                   record.Running != null && ReferenceEquals(record.Latest,
                       record.Running);
        }

        internal AWPathSessionState ReadState(long pActorId)
        {
            return ReadState(ResolveLegacyKey(pActorId));
        }

        internal AWPathSessionState ReadState(AWPathAgentKey pAgentKey)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    record.Latest?.Request?.Stream == null)
                    return AWPathSessionState.None;

                PathfindingTask latest = record.Latest;
                AWPathStream stream = latest.Request.Stream;
                return new AWPathSessionState(
                    pHasRequest: true,
                    pHasQueued: record.Queued != null,
                    pHasRunning: record.Running != null,
                    pIsLatestQueued: ReferenceEquals(latest, record.Queued),
                    pIsLatestRunning: ReferenceEquals(latest, record.Running),
                    pRequestState: stream.State,
                    pFailureReason: stream.FailureReason,
                    pHasPendingSteps: stream.HasPendingSteps);
            }
        }

        public AWPathPollResult Poll(long pActorId)
        {
            return Poll(ResolveLegacyKey(pActorId));
        }

        public AWPathPollResult Poll(AWPathAgentKey pAgentKey)
        {
            if (!_sessions.TryGetValue(pAgentKey, out PathSessionRecord record))
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            return PollOwned(pAgentKey, record.Latest);
        }

#if !AW3_RULES_TESTS
        // Actor-facing parity with Cultiway's PollStep/IsActorPathing APIs.
        // The actor id is only an adapter; the world-scoped key remains the
        // owner of the request and all cleanup decisions.
        public bool IsActorPathing(Actor pActor)
        {
            if (pActor?.data == null) return false;
            AWPathAgentKey agentKey = ResolveLegacyKey(pActor.data.id);
            lock (_requestGate)
            {
                return _sessions.TryGetValue(agentKey,
                           out PathSessionRecord record) &&
                       record.Latest?.Request?.Stream != null &&
                       record.Latest.Request.Stream.State !=
                           AWPathRequestState.Cancelled;
            }
        }

        public List<AWPathStep> TryViewAll(Actor pActor)
        {
            if (pActor?.data == null) return null;
            AWPathAgentKey agentKey = ResolveLegacyKey(pActor.data.id);
            lock (_requestGate)
            {
                return _sessions.TryGetValue(agentKey,
                           out PathSessionRecord record) &&
                       record.Latest?.Request?.Stream != null
                    ? record.Latest.Request.Stream.TryViewAll()
                    : null;
            }
        }

        public AWPathPollResult PollStep(Actor pActor)
        {
            if (pActor?.data == null)
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            return Poll(pActor.data.id);
        }

        public AWPathPollResult PeekReadyStep(Actor pActor,
            out ReadyPathStep pReadyStep)
        {
            pReadyStep = default(ReadyPathStep);
            if (pActor?.data == null)
                return new AWPathPollResult(AWPathPollKind.NoRequest);
            AWPathPollResult result = OpenReadyCursor(
                pActor.data.id, out ReadyPathCursor cursor);
            if (result.Kind == AWPathPollKind.StepReady)
                pReadyStep = new ReadyPathStep(cursor, result.Step);
            return result;
        }

        public bool TryPeekStep(Actor pActor, out AWPathStep pStep,
            out bool pFinished)
        {
            pStep = default(AWPathStep);
            pFinished = false;
            AWPathPollResult result = PollStep(pActor);
            if (result.Kind == AWPathPollKind.StepReady)
            {
                pStep = result.Step;
                return true;
            }
            pFinished = result.Kind != AWPathPollKind.Waiting;
            return false;
        }
#endif

        public AWPathPollResult OpenReadyCursor(long pActorId,
            out ReadyPathCursor pCursor)
        {
            return OpenReadyCursor(ResolveLegacyKey(pActorId), out pCursor);
        }

        public AWPathPollResult OpenReadyCursor(AWPathAgentKey pAgentKey,
            out ReadyPathCursor pCursor)
        {
            lock (_requestGate)
            {
                pCursor = default;
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record))
                    return new AWPathPollResult(AWPathPollKind.NoRequest);
                PathfindingTask task = record.Latest;
                // Opening a cursor is an observational operation.  Keep the
                // terminal stream alive until the consumer explicitly
                // acknowledges it or consumes its final step so a failed
                // request can still reach the central recovery owner.
                AWPathPollResult result = PollCursorOwned(pAgentKey, task);
                if (result.Kind != AWPathPollKind.NoRequest)
                    pCursor = new ReadyPathCursor(this, pAgentKey, task);
                return result;
            }
        }

        /// <summary>
        /// Opens a cursor bound to the exact submission token.  This is the
        /// handle-based equivalent of Cultiway master's token-bound cursor
        /// API; an old caller can only observe NoRequest after replacement.
        /// </summary>
        public AWPathPollResult OpenReadyCursor(AWPathHandle pHandle,
            out ReadyPathCursor pCursor)
        {
            return OpenReadyCursor(pHandle.Agent, pHandle.SubmissionToken,
                out pCursor);
        }

        public bool Consume(long pActorId)
        {
            return Consume(ResolveLegacyKey(pActorId), null);
        }

        public bool Consume(AWPathAgentKey pAgentKey)
        {
            return Consume(pAgentKey, null);
        }

#if !AW3_RULES_TESTS
        public void ConsumeStep(Actor pActor)
        {
            if (pActor?.data == null) return;
            Consume(pActor.data.id);
        }

        public bool Acknowledge(Actor pActor)
        {
            if (pActor?.data == null) return false;
            AWPathAgentKey agentKey = ResolveLegacyKey(pActor.data.id);
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(agentKey,
                        out PathSessionRecord record) ||
                    record.Latest == null) return false;
                CleanupOwned(agentKey, record.Latest);
                return true;
            }
        }
#endif

        private bool Consume(AWPathAgentKey pAgentKey,
            PathfindingTask pExpectedTask)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record)) return false;
                PathfindingTask task = record.Latest;
                if (pExpectedTask != null &&
                    !ReferenceEquals(task, pExpectedTask)) return false;
                if (!task.Request.Stream.TryTake(out _)) return false;
                if (task.Request.Stream.Count <= SegmentLowWatermark)
                {
                    if (ScheduleLocked(record,
                            AWPathWorkPriority.Continuation))
                    {
                        // The session owns one continuation token, so a
                        // burst of consumers cannot enqueue duplicates.
                    }
                }
                CleanupConsumedTerminal(pAgentKey, task);
                return true;
            }
        }

        public void Cancel(long pActorId, AWPathFailureReason pReason)
        {
            Cancel(ResolveLegacyKey(pActorId), pReason);
        }

#if !AW3_RULES_TESTS
        public void Cancel(Actor pActor,
            AWPathFailureReason pReason =
                AWPathFailureReason.CancelledByNewRequest)
        {
            if (pActor?.data == null) return;
            Cancel(pActor.data.id, pReason);
        }
#endif

        public void Cancel(AWPathAgentKey pAgentKey,
            AWPathFailureReason pReason)
        {
#if !AW3_RULES_TESTS
            AWPathfindingProfiler.AWPathfindingProfilerMeasurement measurement =
                AWPathfindingProfiler.Start();
#endif
#if !AW3_RULES_TESTS
            bool removed = false;
#endif
            lock (_requestGate)
            {
                if (_sessions.TryRemove(pAgentKey, out PathSessionRecord record))
                {
#if !AW3_RULES_TESTS
                    removed = true;
#endif
                    CancelRecordLocked(record, pReason);
                }
                _submissionTokens.TryRemove(pAgentKey, out _);
                _recoveryStates.Remove(pAgentKey);
            }
#if !AW3_RULES_TESTS
            measurement.Complete(removed
                ? AWPathfindingBenchmarkMetric.Cancel
                : AWPathfindingBenchmarkMetric.CancelEmpty);
#endif
        }

        public bool CancelOwned(long pActorId, long pSubmissionToken,
            AWPathFailureReason pReason)
        {
            return CancelOwned(ResolveLegacyKey(pActorId), pSubmissionToken,
                pReason);
        }

#if !AW3_RULES_TESTS
        public bool CancelOwned(Actor pActor, long pSubmissionToken,
            AWPathFailureReason pReason =
                AWPathFailureReason.CancelledByNewRequest)
        {
            return pActor?.data != null &&
                   CancelOwned(pActor.data.id, pSubmissionToken, pReason);
        }
#endif

        public bool CancelOwned(AWPathAgentKey pAgentKey,
            long pSubmissionToken, AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                if (!_submissionTokens.TryGetValue(pAgentKey,
                        out long currentToken) ||
                    currentToken != pSubmissionToken)
                    return false;
                _submissionTokens.TryRemove(pAgentKey, out _);
                if (!_sessions.TryRemove(pAgentKey,
                        out PathSessionRecord record))
                    return false;
                CancelRecordLocked(record, pReason);
                _recoveryStates.Remove(pAgentKey);
                return true;
            }
        }

        public bool Cancel(AWPathHandle pHandle,
            AWPathFailureReason pReason = AWPathFailureReason.CancelledByNewRequest)
        {
            return pHandle.IsValid && CancelOwned(pHandle.Agent,
                pHandle.SubmissionToken, pReason);
        }

        public void Clear(AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                foreach (KeyValuePair<AWPathAgentKey, PathSessionRecord> pair in _sessions)
                {
                    if (_sessions.TryRemove(pair.Key, out PathSessionRecord record))
                        CancelRecordLocked(record, pReason);
                }
                _submissionTokens.Clear();
                _lastRequests.Clear();
                _legacyKeys.Clear();
                _openCursors.Clear();
                while (_pendingRecoveries.TryDequeue(
                    out AWPathRecoveryTicket _)) { }
                _scheduledRecoveries.Clear();
                _recoveryStates.Clear();
                Drain(_starvedQueue);
                Drain(_initialQueue);
                Drain(_continuationQueue);
                while (_queueSignal.Wait(0))
                {
                }
                Volatile.Write(ref _queueDepth, 0);
            }
        }

        public void CancelWorld(AWPathWorldKey pWorld,
            AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                foreach (KeyValuePair<AWPathAgentKey, PathSessionRecord> pair in _sessions)
                {
                    if (pair.Key.World != pWorld) continue;
                    if (_sessions.TryRemove(pair.Key,
                            out PathSessionRecord record))
                        CancelRecordLocked(record, pReason);
                    _submissionTokens.TryRemove(pair.Key, out _);
                    _lastRequests.TryRemove(pair.Key, out _);
                    _legacyKeys.TryRemove(pair.Key.AgentId, out _);
                    _openCursors.TryRemove(pair.Key, out _);
                    _recoveryStates.Remove(pair.Key);
                }
            }
        }

        public void StopAndDrain()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            Clear(AWPathFailureReason.WorldCleared);
            for (int i = 0; i < _workers.Length; i++) _queueSignal.Release();
            foreach (Thread worker in _workers)
            {
                if (worker == null || worker == Thread.CurrentThread) continue;
                worker.Join(5000);
            }
            _workers = Array.Empty<Thread>();
        }

        public void Dispose()
        {
            StopAndDrain();
            _queueSignal.Dispose();
        }

        // Raw background thread entry point. Keep restart attempts bounded so
        // a persistent queue or generator fault cannot become a hot loop or
        // escape the thread boundary and terminate the process.
        private void WorkerLoop()
        {
            const int MaximumRestarts = 8;
            for (int restart = 0; ; restart++)
            {
                try
                {
                    RunWorkerLoop();
                    return;
                }
                catch (Exception error)
                {
                    try
                    {
                        AncientWarfare3.ModClass.LogWarning(
                            "[AW3 path worker] unhandled worker fault " +
                            "(restart " + restart + " of " +
                            MaximumRestarts + "): " + error);
                    }
                    catch
                    {
                    }
                    if (Volatile.Read(ref _stopping) != 0) return;
                    if (restart >= MaximumRestarts) return;
                }
            }
        }

        internal bool TryGetLastRequestOptions(long pActorId,
            out AWPathRequestOptions pOptions)
        {
            return TryGetLastRequestOptions(ResolveLegacyKey(pActorId),
                out pOptions);
        }

#if !AW3_RULES_TESTS
        internal bool TryGetLastRequestOptions(Actor pActor,
            out AWPathRequestOptions pOptions)
        {
            return TryGetLastRequestOptions(
                ResolveLegacyKey(pActor?.data?.id ?? 0), out pOptions);
        }
#endif

        private bool TryGetLastRequestOptions(AWPathAgentKey pAgentKey,
            out AWPathRequestOptions pOptions)
        {
            if (_lastRequests.TryGetValue(pAgentKey,
                    out AWPathRequestRecoverySnapshot snapshot))
            {
                pOptions = snapshot.Options;
                return true;
            }
            pOptions = AWPathRequestOptions.Default;
            return false;
        }

        /// <summary>
        /// Rebuilds a request from the latest accepted snapshot. The caller
        /// receives only the new request outcome; ownership remains in the
        /// normal RequestCore path and is never duplicated by the retry owner.
        /// </summary>
#if !AW3_RULES_TESTS
        internal bool TryRequestRecover(Actor pActor,
            WorldTile pOverrideTarget = null)
        {
            AWPathAgentKey agentKey = ResolveLegacyKey(
                pActor?.data?.id ?? 0);
            if (!agentKey.IsValid || pActor?.current_tile?.data == null ||
                pActor.isRekt() || !_lastRequests.TryGetValue(agentKey,
                    out AWPathRequestRecoverySnapshot snapshot))
                return false;

            WorldTile target = pOverrideTarget ?? pActor.tile_target;
            if (target?.data == null && snapshot.TargetTileId >= 0)
            {
                WorldTile[] tiles = World.world?.tiles_list;
                if (tiles != null && snapshot.TargetTileId < tiles.Length)
                    target = tiles[snapshot.TargetTileId];
            }
            if (target?.data == null) return false;

            AWTraversalGeneration generation =
                AWPathfindingBootstrap.Cache?.Pin();
            if (generation == null) return false;
            try
            {
                long worldGeneration = agentKey.World.Generation;
                bool physicalTransport =
                    AWDockTransportService.TryResolveRoute(
                        pActor.current_tile, target,
                        out AWDockRouteCandidate route);
                var request = new AWPathRequest(agentKey,
                    pActor.current_tile.data.tile_id, target.data.tile_id,
                    snapshot.Options,
                    AWPathMovementBridge.CaptureProfile(pActor), generation,
                    AncientWarfare3.core.performance.AWSimulationTime.IsBound
                        ? AncientWarfare3.core.performance.AWSimulationTime.Now
                        : UnityEngine.Time.realtimeSinceStartupAsDouble,
                    snapshot.WorkClass,
                    AWPathfindingBootstrap.Cache.SourceRevision,
                    worldGeneration, pActor.is_inside_boat,
                    physicalTransport);
                if (physicalTransport)
                    request.SetTransportRoute(
                        AWTransportRouteSnapshot.FromRoute(route));
                return RequestCore(request, pAllowReuse: true,
                    out _);
            }
            finally
            {
                generation.Dispose();
            }
        }
#endif

        private void RunWorkerLoop()
        {
            try
            {
                while (Volatile.Read(ref _stopping) == 0)
                {
                    // Consume exactly one producer signal before dequeuing.
                    // This keeps semaphore permits paired with queue entries,
                    // matching Cultiway master and preventing wake debt after
                    // replacements/cancellations.
                    _queueSignal.Wait();
                    if (Volatile.Read(ref _stopping) != 0) break;
                    // A replacement or cancellation can leave a stale
                    // semaphore permit behind. Consume it and return to
                    // the wait loop instead of treating the permit as a
                    // path task.
                    if (!TryTakeWork(out AWScheduledPathWork work))
                    {
                        continue;
                    }
                    _diagnostics?.OnDequeued(work.Priority, work.EnqueuedAt);

                    PathfindingTask task = null;
                    PathSessionRecord record = null;
                    lock (_requestGate)
                    {
                        if (!_sessions.TryGetValue(work.OwnerKey, out record) ||
                            !record.Session.TryBeginWork(work.QueueVersion) ||
                            record.Queued == null)
                        {
                            Interlocked.Increment(ref _staleWorkCount);
                            continue;
                        }
                        task = record.Queued;
                        record.Queued = null;
                        record.Running = task;
                        task.MarkWorkerStarted();
                    }

#if !AW3_RULES_TESTS
                    AWPathfindingProfiler.RecordQueueWait(
                        work.ProfilerSession, work.ProfilerEnqueuedAt);
                    AWPathfindingProfiler.AWPathfindingProfilerMeasurement backgroundMeasurement =
                        AWPathfindingProfiler.Start(record.BenchmarkSession);
#endif
                    IAWPathSegmentGenerator segmentGenerator = _generator as
                        IAWPathSegmentGenerator;
                    bool usedSegmentGenerator = segmentGenerator != null;
                    AWPathGenerationResult segmentResult = default;
                    try
                    {
                        if (!IsTerminal(task.Request.Stream.State))
                        {
                            if (usedSegmentGenerator)
                            {
                                segmentResult = segmentGenerator.GenerateSegment(
                                    task.Request,
                                    task.Request.Cancellation.Token,
                                    SegmentStepBudget);
                                ApplySegmentResult(task.Request, segmentResult);
                            }
                            else
                            {
                                _generator.Generate(task.Request,
                                    task.Request.Cancellation.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        task.Request.Stream.Cancel(
                            AWPathFailureReason.CancelledByNewRequest);
                    }
                    catch (Exception error)
                    {
                        task.Request.Stream.Fail(
                            AWPathFailureReason.GeneratorException, error);
                    }
                    finally
                    {
#if !AW3_RULES_TESTS
                        backgroundMeasurement.Complete(
                            AWPathfindingBenchmarkMetric.BackgroundPath);
#endif
                        if (!usedSegmentGenerator ||
                            !segmentResult.Succeeded ||
                            segmentResult.ReachedTarget)
                            task.Request.Stream.EnsureCompleted();
                        if (task.Request.Stream.State == AWPathRequestState.Succeeded)
                            _diagnostics?.OnCompleted();
                        else if (task.Request.Stream.State == AWPathRequestState.Failed)
                        {
                            _diagnostics?.OnFailed();
                            Exception error = task.Request.Stream.Error;
                            if (error != null)
                                _diagnostics?.Enqueue(new AWPathDiagnosticEvent(
                                    task.Request.ActorId,
                                    task.Request.Stream.FailureReason,
                                    error.GetType().Name + ": " + error.Message));
                        }
                        task.ReleaseWorker();

                        lock (_requestGate)
                        {
                            if (_sessions.TryGetValue(work.OwnerKey,
                                    out PathSessionRecord current) &&
                                ReferenceEquals(current, record) &&
                                ReferenceEquals(record.Running, task))
                            {
                                record.Running = null;
                                bool hasMoreSegments = usedSegmentGenerator &&
                                    segmentResult.Succeeded &&
                                    !segmentResult.ReachedTarget;
                                bool scheduleWhenEmpty = hasMoreSegments &&
                                    task.Request.Stream.Count == 0;
                                bool scheduled = record.Session.CompleteWork(
                                        hasMoreSegments, scheduleWhenEmpty,
                                        out AWScheduledPathWork replacement);
                                if (scheduled)
                                {
                                    if (record.Queued == null)
                                        record.Queued = record.Latest;
                                    EnqueueLocked(replacement);
                                }
                                if (task.Request.Stream.State ==
                                        AWPathRequestState.Failed)
                                {
                                    TryScheduleRecoveryAfterFailure(
                                        work.OwnerKey,
                                        task.Request.Stream.FailureReason);
                                }
                            }
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static void ApplySegmentResult(AWPathRequest pRequest,
            AWPathGenerationResult pResult)
        {
            if (pRequest == null) return;
            if (!pResult.Succeeded)
            {
                if (pResult.FailureReason ==
                    AWPathFailureReason.CancelledByNewRequest)
                    pRequest.Stream.Cancel(pResult.FailureReason);
                else
                    pRequest.Stream.Fail(pResult.FailureReason,
                        pResult.Error);
                return;
            }

            IReadOnlyList<AWPathStep> steps = pResult.Steps;
            AWTraversalState continuation = new AWTraversalState(
                pRequest.Profile.Stamina, pRequest.Profile.Health, 0f);
            for (int index = 0; index < (steps?.Count ?? 0); index++)
            {
                continuation = AWTraversalRules.AdvanceState(
                    continuation, pRequest.Profile, steps[index].Estimate);
                if (!pRequest.Stream.AddStep(steps[index])) return;
            }
            if ((steps?.Count ?? 0) > 0)
            {
                // Generators that calculate traversal state publish it with
                // the segment result. Keep the local fold as a compatibility
                // fallback for custom/test generators that only publish
                // steps.
                if (!float.IsNaN(pResult.EndStamina) &&
                    !float.IsNaN(pResult.EndHealth))
                {
                    continuation = new AWTraversalState(
                        pResult.EndStamina, pResult.EndHealth,
                        continuation.Risk);
                }
                pRequest.AdvanceContinuationState(continuation);
            }

            if (pResult.ReachedTarget)
                pRequest.Stream.Complete();
            else if (pResult.EndTileId >= 0)
                pRequest.AdvanceStartTile(pResult.EndTileId);
            else
                pRequest.Stream.Fail(AWPathFailureReason.Unreachable, null);
        }

        private void ReplaceLocked(PathSessionRecord pRecord,
            AWPathRequest pRequest)
        {
            PathfindingTask replaced = pRecord.Latest;
            pRecord.Session.Replace();
            bool replacedQueued = false;
            if (pRecord.Queued != null)
            {
                replacedQueued = ReferenceEquals(pRecord.Queued, replaced);
                pRecord.Queued.Cancel(AWPathFailureReason.CancelledByNewRequest);
                pRecord.Queued.ReleaseOwner();
                pRecord.Queued.ReleaseWorkerIfNotStarted();
                pRecord.Queued = null;
            }
            if (ReferenceEquals(replaced, pRecord.Running))
            {
                replaced.Cancel(AWPathFailureReason.CancelledByNewRequest);
                replaced.ReleaseOwner();
            }
            else if (!replacedQueued)
            {
                replaced.Cancel(AWPathFailureReason.CancelledByNewRequest);
                replaced.ReleaseOwner();
                replaced.ReleaseWorkerIfNotStarted();
            }

            var latest = new PathfindingTask(pRequest);
            pRecord.Latest = latest;
            pRecord.Queued = latest;
            if (pRecord.Running == null)
                ScheduleLocked(pRecord, PriorityFor(pRequest.WorkClass));
        }

        private bool ScheduleLocked(PathSessionRecord pRecord,
            AWPathWorkPriority pPriority)
        {
            if (!pRecord.Session.TrySchedule(pPriority,
                    out AWScheduledPathWork work)) return false;
            if (pRecord.Queued == null)
                pRecord.Queued = pRecord.Latest;
            EnqueueLocked(work);
            return true;
        }

        private void EnqueueLocked(AWScheduledPathWork pWork)
        {
#if !AW3_RULES_TESTS
            AWPathfindingProfiler.AWPathfindingProfilerSession profilerSession = null;
            if (_sessions.TryGetValue(pWork.OwnerKey,
                    out PathSessionRecord record))
                profilerSession = record.BenchmarkSession;
            AWPathfindingProfiler.AWPathfindingProfilerMeasurement enqueueMeasurement =
                AWPathfindingProfiler.Start(profilerSession);
            pWork = pWork.WithProfiler(profilerSession,
                AWPathfindingProfiler.MarkEnqueued(profilerSession));
#endif
            QueueFor(pWork.Priority).Enqueue(pWork);
            Interlocked.Increment(ref _queueDepth);
            ObserveQueuesLocked();
            _queueSignal.Release();
#if !AW3_RULES_TESTS
            enqueueMeasurement.Complete(AWPathfindingBenchmarkMetric.Enqueue);
#endif
        }

        private void RecordLastRequestLocked(AWPathAgentKey pAgentKey,
            AWPathRequest pRequest)
        {
            if (!pAgentKey.IsValid || pRequest == null) return;
            _lastRequests[pAgentKey] = new AWPathRequestRecoverySnapshot(
                pRequest.TargetTileId, pRequest.Options, pRequest.WorkClass,
                pRequest.PhysicalTransportAvailable);
        }

        internal AWPathRecoveryScheduleResult TryScheduleRecovery(
            long pActorId, AWPathFailureReason pReason, out float pDelay)
        {
            pDelay = 0f;
            if (pActorId <= 0)
                return AWPathRecoveryScheduleResult.NotOwned;
            AWPathAgentKey agentKey = ResolveLegacyKey(pActorId);
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(agentKey,
                        out PathSessionRecord record) ||
                    record.Latest?.Request?.Stream == null)
                    return AWPathRecoveryScheduleResult.NotOwned;
                if (_recoveryStates.TryGetValue(agentKey,
                        out AWPathRecoveryState pending) && pending.Pending)
                    return AWPathRecoveryScheduleResult.AlreadyPending;

                int attempt = _recoveryStates.TryGetValue(agentKey,
                    out AWPathRecoveryState previous) &&
                    previous.Reason == pReason
                    ? previous.Attempt + 1 : 1;
                int limit = AWPathLifecycleRules.RetryLimit(pReason);
                if (limit <= 0 || attempt > limit)
                {
                    _recoveryStates.Remove(agentKey);
                    return AWPathRecoveryScheduleResult.Exhausted;
                }

                pDelay = AWPathLifecycleRules.RetryDelay(attempt);
                _recoveryStates[agentKey] = new AWPathRecoveryState(
                    pReason, attempt, pPending: true);
                _pendingRecoveries.Enqueue(new AWPathRecoveryTicket(
                    agentKey, record.Latest, pReason, attempt, pDelay));
                return AWPathRecoveryScheduleResult.Scheduled;
            }
        }

        // Generator failures follow the same single recovery owner as
        // movement-bridge failures.  Keeping this at the worker completion
        // boundary prevents a failed segment from becoming a permanently
        // terminal route while avoiding a second request owner.
        private void TryScheduleRecoveryAfterFailure(
            AWPathAgentKey pAgentKey, AWPathFailureReason pReason)
        {
            if (!pAgentKey.IsValid || pAgentKey.AgentId <= 0L ||
                pReason == AWPathFailureReason.None) return;
            TryScheduleRecovery(pAgentKey.AgentId, pReason, out _);
        }

        internal bool IsRecoveryPending(long pActorId)
        {
            AWPathAgentKey key = ResolveLegacyKey(pActorId);
            lock (_requestGate)
            {
                return _recoveryStates.TryGetValue(key,
                    out AWPathRecoveryState state) && state.Pending;
            }
        }

        internal void ClearRecovery(long pActorId)
        {
            AWPathAgentKey key = ResolveLegacyKey(pActorId);
            lock (_requestGate) _recoveryStates.Remove(key);
        }

        private void ActivateRecoveryLocked(AWPathScheduledRecovery pRetry)
        {
            if (!_sessions.TryGetValue(pRetry.AgentKey,
                    out PathSessionRecord record) ||
                !ReferenceEquals(record.Latest, pRetry.ExpectedTask) ||
                record.Latest?.Request == null)
            {
                _recoveryStates.Remove(pRetry.AgentKey);
                return;
            }

            AWPathRequest request = _recoveryRequestFactory?.Invoke(
                pRetry.AgentKey, record.Latest.Request);
            if (request == null)
            {
                _recoveryStates.Remove(pRetry.AgentKey);
                return;
            }
            // Recovery replaces the terminal task with a new stream. Any
            // cursor opened against the failed task must stop blocking the
            // next owner from opening the replacement stream.
            _openCursors.TryRemove(pRetry.AgentKey, out _);
            ReplaceLocked(record, request);
            _recoveryStates[pRetry.AgentKey] = new AWPathRecoveryState(
                pRetry.Reason, pRetry.Attempt, pPending: false);
        }

        private void Drain(ConcurrentQueue<AWScheduledPathWork> pQueue)
        {
            while (pQueue.TryDequeue(out _))
            {
                if (Interlocked.Decrement(ref _queueDepth) < 0)
                    Volatile.Write(ref _queueDepth, 0);
            }
        }

        private bool TryTakeWork(out AWScheduledPathWork pWork)
        {
            lock (_requestGate)
            {
                AWPathWorkPriority priority = AWPathQueueFairnessRules.Select(
                    !_starvedQueue.IsEmpty, !_initialQueue.IsEmpty,
                    !_continuationQueue.IsEmpty,
                    Volatile.Read(ref _consecutiveStarvedWork));
                bool dequeued = TryDequeue(priority, out pWork);
                if (!dequeued)
                {
                    // A producer may have changed a queue between the snapshot
                    // and dequeue. Fall back to the remaining queues without
                    // relaxing the bounded-streak counter.
                    dequeued = _starvedQueue.TryDequeue(out pWork) ||
                        _initialQueue.TryDequeue(out pWork) ||
                        _continuationQueue.TryDequeue(out pWork);
                }
                if (!dequeued)
                {
                    pWork = default;
                    return false;
                }

                if (pWork.Priority == AWPathWorkPriority.Starved)
                    Interlocked.Increment(ref _consecutiveStarvedWork);
                else
                    Volatile.Write(ref _consecutiveStarvedWork, 0);
                Interlocked.Decrement(ref _queueDepth);
                return true;
            }
        }

        private bool TryDequeue(AWPathWorkPriority pPriority,
            out AWScheduledPathWork pWork)
        {
            switch (pPriority)
            {
                case AWPathWorkPriority.Starved:
                    return _starvedQueue.TryDequeue(out pWork);
                case AWPathWorkPriority.Initial:
                    return _initialQueue.TryDequeue(out pWork);
                default:
                    return _continuationQueue.TryDequeue(out pWork);
            }
        }

        private ConcurrentQueue<AWScheduledPathWork> QueueFor(
            AWPathWorkPriority pPriority)
        {
            switch (pPriority)
            {
                case AWPathWorkPriority.Starved: return _starvedQueue;
                case AWPathWorkPriority.Continuation: return _continuationQueue;
                default: return _initialQueue;
            }
        }

        private static AWPathWorkPriority PriorityFor(AWPathWorkClass pWorkClass)
        {
            return pWorkClass == AWPathWorkClass.Operational
                ? AWPathWorkPriority.Starved
                : AWPathWorkPriority.Initial;
        }

        private static AWPathSubmissionKind ToSubmissionKind(
            AWPathSubmissionDisposition pDisposition)
        {
            switch (pDisposition)
            {
                case AWPathSubmissionDisposition.Reused:
                    return AWPathSubmissionKind.Reused;
                case AWPathSubmissionDisposition.ReplacedPending:
                case AWPathSubmissionDisposition.ReplacedRunning:
                    return AWPathSubmissionKind.Replaced;
                case AWPathSubmissionDisposition.Submitted:
                    return AWPathSubmissionKind.Created;
                default:
                    return AWPathSubmissionKind.Rejected;
            }
        }

        private void CancelRecordLocked(PathSessionRecord pRecord,
            AWPathFailureReason pReason)
        {
            pRecord.Session.Cancel();
            PathfindingTask latest = pRecord.Latest;
            latest.Cancel(pReason);
            latest.ReleaseOwner();
            if (ReferenceEquals(pRecord.Queued, latest))
                latest.ReleaseWorkerIfNotStarted();
            if (pRecord.Running != null && !ReferenceEquals(pRecord.Running,
                    latest))
                pRecord.Running.Cancel(pReason);
            _diagnostics?.OnCancelled();
            ObserveQueuesLocked();
        }

        private bool CanReuse(AWPathAgentKey pAgentKey, PathfindingTask pTask,
            AWPathReuseKey pReuseKey)
        {
            if (_openCursors.ContainsKey(pAgentKey)) return false;
            if (pTask?.Request == null ||
                !AWPathRequestReuseRules.CanReuse(pTask.Request.ReuseKey,
                    pReuseKey, ageTicks: 0L, maximumAgeTicks: 0L)) return false;
            return !IsTerminal(pTask.Request.Stream.State) ||
                   pTask.Request.Stream.HasPendingSteps;
        }

        private AWPathPollResult PollOwned(AWPathAgentKey pAgentKey,
            PathfindingTask pTask)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    !ReferenceEquals(record.Latest, pTask))
                    return new AWPathPollResult(AWPathPollKind.NoRequest);

                AWPathPollResult result = pTask.Request.Stream.Poll();
                if (IsTerminal(result.Kind) &&
                    pTask.Request.Stream.Count == 0)
                    CleanupOwned(pAgentKey, pTask);
                return result;
            }
        }

        private AWPathPollResult PollCursorOwned(AWPathAgentKey pAgentKey,
            PathfindingTask pTask)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    !ReferenceEquals(record.Latest, pTask))
                    return new AWPathPollResult(AWPathPollKind.NoRequest);

                // This is the Cultiway PollCursor boundary: it only observes
                // the current stream. Cleanup belongs to Consume/Acknowledge,
                // never to a cursor poll.
                return pTask.Request.Stream.Poll();
            }
        }

        public AWPathPollResult OpenReadyCursor(long pActorId,
            long pSubmissionToken, out ReadyPathCursor pCursor)
        {
            return OpenReadyCursor(ResolveLegacyKey(pActorId),
                pSubmissionToken, out pCursor);
        }

        public AWPathPollResult OpenReadyCursor(AWPathAgentKey pAgentKey,
            long pSubmissionToken, out ReadyPathCursor pCursor)
        {
            pCursor = default(ReadyPathCursor);
            lock (_requestGate)
            {
                if (!_submissionTokens.TryGetValue(pAgentKey,
                        out long currentToken) ||
                    currentToken != pSubmissionToken ||
                    !_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record))
                    return new AWPathPollResult(AWPathPollKind.NoRequest);

                PathfindingTask task = record.Latest;
                AWPathPollResult result = PollCursorOwned(pAgentKey, task);
                if (result.Kind != AWPathPollKind.NoRequest)
                {
                    _openCursors[pAgentKey] = 0;
                    pCursor = new ReadyPathCursor(this, pAgentKey, task,
                        pSubmissionToken);
                }
                return result;
            }
        }

        public bool TryGetCurrentSubmissionToken(long pActorId,
            out long pSubmissionToken)
        {
            return TryGetCurrentSubmissionToken(ResolveLegacyKey(pActorId),
                out pSubmissionToken);
        }

        public bool TryGetCurrentSubmissionToken(AWPathAgentKey pAgentKey,
            out long pSubmissionToken)
        {
            return _submissionTokens.TryGetValue(pAgentKey,
                out pSubmissionToken) &&
                   _sessions.ContainsKey(pAgentKey);
        }

#if !AW3_RULES_TESTS
        public bool TryGetCurrentSubmissionToken(Actor pActor,
            out long pSubmissionToken)
        {
            pSubmissionToken = 0L;
            return pActor?.data != null &&
                   TryGetCurrentSubmissionToken(pActor.data.id,
                       out pSubmissionToken);
        }
#endif

        public void Cleanup(long pActorId)
        {
            AWPathAgentKey agentKey = ResolveLegacyKey(pActorId);
            lock (_requestGate)
            {
                if (_sessions.TryRemove(agentKey,
                        out PathSessionRecord record))
                    CancelRecordLocked(record,
                        AWPathFailureReason.ActorDead);
                _lastRequests.TryRemove(agentKey, out _);
                _submissionTokens.TryRemove(agentKey, out _);
                _legacyKeys.TryRemove(pActorId, out _);
                _openCursors.TryRemove(agentKey, out _);
                _recoveryStates.Remove(agentKey);
            }
        }

        private bool TryExecuteCurrentStep<T>(AWPathAgentKey pAgentKey,
            PathfindingTask pExpectedTask, long pSubmissionToken,
            bool pTokenBound, Func<AWPathStep, T> pAction,
            out T pResult)
        {
            pResult = default(T);
            if (pAction == null || pExpectedTask == null) return false;

            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    !ReferenceEquals(record.Latest, pExpectedTask) ||
                    (pTokenBound &&
                     (!_submissionTokens.TryGetValue(pAgentKey,
                            out long currentToken) ||
                      currentToken != pSubmissionToken)))
                    return false;

                AWPathStream stream = pExpectedTask.Request.Stream;
                if (!stream.TryPeek(out AWPathStep step)) return false;

                // Keep the ownership check and the side effect in one critical
                // section. A retarget/cancel cannot make an older caller
                // execute a step from a replaced request.
                pResult = pAction(step);
                return true;
            }
        }

        private bool TryClaimCurrentStep(AWPathAgentKey pAgentKey,
            PathfindingTask pExpectedTask, long pSubmissionToken,
            bool pTokenBound, out AWPathStep pStep)
        {
            pStep = default(AWPathStep);
            if (pExpectedTask == null) return false;

            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    !ReferenceEquals(record.Latest, pExpectedTask) ||
                    (pTokenBound &&
                     (!_submissionTokens.TryGetValue(pAgentKey,
                            out long currentToken) ||
                      currentToken != pSubmissionToken)))
                    return false;

                AWPathStream stream = pExpectedTask.Request.Stream;
                if (!stream.TryTake(out pStep)) return false;
                if (stream.Count <= SegmentLowWatermark)
                    ScheduleLocked(record, AWPathWorkPriority.Continuation);
                CleanupConsumedTerminal(pAgentKey, pExpectedTask);
                return true;
            }
        }

        private bool AcknowledgeCursor(AWPathAgentKey pAgentKey,
            PathfindingTask pExpectedTask, long pSubmissionToken,
            bool pTokenBound)
        {
            lock (_requestGate)
            {
                if (!IsCursorCurrentLocked(pAgentKey, pExpectedTask,
                        pTokenBound, pSubmissionToken)) return false;
                CleanupOwned(pAgentKey, pExpectedTask);
                return true;
            }
        }

        private bool ScheduleRecoveryCursor(AWPathAgentKey pAgentKey,
            PathfindingTask pExpectedTask, long pSubmissionToken,
            bool pTokenBound, Actor pActor, AWPathFailureReason pReason)
        {
            lock (_requestGate)
            {
                if (!IsCursorCurrentLocked(pAgentKey, pExpectedTask,
                        pTokenBound, pSubmissionToken)) return false;
                AWPathRecoveryScheduleResult result =
                    TryScheduleRecovery(pAgentKey.AgentId,
                        pReason, out _);
                return result == AWPathRecoveryScheduleResult.Scheduled ||
                       result == AWPathRecoveryScheduleResult.AlreadyPending;
            }
        }

        private void CleanupConsumedTerminal(AWPathAgentKey pAgentKey,
            PathfindingTask pTask)
        {
            if (pTask.Request.Stream.Count != 0 ||
                !IsTerminal(pTask.Request.Stream.State)) return;
            CleanupOwned(pAgentKey, pTask);
        }

        private void CleanupOwned(AWPathAgentKey pAgentKey,
            PathfindingTask pTask)
        {
            lock (_requestGate)
            {
                if (!_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record) ||
                    !ReferenceEquals(record.Latest, pTask)) return;
                _sessions.TryRemove(pAgentKey, out _);
                _submissionTokens.TryRemove(pAgentKey, out _);
                _legacyKeys.TryRemove(pAgentKey.AgentId, out _);
                _openCursors.TryRemove(pAgentKey, out _);
                record.Session.Cancel();
                pTask.ReleaseOwner();
            }
        }

        private AWPathQueueSnapshot SnapshotQueuesLocked()
        {
            int operationalQueued = 0, essentialQueued = 0, ambientQueued = 0;
            int operationalActive = 0, essentialActive = 0, ambientActive = 0;
            foreach (PathSessionRecord record in _sessions.Values)
            {
                CountWork(record.Latest?.Request?.WorkClass, active: true,
                    ref operationalActive, ref essentialActive, ref ambientActive);
                if (record.Queued != null)
                    CountWork(record.Queued.Request.WorkClass, active: false,
                        ref operationalQueued, ref essentialQueued, ref ambientQueued);
            }
            return new AWPathQueueSnapshot(operationalQueued, essentialQueued,
                ambientQueued, operationalActive, essentialActive, ambientActive);
        }

        private static void CountWork(AWPathWorkClass? pWorkClass, bool active,
            ref int operational, ref int essential, ref int ambient)
        {
            switch (pWorkClass)
            {
                case AWPathWorkClass.Operational: operational++; break;
                case AWPathWorkClass.EssentialTravel: essential++; break;
                default: ambient++; break;
            }
        }

        private void ObserveQueuesLocked()
        {
            _diagnostics?.ObserveQueue(SnapshotQueuesLocked());
        }

        private void Reject(AWPathRequest pRequest,
            AWPathSubmissionDisposition pDisposition)
        {
            Reject(pRequest, pDisposition, AWPathFailureReason.None);
        }

        private void Reject(AWPathRequest pRequest,
            AWPathSubmissionDisposition pDisposition,
            AWPathFailureReason pFailureReason)
        {
            if (pRequest == null) return;
            _diagnostics?.OnSubmission(pRequest.WorkClass, pDisposition);
            if (pFailureReason != AWPathFailureReason.None)
                pRequest.Stream.Fail(pFailureReason, null);
            pRequest.Dispose();
        }

        private static bool IsTerminal(AWPathPollKind pKind)
        {
            return pKind == AWPathPollKind.Completed ||
                   pKind == AWPathPollKind.Failed ||
                   pKind == AWPathPollKind.Cancelled;
        }

        private static bool IsTerminal(AWPathRequestState pState)
        {
            return pState == AWPathRequestState.Succeeded ||
                   pState == AWPathRequestState.Failed ||
                   pState == AWPathRequestState.Cancelled;
        }

        private bool IsCursorCurrent(AWPathAgentKey pAgentKey,
            PathfindingTask pTask, bool pTokenBound, long pSubmissionToken)
        {
            if (pTask == null) return false;
            lock (_requestGate)
            {
                return IsCursorCurrentLocked(pAgentKey, pTask,
                    pTokenBound, pSubmissionToken);
            }
        }

        private bool IsCursorCurrentLocked(AWPathAgentKey pAgentKey,
            PathfindingTask pTask, bool pTokenBound, long pSubmissionToken)
        {
            if (pTask == null || !_sessions.TryGetValue(pAgentKey,
                    out PathSessionRecord record) ||
                !ReferenceEquals(record.Latest, pTask)) return false;
            if (!pTokenBound) return true;
            return _submissionTokens.TryGetValue(pAgentKey,
                       out long currentToken) &&
                   currentToken == pSubmissionToken;
        }

        private AWPathPollResult PollRetainedCursor(AWPathAgentKey pAgentKey,
            PathfindingTask pTask)
        {
            lock (_requestGate)
            {
                if (_sessions.TryGetValue(pAgentKey,
                        out PathSessionRecord record))
                {
                    if (!ReferenceEquals(record.Latest, pTask))
                        return new AWPathPollResult(AWPathPollKind.NoRequest);
                    return pTask.Request.Stream.Poll();
                }

                // A cursor may observe the terminal state once after its
                // final step was consumed. A replaced session must never be
                // treated as terminal because a newer request owns the key.
                AWPathPollResult result = pTask?.Request?.Stream == null
                    ? new AWPathPollResult(AWPathPollKind.NoRequest)
                    : pTask.Request.Stream.Poll();
                return IsTerminal(result.Kind) &&
                       pTask.Request.Stream.Count == 0
                    ? result
                    : new AWPathPollResult(AWPathPollKind.NoRequest);
            }
        }

        public readonly struct ReadyPathCursor
        {
            private readonly AWPathFinder _owner;
            private readonly AWPathAgentKey _agentKey;
            private readonly PathfindingTask _task;
            private readonly long _submissionToken;
            private readonly bool _tokenBound;

            internal ReadyPathCursor(AWPathFinder pOwner, AWPathAgentKey pAgentKey,
                PathfindingTask pTask, long pSubmissionToken = 0L)
            {
                _owner = pOwner;
                _agentKey = pAgentKey;
                _task = pTask;
                _submissionToken = pSubmissionToken;
                _tokenBound = pSubmissionToken > 0L;
            }

            public bool IsValid => _owner != null &&
                _owner.IsCursorCurrent(_agentKey, _task, _tokenBound,
                    _submissionToken);
            public AWPathPollResult Poll() => _owner == null || _task == null
                ? new AWPathPollResult(AWPathPollKind.NoRequest)
                : IsValid
                    ? _owner.PollCursorOwned(_agentKey, _task)
                    : _owner.PollRetainedCursor(_agentKey, _task);

            public void Consume()
            {
                if (!IsValid) return;
                _owner.Consume(_agentKey, _task);
            }

            public bool Acknowledge()
            {
                return _owner != null && _owner.AcknowledgeCursor(
                    _agentKey, _task, _submissionToken, _tokenBound);
            }

            public bool ScheduleRecovery(Actor pActor,
                AWPathFailureReason pReason)
            {
                return _owner != null && _owner.ScheduleRecoveryCursor(
                    _agentKey, _task, _submissionToken, _tokenBound,
                    pActor, pReason);
            }

            public bool TryExecuteCurrentStep<T>(Func<AWPathStep, T> pAction,
                out T pResult)
            {
                if (!IsValid)
                {
                    pResult = default(T);
                    return false;
                }

                return _owner.TryExecuteCurrentStep(
                    _agentKey, _task, _submissionToken, _tokenBound,
                    pAction, out pResult);
            }

            public bool TryClaimCurrentStep(out AWPathStep pStep)
            {
                if (!IsValid)
                {
                    pStep = default(AWPathStep);
                    return false;
                }

                return _owner.TryClaimCurrentStep(
                    _agentKey, _task, _submissionToken, _tokenBound,
                    out pStep);
            }
        }

        public readonly struct ReadyPathStep
        {
            private readonly ReadyPathCursor _cursor;

            internal ReadyPathStep(ReadyPathCursor pCursor,
                AWPathStep pStep)
            {
                _cursor = pCursor;
                Step = pStep;
            }

            public AWPathStep Step { get; }
            public bool IsValid => _cursor.IsValid;
            public void Consume()
            {
                if (IsValid) _cursor.Consume();
            }
        }

        private void RecordSubmissionLocked(AWPathAgentKey pAgentKey)
        {
            _submissionTokens[pAgentKey] =
                Interlocked.Increment(ref _nextSubmissionToken);
        }

        private AWPathAgentKey ResolveLegacyKey(long pActorId)
        {
            if (_legacyKeys.TryGetValue(pActorId, out AWPathAgentKey key))
                return key;
            long worldGeneration;
#if AW3_RULES_TESTS
            worldGeneration = 0L;
#else
            worldGeneration =
                AncientWarfare3.core.asyncwork.AWAsyncRuntime.WorldGeneration;
#endif
            return new AWPathAgentKey(
                AWPathWorldKey.MainWorld(worldGeneration), pActorId);
        }

        private readonly struct AWPathRecoveryState
        {
            internal AWPathRecoveryState(AWPathFailureReason pReason,
                int pAttempt, bool pPending)
            {
                Reason = pReason;
                Attempt = pAttempt;
                Pending = pPending;
            }

            internal AWPathFailureReason Reason { get; }
            internal int Attempt { get; }
            internal bool Pending { get; }
        }

        private readonly struct AWPathRecoveryTicket
        {
            internal AWPathRecoveryTicket(AWPathAgentKey pAgentKey,
                PathfindingTask pExpectedTask, AWPathFailureReason pReason,
                int pAttempt, float pDelaySeconds)
            {
                AgentKey = pAgentKey;
                ExpectedTask = pExpectedTask;
                Reason = pReason;
                Attempt = pAttempt;
                DelaySeconds = pDelaySeconds;
            }

            internal AWPathAgentKey AgentKey { get; }
            internal PathfindingTask ExpectedTask { get; }
            internal AWPathFailureReason Reason { get; }
            internal int Attempt { get; }
            internal float DelaySeconds { get; }
        }

        private readonly struct AWPathScheduledRecovery
        {
            internal AWPathScheduledRecovery(AWPathAgentKey pAgentKey,
                PathfindingTask pExpectedTask, AWPathFailureReason pReason,
                int pAttempt, double pDueTime)
            {
                AgentKey = pAgentKey;
                ExpectedTask = pExpectedTask;
                Reason = pReason;
                Attempt = pAttempt;
                DueTime = pDueTime;
            }

            internal AWPathAgentKey AgentKey { get; }
            internal PathfindingTask ExpectedTask { get; }
            internal AWPathFailureReason Reason { get; }
            internal int Attempt { get; }
            internal double DueTime { get; }
        }

        private readonly struct AWPathRequestRecoverySnapshot
        {
            internal AWPathRequestRecoverySnapshot(int pTargetTileId,
                AWPathRequestOptions pOptions, AWPathWorkClass pWorkClass,
                bool pPhysicalTransportAvailable)
            {
                TargetTileId = pTargetTileId;
                Options = pOptions;
                WorkClass = pWorkClass;
                PhysicalTransportAvailable = pPhysicalTransportAvailable;
            }

            internal int TargetTileId { get; }
            internal AWPathRequestOptions Options { get; }
            internal AWPathWorkClass WorkClass { get; }
            internal bool PhysicalTransportAvailable { get; }
        }

        internal sealed class PathfindingTask
        {
            private int _references = 2;
            private int _workerStarted;
            private int _workerReleased;

            internal PathfindingTask(AWPathRequest pRequest) { Request = pRequest; }
            internal AWPathRequest Request { get; }
            internal bool WorkerStarted => Volatile.Read(ref _workerStarted) != 0;
            internal void MarkWorkerStarted() => Volatile.Write(ref _workerStarted, 1);
            internal void ReleaseOwner() => Release();
            internal void ReleaseWorker()
            {
                if (Interlocked.Exchange(ref _workerReleased, 1) == 0) Release();
            }
            internal void ReleaseWorkerIfNotStarted()
            {
                if (!WorkerStarted) ReleaseWorker();
            }
            internal void Cancel(AWPathFailureReason pReason)
            {
                try { Request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
                Request.Stream.Cancel(pReason);
            }
            private void Release()
            {
                if (Interlocked.Decrement(ref _references) == 0) Request.Dispose();
            }
        }

        private sealed class PathSessionRecord
        {
            internal PathSessionRecord(AWPathAgentKey pAgentKey,
            PathfindingTask pLatest, bool pHasMoreSegments = true
                )
            {
                Session = new AWPathSession(pAgentKey, pHasMoreSegments);
                Latest = pLatest;
            }

            internal readonly AWPathSession Session;
            internal PathfindingTask Latest;
            internal PathfindingTask Queued;
            internal PathfindingTask Running;
#if !AW3_RULES_TESTS
            internal AWPathfindingProfiler.AWPathfindingProfilerSession BenchmarkSession { get; set; }
#endif
        }
    }
}
