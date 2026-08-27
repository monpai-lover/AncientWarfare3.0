using System;
using AncientWarfare3.core.asyncwork;
using System.Collections.Generic;
using AncientWarfare3.core.performance;
#if !AW3_RULES_TESTS
using ai;
#endif

namespace AncientWarfare3.core.pathfinding
{
    public enum ArmyRoutePollKind
    {
        NoRequest,
        Waiting,
        StepReady,
        Completed,
        Failed,
        Cancelled
    }

    public enum ArmyRouteCancelReason
    {
        TargetReplaced,
        MissionCancelled,
        ProviderChanged,
        WorldCleared,
        ArmyDisposed,
        InvalidRequest
    }

    public readonly struct ArmyRouteRequest : IEquatable<ArmyRouteRequest>
    {
        public ArmyRouteRequest(long armyId, int startTileId,
            int targetTileId)
        {
            ArmyId = armyId;
            StartTileId = startTileId;
            TargetTileId = targetTileId;
        }

        public long ArmyId { get; }
        public int StartTileId { get; }
        public int TargetTileId { get; }
        public bool IsValid => ArmyId >= 0L && StartTileId >= 0 &&
                               TargetTileId >= 0;

        public bool Equals(ArmyRouteRequest pOther)
        {
            return ArmyId == pOther.ArmyId &&
                   StartTileId == pOther.StartTileId &&
                   TargetTileId == pOther.TargetTileId;
        }

        public override bool Equals(object pObject)
        {
            return pObject is ArmyRouteRequest other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ArmyId.GetHashCode();
                hash = hash * 397 ^ StartTileId;
                hash = hash * 397 ^ TargetTileId;
                return hash;
            }
        }
    }

    public readonly struct ArmyRouteHandle
    {
        public ArmyRouteHandle(long armyId, long requestId,
            bool accepted, bool reused, string failureReason = "")
        {
            ArmyId = armyId;
            RequestId = requestId;
            Accepted = accepted;
            Reused = reused;
            FailureReason = failureReason ?? string.Empty;
        }

        public long ArmyId { get; }
        public long RequestId { get; }
        public bool Accepted { get; }
        public bool Reused { get; }
        public string FailureReason { get; }

        public static ArmyRouteHandle Rejected(long pArmyId,
            string pFailureReason = "rejected")
        {
            return new ArmyRouteHandle(pArmyId, -1L, accepted: false,
                reused: false, failureReason: pFailureReason);
        }

        public ArmyRouteHandle AsReused()
        {
            return new ArmyRouteHandle(ArmyId, RequestId, Accepted,
                reused: true, failureReason: FailureReason);
        }
    }

    public readonly struct ArmyRoutePoll
    {
        public ArmyRoutePoll(ArmyRoutePollKind kind, int tileId = -1,
            string failureReason = "",
            AWMovementMethod movementMethod = AWMovementMethod.Walk,
            AWTraversalEstimate estimate = default)
        {
            Kind = kind;
            TileId = tileId;
            FailureReason = failureReason ?? string.Empty;
            MovementMethod = movementMethod;
            Estimate = estimate;
        }

        public ArmyRoutePollKind Kind { get; }
        public int TileId { get; }
        public string FailureReason { get; }
        public AWMovementMethod MovementMethod { get; }
        public AWTraversalEstimate Estimate { get; }
        public bool IsTerminal => Kind == ArmyRoutePollKind.Completed ||
                                  Kind == ArmyRoutePollKind.Failed ||
                                  Kind == ArmyRoutePollKind.Cancelled;
    }

    public interface IArmyRouteProvider : IDisposable
    {
        public ArmyRouteHandle Submit(ArmyRouteRequest request);
        public ArmyRoutePoll Poll(long armyId);
        public void Cancel(long armyId, ArmyRouteCancelReason reason);
        public void ClearWorld();
    }

    public enum ArmyRouteProviderBackend
    {
        Vanilla,
        VanillaFallback,
        Aw3Dedicated,
        Aw3Shared
    }

    public static class ArmyRouteProviderRules
    {
        public static bool ShouldRun(bool worldReady,
            bool externalActorPathOwner)
        {
            _ = externalActorPathOwner;
            return worldReady;
        }

        public static bool CanSubmitRoute(bool aw3Mode,
            bool traversalGenerationReady, int dedicatedWorkerCount,
            bool sharedFinderReady)
        {
            _ = SelectBackend(aw3Mode, traversalGenerationReady,
                dedicatedWorkerCount, sharedFinderReady);
            return true;
        }

        public static ArmyRouteProviderBackend SelectBackend(bool aw3Mode,
            bool traversalGenerationReady, int dedicatedWorkerCount,
            bool sharedFinderReady)
        {
            _ = dedicatedWorkerCount;
            if (!aw3Mode) return ArmyRouteProviderBackend.Vanilla;
            if (!traversalGenerationReady)
                return ArmyRouteProviderBackend.VanillaFallback;
            return sharedFinderReady
                ? ArmyRouteProviderBackend.Aw3Shared
                : ArmyRouteProviderBackend.VanillaFallback;
        }

        public static int ActorWorkerCount(int totalPathWorkerBudget)
        {
            return Math.Max(1, totalPathWorkerBudget - 1);
        }

        public static bool TryResolveTerminalAfterConsumedStep(
            bool consumed, ArmyRoutePoll observed,
            out ArmyRoutePoll terminal)
        {
            if (!consumed)
            {
                terminal = new ArmyRoutePoll(ArmyRoutePollKind.Failed,
                    failureReason: "step_consume_failed");
                return true;
            }
            switch (observed.Kind)
            {
                case ArmyRoutePollKind.NoRequest:
                    terminal = new ArmyRoutePoll(
                        ArmyRoutePollKind.Completed);
                    return true;
                case ArmyRoutePollKind.Completed:
                case ArmyRoutePollKind.Failed:
                case ArmyRoutePollKind.Cancelled:
                    terminal = observed;
                    return true;
                default:
                    terminal = default;
                    return false;
            }
        }
    }

    public sealed class ArmyRouteProviderHost : IDisposable
    {
        private sealed class ActiveRequest
        {
            internal ArmyRouteRequest Request;
            internal ArmyRouteHandle Handle;
        }

        private readonly Dictionary<long, ActiveRequest> _active =
            new Dictionary<long, ActiveRequest>();
        private IArmyRouteProvider _provider;
        private bool _disposed;

        public ArmyRouteProviderHost(IArmyRouteProvider pProvider)
        {
            _provider = pProvider ?? throw new ArgumentNullException(
                nameof(pProvider));
        }

        public int ActiveCount => _active.Count;
        public IArmyRouteProvider CurrentProvider => _provider;

        public ArmyRouteHandle Submit(ArmyRouteRequest pRequest)
        {
            if (_disposed || _provider == null || !pRequest.IsValid)
            {
                ArmyRtsBenchmark.RecordRoute(ArmyRtsRouteLifecycle.Failed);
                return ArmyRouteHandle.Rejected(pRequest.ArmyId,
                    _disposed ? "host_disposed" : "host_provider_unavailable");
            }
            if (_active.TryGetValue(pRequest.ArmyId,
                    out ActiveRequest current))
            {
                if (current.Request.Equals(pRequest))
                {
                    ArmyRtsBenchmark.RecordRoute(
                        ArmyRtsRouteLifecycle.Reused);
                    return current.Handle.AsReused();
                }
                _provider.Cancel(pRequest.ArmyId,
                    ArmyRouteCancelReason.TargetReplaced);
                _active.Remove(pRequest.ArmyId);
                ArmyRtsBenchmark.RecordRoute(
                    ArmyRtsRouteLifecycle.Cancelled);
            }

            ArmyRouteHandle handle = _provider.Submit(pRequest);
            if (handle.Accepted)
            {
                _active[pRequest.ArmyId] = new ActiveRequest
                {
                    Request = pRequest,
                    Handle = handle
                };
                ArmyRtsBenchmark.RecordRoute(
                    ArmyRtsRouteLifecycle.Submitted);
            }
            else
                ArmyRtsBenchmark.RecordRoute(ArmyRtsRouteLifecycle.Failed);
            return handle;
        }

        public ArmyRoutePoll Poll(long pArmyId)
        {
            if (_disposed || _provider == null ||
                !_active.ContainsKey(pArmyId))
                return new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
            ArmyRoutePoll poll = _provider.Poll(pArmyId);
            if (poll.IsTerminal || poll.Kind == ArmyRoutePollKind.NoRequest)
            {
                _active.Remove(pArmyId);
                ArmyRtsBenchmark.RecordRoute(poll.Kind switch
                {
                    ArmyRoutePollKind.Completed =>
                        ArmyRtsRouteLifecycle.Completed,
                    ArmyRoutePollKind.Cancelled =>
                        ArmyRtsRouteLifecycle.Cancelled,
                    _ => ArmyRtsRouteLifecycle.Failed
                });
            }
            return poll;
        }

        public void Cancel(long pArmyId, ArmyRouteCancelReason pReason)
        {
            if (_disposed || _provider == null) return;
            bool active = _active.ContainsKey(pArmyId);
            _provider.Cancel(pArmyId, pReason);
            _active.Remove(pArmyId);
            if (active)
                ArmyRtsBenchmark.RecordRoute(
                    ArmyRtsRouteLifecycle.Cancelled);
        }

        public void Install(IArmyRouteProvider pProvider)
        {
            if (_disposed) throw new ObjectDisposedException(
                nameof(ArmyRouteProviderHost));
            if (pProvider == null) throw new ArgumentNullException(
                nameof(pProvider));
            if (ReferenceEquals(_provider, pProvider)) return;
            IArmyRouteProvider previous = _provider;
            RecordCancelledRoutes(_active.Count);
            previous?.ClearWorld();
            previous?.Dispose();
            _active.Clear();
            _provider = pProvider;
        }

        public void ClearWorld()
        {
            if (_disposed) return;
            RecordCancelledRoutes(_active.Count);
            _provider?.ClearWorld();
            _active.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RecordCancelledRoutes(_active.Count);
            _provider?.ClearWorld();
            _provider?.Dispose();
            _provider = null;
            _active.Clear();
        }

        private static void RecordCancelledRoutes(int pCount)
        {
            for (var index = 0; index < pCount; index++)
                ArmyRtsBenchmark.RecordRoute(
                    ArmyRtsRouteLifecycle.Cancelled);
        }
    }

#if !AW3_RULES_TESTS
    internal sealed class Aw3ArmyRouteProvider : IArmyRouteProvider
    {
        private readonly AWPathFinder _finder;
        private readonly bool _ownsFinder;
        private readonly Dictionary<long, ArmyRouteHandle> _handles =
            new Dictionary<long, ArmyRouteHandle>();
        private readonly Dictionary<long, ArmyRoutePoll> _terminalAfterStep =
            new Dictionary<long, ArmyRoutePoll>();
        // A final stream step can be consumed at the same boundary that the
        // finder retires its session. Remember that ownership transition so
        // the next provider poll reports completion instead of a spurious
        // NoRequest/failure.
        private readonly HashSet<long> _consumedStepPending =
            new HashSet<long>();
        private long _nextRequestId;
        private bool _disposed;

        internal Aw3ArmyRouteProvider(int pWorkerCount)
        {
            if (pWorkerCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(pWorkerCount));
            int worldTileCount = World.world?.tiles_list?.Length ?? 0;
            _finder = new AWPathFinder(new AWStreamingPathGenerator(
                    AWPathfindingConfig.CreateArmyRouteConfig(
                        worldTileCount)),
                AWPathfindingBootstrap.PathDiagnostics);
            _ownsFinder = true;
            _finder.Start(pWorkerCount);
        }

        internal Aw3ArmyRouteProvider(AWPathFinder pSharedFinder)
        {
            _finder = pSharedFinder ?? throw new ArgumentNullException(
                nameof(pSharedFinder));
            _ownsFinder = false;
        }

        // The shared actor finder is ticked by AWPathfindingBootstrap at the
        // simulation completion boundary.  A dedicated army finder has no
        // bootstrap owner, so it needs the same Cultiway lifecycle tick here.
        // Never tick the shared finder from this provider or recovery would be
        // consumed twice by the RTS and actor paths.
        internal void TickOwnedFinder()
        {
            if (_ownsFinder) _finder.Tick();
        }

        internal bool OwnsFinder => _ownsFinder;

        public ArmyRouteHandle Submit(ArmyRouteRequest request)
        {
            if (_disposed || !request.IsValid)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    _disposed ? "provider_disposed" : "invalid_request");
            _terminalAfterStep.Remove(request.ArmyId);
            _consumedStepPending.Remove(request.ArmyId);
            Army army = FindArmy(request.ArmyId);
            Actor captain = SafeCaptain(army);
            if (captain?.data == null || captain.current_tile?.data == null)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    "captain_unavailable");
            WorldTile target = FindTile(request.TargetTileId);
            if (target?.data == null)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    "target_unavailable");
            AWTraversalGeneration generation =
                AWPathfindingBootstrap.Cache.Pin();
            if (generation == null)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    "traversal_unavailable");
            try
            {
                var options = AWPathRequestOptions.Default.
                    WithBoundedMilitaryWater(
                        AWNarrowWaterRecoveryRules.
                            MaximumConsecutiveWaterTiles);
                long finderRequestId = FinderRequestId(request.ArmyId);
                AWPathAgentKey agentKey = new AWPathAgentKey(
                    AWPathWorldKey.MainWorld(AWAsyncRuntime.WorldGeneration),
                    finderRequestId);
                var pathRequest = new AWPathRequest(agentKey,
                    captain.current_tile.data.tile_id,
                    target.data.tile_id, options,
                    AWPathMovementBridge.CaptureProfile(captain),
                    generation,
                    UnityEngine.Time.realtimeSinceStartupAsDouble,
                    AWPathWorkClass.Operational,
                    AWPathfindingBootstrap.Cache.SourceRevision,
                    AWAsyncRuntime.WorldGeneration,
                    captain.is_inside_boat,
                    AWDockTransportService.TryResolveRoute(
                        captain.current_tile, target, out _));
                bool accepted = _finder.Request(pathRequest,
                    out AWPathSubmissionDisposition disposition);
                bool reused = disposition == AWPathSubmissionDisposition.Reused;
                if (reused)
                    AWPathfindingBootstrap.PathDiagnostics.OnRtsSharedRouteReuse();
                if (!accepted)
                    return ArmyRouteHandle.Rejected(request.ArmyId,
                        "finder_rejected");
                if (reused && _handles.TryGetValue(request.ArmyId,
                        out ArmyRouteHandle existing))
                    return existing.AsReused();
                long requestId = _nextRequestId == long.MaxValue
                    ? long.MaxValue
                    : ++_nextRequestId;
                var handle = new ArmyRouteHandle(request.ArmyId,
                    requestId, accepted: true, reused: false);
                _handles[request.ArmyId] = handle;
                return handle;
            }
            finally
            {
                generation.Dispose();
            }
        }

        public ArmyRoutePoll Poll(long armyId)
        {
            if (_disposed)
                return new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
            if (_terminalAfterStep.TryGetValue(armyId,
                    out ArmyRoutePoll pendingTerminal))
            {
                _terminalAfterStep.Remove(armyId);
                _handles.Remove(armyId);
                return pendingTerminal;
            }
            long finderRequestId = FinderRequestId(armyId);
            AWPathPollResult poll = _finder.Poll(finderRequestId);
            switch (poll.Kind)
            {
                case AWPathPollKind.Waiting:
                    return new ArmyRoutePoll(ArmyRoutePollKind.Waiting);
                case AWPathPollKind.StepReady:
                    bool consumed = _finder.Consume(finderRequestId);
                    if (!consumed)
                    {
                        _handles.Remove(armyId);
                        _consumedStepPending.Remove(armyId);
                        return new ArmyRoutePoll(ArmyRoutePollKind.Failed,
                            failureReason: "step_consume_failed");
                    }
                    // Consume is the sole ownership boundary for a ready
                    // step. Do not poll again here: the second observation
                    // can advance/retire a shared Cultiway stream before the
                    // RTS controller has installed the step.
                    _consumedStepPending.Remove(armyId);
                    _consumedStepPending.Add(armyId);
                    return new ArmyRoutePoll(ArmyRoutePollKind.StepReady,
                        poll.Step.TileId,
                        movementMethod: poll.Step.Method,
                        estimate: poll.Step.Estimate);
                case AWPathPollKind.Completed:
                    _handles.Remove(armyId);
                    _consumedStepPending.Remove(armyId);
                    return new ArmyRoutePoll(ArmyRoutePollKind.Completed);
                case AWPathPollKind.Failed:
                    _handles.Remove(armyId);
                    _consumedStepPending.Remove(armyId);
                    return new ArmyRoutePoll(ArmyRoutePollKind.Failed,
                        failureReason: poll.FailureReason.ToString());
                case AWPathPollKind.Cancelled:
                    _handles.Remove(armyId);
                    _consumedStepPending.Remove(armyId);
                    return new ArmyRoutePoll(ArmyRoutePollKind.Cancelled,
                        failureReason: poll.FailureReason.ToString());
                default:
                    _handles.Remove(armyId);
                    if (_consumedStepPending.Remove(armyId))
                        return new ArmyRoutePoll(ArmyRoutePollKind.Completed);
                    return new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
            }
        }

        public void Cancel(long armyId, ArmyRouteCancelReason reason)
        {
            if (_disposed) return;
            _finder.Cancel(FinderRequestId(armyId),
                ToPathFailure(reason));
            _handles.Remove(armyId);
            _terminalAfterStep.Remove(armyId);
            _consumedStepPending.Remove(armyId);
        }

        public void ClearWorld()
        {
            if (_disposed) return;
            if (_ownsFinder)
                _finder.Clear(AWPathFailureReason.WorldCleared);
            else
                CancelSharedRequests(AWPathFailureReason.WorldCleared);
            _handles.Clear();
            _terminalAfterStep.Clear();
            _consumedStepPending.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsFinder)
            {
                _finder.StopAndDrain();
                _finder.Dispose();
            }
            else
            {
                CancelSharedRequests(
                    AWPathFailureReason.CancelledByNewRequest);
            }
            _handles.Clear();
            _terminalAfterStep.Clear();
            _consumedStepPending.Clear();
        }

        private static ArmyRoutePoll ToArmyRoutePoll(
            AWPathPollResult pPoll)
        {
            return pPoll.Kind switch
            {
                AWPathPollKind.Waiting => new ArmyRoutePoll(
                    ArmyRoutePollKind.Waiting),
                AWPathPollKind.StepReady => new ArmyRoutePoll(
                    ArmyRoutePollKind.StepReady, pPoll.Step.TileId,
                    movementMethod: pPoll.Step.Method,
                    estimate: pPoll.Step.Estimate),
                AWPathPollKind.Completed => new ArmyRoutePoll(
                    ArmyRoutePollKind.Completed),
                AWPathPollKind.Failed => new ArmyRoutePoll(
                    ArmyRoutePollKind.Failed,
                    failureReason: pPoll.FailureReason.ToString()),
                AWPathPollKind.Cancelled => new ArmyRoutePoll(
                    ArmyRoutePollKind.Cancelled,
                    failureReason: pPoll.FailureReason.ToString()),
                _ => new ArmyRoutePoll(ArmyRoutePollKind.NoRequest)
            };
        }

        private void CancelSharedRequests(AWPathFailureReason pReason)
        {
            var armyIds = new List<long>(_handles.Keys);
            for (int i = 0; i < armyIds.Count; i++)
                _finder.Cancel(FinderRequestId(armyIds[i]), pReason);
        }

        private static long FinderRequestId(long pArmyId)
        {
            if (pArmyId < 0L) return long.MinValue;
            return pArmyId == long.MaxValue
                ? long.MinValue + 1L
                : -pArmyId - 1L;
        }

        private static AWPathFailureReason ToPathFailure(
            ArmyRouteCancelReason pReason)
        {
            return pReason == ArmyRouteCancelReason.WorldCleared
                ? AWPathFailureReason.WorldCleared
                : AWPathFailureReason.CancelledByNewRequest;
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static WorldTile FindTile(int pTileId)
        {
            try
            {
                WorldTile[] tiles = World.world?.tiles_list;
                return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                    ? tiles[pTileId]
                    : null;
            }
            catch { return null; }
        }
    }

    internal sealed class VanillaArmyRouteProvider : IArmyRouteProvider
    {
        private const int MaximumCopiedSteps = 4096;

        private sealed class CopiedRoute
        {
            internal long RequestId;
            internal readonly List<int> TileIds = new List<int>();
            internal int Cursor;
            internal bool Failed;
        }

        private readonly Dictionary<long, CopiedRoute> _routes =
            new Dictionary<long, CopiedRoute>();
        private long _nextRequestId;
        private bool _disposed;

        public ArmyRouteHandle Submit(ArmyRouteRequest request)
        {
            if (_disposed || !request.IsValid)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    _disposed ? "provider_disposed" : "invalid_request");
            Army army = FindArmy(request.ArmyId);
            Actor captain = SafeCaptain(army);
            WorldTile target = FindTile(request.TargetTileId);
            if (captain?.data == null || captain.current_tile?.data == null ||
                target?.data == null)
                return ArmyRouteHandle.Rejected(request.ArmyId,
                    "captain_or_target_unavailable");

            bool accepted;
            try
            {
                accepted = captain.goTo(target,
                    pLimitPathfindingRegions: 0) != ExecuteEvent.False;
            }
            catch { accepted = false; }
            if (!accepted) return ArmyRouteHandle.Rejected(request.ArmyId,
                "vanilla_go_to_rejected");

            var copied = new CopiedRoute
            {
                RequestId = _nextRequestId == long.MaxValue
                    ? long.MaxValue
                    : ++_nextRequestId
            };
            try
            {
                int start = Math.Max(0, captain.current_path_index);
                int end = Math.Min(captain.current_path.Count,
                    start + MaximumCopiedSteps);
                for (int i = start; i < end; i++)
                {
                    WorldTile tile = captain.current_path[i];
                    if (tile?.data != null)
                        copied.TileIds.Add(tile.data.tile_id);
                }
                copied.Failed = copied.TileIds.Count == 0 &&
                                captain.current_tile != target;
            }
            catch { copied.Failed = true; }
            _routes[request.ArmyId] = copied;
            return new ArmyRouteHandle(request.ArmyId, copied.RequestId,
                accepted: true, reused: false);
        }

        public ArmyRoutePoll Poll(long armyId)
        {
            if (_disposed || !_routes.TryGetValue(armyId,
                    out CopiedRoute route))
                return new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
            if (route.Failed)
            {
                _routes.Remove(armyId);
                return new ArmyRoutePoll(ArmyRoutePollKind.Failed,
                    failureReason: "vanilla_path_unavailable");
            }
            if (route.Cursor < route.TileIds.Count)
                return new ArmyRoutePoll(ArmyRoutePollKind.StepReady,
                    route.TileIds[route.Cursor++]);
            _routes.Remove(armyId);
            return new ArmyRoutePoll(ArmyRoutePollKind.Completed);
        }

        public void Cancel(long armyId, ArmyRouteCancelReason reason)
        {
            _ = reason;
            _routes.Remove(armyId);
        }

        public void ClearWorld()
        {
            _routes.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            _routes.Clear();
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try { return pArmy?.getCaptain(); }
            catch { return null; }
        }

        private static WorldTile FindTile(int pTileId)
        {
            try
            {
                WorldTile[] tiles = World.world?.tiles_list;
                return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                    ? tiles[pTileId]
                    : null;
            }
            catch { return null; }
        }
    }

    internal static class ArmyRouteProviderService
    {
        private static ArmyRouteProviderHost _host;

        public static int ActiveCount => _host?.ActiveCount ?? 0;
        public static bool CanSubmit => ArmyRouteProviderRules.
            CanSubmitRoute(AWPathfindingRuntimeMode.IsAw3,
                AWPathfindingBootstrap.Cache.GenerationId >= 0,
                AWPerformanceSettings.ArmyRouteWorkerCount,
                AWPathfindingBootstrap.Finder != null);

        public static void ProcessFrame()
        {
            bool worldReady = World.world?.tiles_list != null &&
                              World.world.tiles_list.Length > 0;
            bool externalActorPathOwner =
                !PathfindingOwnershipService.IsAw3Owner;
            if (!ArmyRouteProviderRules.ShouldRun(worldReady,
                    externalActorPathOwner)) return;
            if (!CanSubmit) return;
            if (_host == null)
            {
                EnsureProvider();
            }
            Aw3ArmyRouteProvider currentAw3 = _host?.CurrentProvider as
                Aw3ArmyRouteProvider;
            currentAw3?.TickOwnedFinder();
        }

        public static ArmyRouteHandle Submit(ArmyRouteRequest pRequest)
        {
            if (!CanSubmit)
                return ArmyRouteHandle.Rejected(pRequest.ArmyId,
                    "route_provider_unavailable");
            EnsureProvider();
            return _host?.Submit(pRequest) ??
                   ArmyRouteHandle.Rejected(pRequest.ArmyId,
                       "route_provider_unavailable");
        }

        public static ArmyRoutePoll Poll(long pArmyId)
        {
            return _host?.Poll(pArmyId) ??
                   new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
        }

        public static void Cancel(long pArmyId,
            ArmyRouteCancelReason pReason)
        {
            _host?.Cancel(pArmyId, pReason);
        }

        public static void ClearWorld()
        {
            _host?.Dispose();
            _host = null;
        }

        private static void EnsureProvider()
        {
            int armyRouteWorkers =
                AWPerformanceSettings.ArmyRouteWorkerCount;
            AWPathFinder sharedFinder = AWPathfindingBootstrap.Finder;
            bool aw3 = AWPathfindingRuntimeMode.IsAw3;
            ArmyRouteProviderBackend backend = ArmyRouteProviderRules.
                SelectBackend(aw3,
                    AWPathfindingBootstrap.Cache.GenerationId >= 0,
                    armyRouteWorkers, sharedFinder != null);
            if (_host == null)
            {
                _host = new ArmyRouteProviderHost(CreateProvider(backend,
                    armyRouteWorkers, sharedFinder));
                return;
            }
            var currentAw3 = _host.CurrentProvider as
                Aw3ArmyRouteProvider;
            bool providerMatches = backend switch
            {
                ArmyRouteProviderBackend.Vanilla =>
                    _host.CurrentProvider is VanillaArmyRouteProvider,
                ArmyRouteProviderBackend.VanillaFallback =>
                    _host.CurrentProvider is VanillaArmyRouteProvider,
                ArmyRouteProviderBackend.Aw3Dedicated =>
                    currentAw3?.OwnsFinder == true,
                ArmyRouteProviderBackend.Aw3Shared =>
                    currentAw3 != null && !currentAw3.OwnsFinder,
                _ => false
            };
            if (providerMatches) return;
            _host.Install(CreateProvider(backend, armyRouteWorkers,
                sharedFinder));
        }

        private static IArmyRouteProvider CreateProvider(
            ArmyRouteProviderBackend pBackend, int pArmyRouteWorkers,
            AWPathFinder pSharedFinder)
        {
            return pBackend switch
            {
                ArmyRouteProviderBackend.Aw3Dedicated =>
                    new Aw3ArmyRouteProvider(pArmyRouteWorkers),
                ArmyRouteProviderBackend.Aw3Shared =>
                    new Aw3ArmyRouteProvider(pSharedFinder),
                _ => new VanillaArmyRouteProvider()
            };
        }
    }
#endif
}
