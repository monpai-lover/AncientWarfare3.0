using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;
using ai;
using life.taxi;
using UnityEngine;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathMovementBridge
    {
        private const float DiagonalTileDistance = 1.41421356237f;
        private const double TransportWaitTimeoutSeconds = 120d;
        private static readonly ConcurrentDictionary<long, RetryContext> RetryContexts =
            new ConcurrentDictionary<long, RetryContext>();
        private static readonly ConcurrentDictionary<long, AWPathPollResult> TerminalPolls =
            new ConcurrentDictionary<long, AWPathPollResult>();
        private static readonly ConcurrentDictionary<long, long> SubmissionTokens =
            new ConcurrentDictionary<long, long>();
        private static readonly ConcurrentDictionary<long, TransportContext> TransportContexts =
            new ConcurrentDictionary<long, TransportContext>();
        private static readonly AWPathOwnershipIndex OwnedActors =
            new AWPathOwnershipIndex();

        internal static int ActorGateCount => 0;

        public static ExecuteEvent Submit(Actor pActor, WorldTile pTarget, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions)
        {
            return SubmitCore(pActor, pTarget, new AWPathRequestOptions(pPathOnWater,
                pWalkOnBlocks, pWalkOnLava, pLimitPathfindingRegions), pIsRecovery: false);
        }

        internal static AWPathRequest CreateRecoveryRequest(
            AWPathAgentKey pAgentKey, AWPathRequest pPrevious)
        {
            if (pPrevious == null || pAgentKey.AgentId <= 0) return null;
            Actor actor = World.world?.units?.get(pAgentKey.AgentId);
            WorldTile target = actor?.tile_target;
            if (actor?.data == null || actor.current_tile?.data == null ||
                target?.data == null || actor.isRekt()) return null;
            return new AWPathRequest(
                pAgentKey,
                actor.current_tile.data.tile_id,
                target.data.tile_id,
                pPrevious.Options,
                pPrevious.Profile,
                pPrevious.Generation,
                Time.realtimeSinceStartupAsDouble,
                pPrevious.WorkClass,
                AWPathfindingBootstrap.Cache?.SourceRevision ??
                    pPrevious.TerrainRevision,
                pAgentKey.World.Generation,
                actor.is_inside_boat,
                pPrevious.PhysicalTransportAvailable);
        }

        private static ExecuteEvent SubmitCore(Actor pActor, WorldTile pTarget,
            AWPathRequestOptions pOptions, bool pIsRecovery,
            AWPathWorkClass? pRetainedWorkClass = null)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (pActor?.data == null || pTarget?.data == null ||
                pActor.current_tile?.data == null) return ExecuteEvent.False;
            if (AWPathLifecycleRules.ShouldBypassDecorativePath(
                    pActor.ai?.task?.id))
                return CompleteAtCurrentTile(pActor, finder,
                    pUpdateBehaviourTarget: true);
            if (finder == null) return ExecuteEvent.False;
            if (TransportContexts.TryGetValue(pActor.data.id, out TransportContext transport))
            {
                if (transport.TargetTileId == pTarget.data.tile_id &&
                    transport.Options.Equals(pOptions)) return ExecuteEvent.True;
                CancelTransport(pActor);
            }
            if (pActor.current_tile == pTarget)
            {
                return CompleteAtCurrentTile(pActor, finder,
                    pUpdateBehaviourTarget: false);
            }

            long actorId = pActor.data.id;
            int targetTileId = pTarget.data.tile_id;
            AWPathWorkClass workClass = pRetainedWorkClass ??
                ClassifyWork(pActor, actorId);
            long terrainRevision = AWPathfindingBootstrap.Cache.SourceRevision;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            AWPathAgentKey agentKey = new AWPathAgentKey(
                AWPathWorldKey.MainWorld(worldGeneration), actorId);
            bool insideBoat = pActor.is_inside_boat;
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            bool reused;
            try
            {
                var reuseKey = new AWPathReuseKey(agentKey,
                    AWPathfindingBootstrap.Cache.StartRegion(
                        pActor.current_tile.data.tile_id),
                    new AWPathRequestKey(targetTileId,
                        pOptions.PathOnWater, pOptions.WalkOnBlocks,
                        pOptions.WalkOnLava,
                        pOptions.LimitPathfindingRegions,
                        pOptions.BoundedMilitaryWater,
                        pOptions.MaximumConsecutiveWaterTiles),
                    terrainRevision, worldGeneration, insideBoat);
                reused = finder.TryReuse(actorId, reuseKey);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("path_submit_reuse",
                    diagnostic);
            }
            if (!reused)
            {
                if (ArmyMarchRules.ShouldClearRouteBeforeReplacement(reused))
                    AWArmyMarchService.OnPathEnded(pActor);
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                AWTraversalGeneration generation;
                try { generation = AWPathfindingBootstrap.Cache.Pin(); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail("path_submit_pin",
                        diagnostic);
                }
                if (generation == null) return ExecuteEvent.False;
                try
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                    AWActorTraversalProfile profile;
                    try { profile = CaptureProfile(pActor); }
                    finally
                    {
                        RuntimePerformanceDiagnostic.EndDetail(
                            "path_submit_profile", diagnostic);
                    }
                    var request = new AWPathRequest(agentKey,
                        pActor.current_tile.data.tile_id, targetTileId,
                        pOptions, profile, generation, now,
                        workClass, terrainRevision, worldGeneration,
                        insideBoat,
                        AWDockTransportService.TryResolveRoute(
                            pActor.current_tile, pTarget, out _));
                    diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                    bool accepted;
                    try
                    {
                        accepted = finder.SubmitNew(request,
                            out AWPathSubmissionDisposition disposition);
                        reused = disposition ==
                            AWPathSubmissionDisposition.Reused;
                    }
                    finally
                    {
                        RuntimePerformanceDiagnostic.EndDetail(
                            "path_submit_request", diagnostic);
                    }
                    if (!accepted) return ExecuteEvent.False;

                    if (finder.TryGetCurrentSubmissionToken(actorId,
                            out long submissionToken))
                        SubmissionTokens[actorId] = submissionToken;

                    if (!reused)
                    {
                        if (!pIsRecovery && RetryContexts.TryGetValue(actorId,
                                out RetryContext previous) &&
                            (previous.TargetTileId != targetTileId ||
                             !previous.Options.Equals(pOptions)))
                            AWPathfindingBootstrap.RecoveryManager.Clear(actorId);
                        RetryContexts[actorId] = new RetryContext(
                            targetTileId, pOptions, generation.Id,
                            pPending: false, 0d, now,
                            pAcceptedNoProgressAt: -1d, workClass,
                            pNextPollAt: now);
                        diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                        try
                        {
                            AWArmyMarchService.OnLeaderPathSubmitted(pActor,
                                pTarget, generation.Id);
                        }
                        finally
                        {
                            RuntimePerformanceDiagnostic.EndDetail(
                                "path_submit_army_route", diagnostic);
                        }
                    }
                }
                finally
                {
                    generation.Dispose();
                }
            }
            OwnedActors.Add(actorId);
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                pActor.clearOldPath();
                pActor.setTileTarget(pTarget);
                pActor.next_step_position = pActor.current_tile.posV3;
                TrySetNotMoving(pActor);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "path_submit_actor_state", diagnostic);
            }
            return ExecuteEvent.True;
        }

        private static ExecuteEvent CompleteAtCurrentTile(Actor pActor,
            AWPathFinder pFinder, bool pUpdateBehaviourTarget)
        {
            long actorId = pActor.data.id;
            CancelTransport(pActor);
            CancelFinderRequest(pFinder, actorId,
                AWPathFailureReason.CancelledByNewRequest);
            RetryContexts.TryRemove(actorId, out _);
            TerminalPolls.TryRemove(actorId, out _);
            OwnedActors.Remove(actorId);
            AWPathfindingBootstrap.RecoveryManager.Clear(actorId);
            pActor.clearOldPath();
            if (pUpdateBehaviourTarget)
                pActor.beh_tile_target = pActor.current_tile;
            pActor.setTileTarget(pActor.current_tile);
            pActor.moveTo(pActor.current_tile);
            return ExecuteEvent.True;
        }

        public static void Update(Actor pActor)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null) return;
            if (!HasOwnedPathState(pActor.data.id)) return;
            if (TransportContexts.ContainsKey(pActor.data.id))
            {
                ProcessTransport(pActor);
                return;
            }
            AWPathFinder.ReadyPathCursor cursor = default;
            AWPathPollResult poll;
            if (!TerminalPolls.TryRemove(pActor.data.id, out poll))
            {
                if (SubmissionTokens.TryGetValue(pActor.data.id,
                        out long submissionToken))
                    poll = finder.OpenReadyCursor(pActor.data.id,
                        submissionToken, out cursor);
                else
                    poll = finder.OpenReadyCursor(pActor.data.id,
                        out cursor);
            }
            HandlePoll(pActor, poll, ref cursor, pHandleNoRequest: true);
        }

        private static bool HandlePoll(Actor pActor, AWPathPollResult pPoll,
            ref AWPathFinder.ReadyPathCursor pCursor, bool pHandleNoRequest)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null) return false;
            if (pPoll.Kind == AWPathPollKind.StepReady)
            {
                bool executed = pCursor.TryExecuteCurrentStep(
                    pStep => TryMove(pActor, pStep), out bool moved);
                if (executed && moved)
                {
                    int routeGeneration = RetryContexts.TryGetValue(pActor.data.id,
                        out RetryContext routeContext)
                        ? routeContext.GenerationId
                        : AWPathfindingBootstrap.Cache.GenerationId;
                    AWArmyMarchService.OnLeaderPathStep(pActor, pPoll.Step,
                        routeGeneration);
                    if (pCursor.IsValid) pCursor.Consume();
                    else finder.Consume(pActor.data.id);
                    MarkRetryProgress(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                }
                else
                {
                    AWPathfindingBootstrap.PathDiagnostics.OnStaleStep();
                    CancelFinderRequest(finder, pActor.data.id,
                        AWPathFailureReason.UnsafeStep);
                    pCursor = default;
                    HandleFailure(pActor, AWPathFailureReason.UnsafeStep);
                }

                if (pActor.tile_target == null)
                {
                    AWArmyMarchService.OnPathEnded(pActor);
                    CancelFinderRequest(finder, pActor.data.id,
                        AWPathFailureReason.CancelledByNewRequest);
                    pCursor = default;
                    RetryContexts.TryRemove(pActor.data.id, out _);
                    OwnedActors.Remove(pActor.data.id);
                    pActor.stopMovement();
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                }
                return true;
            }

            if (!pHandleNoRequest)
            {
                if (pPoll.Kind != AWPathPollKind.Waiting) return false;
                if (ExpireWaitingRequestIfNeeded(pActor, finder))
                {
                    pCursor = default;
                    return true;
                }
                SetWaiting(pActor);
                return true;
            }

            switch (pPoll.Kind)
            {
                case AWPathPollKind.Waiting:
                    if (ExpireWaitingRequestIfNeeded(pActor, finder))
                    {
                        pCursor = default;
                        return true;
                    }
                    SetWaiting(pActor);
                    return true;
                case AWPathPollKind.Completed:
                    if (TryContinueBoundedSegment(pActor)) return true;
                    AWArmyMarchService.OnPathEnded(pActor);
                    TrySetNotMoving(pActor);
                    RetryContexts.TryRemove(pActor.data.id, out _);
                    OwnedActors.Remove(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                    return true;
                case AWPathPollKind.Failed:
                    AWArmyMarchService.OnPathEnded(pActor);
                    TrySetNotMoving(pActor);
                    HandleFailure(pActor, pPoll.FailureReason);
                    return true;
                case AWPathPollKind.Cancelled:
                    AWArmyMarchService.OnPathEnded(pActor);
                    TrySetNotMoving(pActor);
                    RetryContexts.TryRemove(pActor.data.id, out _);
                    OwnedActors.Remove(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
                    return true;
                default:
                    AWArmyMarchService.OnPathEnded(pActor);
                    if (TryStartDueRetry(pActor)) return true;
                    ReleaseStaleOwnership(pActor);
                    return true;
            }
        }

        private static bool TryContinueBoundedSegment(Actor pActor)
        {
            if (pActor?.data == null || pActor.current_tile?.data == null ||
                pActor.tile_target?.data == null ||
                !RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context) ||
                !AWPathLifecycleRules.ShouldContinueBoundedSegment(
                    context.Options.LimitPathfindingRegions,
                    pActor.current_tile.data.tile_id,
                    pActor.tile_target.data.tile_id)) return false;

            if (SubmitCore(pActor, pActor.tile_target, context.Options,
                    pIsRecovery: true,
                    pRetainedWorkClass: context.WorkClass) !=
                ExecuteEvent.False) return true;

            HandleFailure(pActor, AWPathFailureReason.Timeout);
            return true;
        }

        private static void SetWaiting(Actor pActor)
        {
            double interval = AWPathLifecycleRules.NormalWaitingPollSeconds;
            if (pActor?.data != null && RetryContexts.TryGetValue(
                    pActor.data.id, out RetryContext context))
            {
                double now = Time.realtimeSinceStartupAsDouble;
                interval = AWPathLifecycleRules.WaitingPollInterval(
                    context.WorkClass);
                RetryContexts[pActor.data.id] = new RetryContext(
                    context.TargetTileId, context.Options,
                    context.GenerationId, context.Pending,
                    context.DueTime, context.SubmittedAt,
                    context.AcceptedNoProgressAt,
                    context.WorkClass, now + interval);
            }
            TrySetNotMoving(pActor);
            pActor.next_step_position = pActor.current_tile?.posV3 ?? pActor.next_step_position;
            pActor.timer_action = (float)interval;
        }

        private static bool ExpireWaitingRequestIfNeeded(Actor pActor,
            AWPathFinder pFinder)
        {
            if (pActor?.data == null || pFinder == null ||
                !RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context)) return false;
            double now = Time.realtimeSinceStartupAsDouble;
            if (pFinder.IsWaitingForWorker(pActor.data.id))
            {
                return false;
            }
            if (!pFinder.IsWorkerRunning(pActor.data.id)) return false;
            double acceptedNoProgressAt =
                AWPathLifecycleRules.ResolveWorkerWatchdogBaseline(
                    context.AcceptedNoProgressAt, now);
            if (acceptedNoProgressAt != context.AcceptedNoProgressAt)
            {
                RetryContexts[pActor.data.id] = new RetryContext(
                    context.TargetTileId, context.Options,
                    context.GenerationId, context.Pending,
                    context.DueTime, context.SubmittedAt,
                    acceptedNoProgressAt, context.WorkClass,
                    context.NextPollAt);
                return false;
            }
            if (!AWPathLifecycleRules.ShouldExpireAcceptedRequest(
                    acceptedNoProgressAt, now)) return false;

            CancelFinderRequest(pFinder, pActor.data.id,
                AWPathFailureReason.Timeout);
            AWArmyMarchService.OnPathEnded(pActor);
            HandleFailure(pActor, AWPathFailureReason.Timeout);
            return true;
        }

        public static bool IsUsing(Actor pActor)
        {
            if (pActor?.data == null || AWPathfindingBootstrap.Finder == null) return false;
            long actorId = pActor.data.id;
            if (!HasOwnedPathState(actorId)) return false;
            if (TransportContexts.ContainsKey(pActor.data.id)) return true;
            if (pActor.tile_target?.data == null)
            {
                Cancel(pActor, AWPathFailureReason.CancelledByNewRequest);
                return false;
            }
            if (TerminalPolls.ContainsKey(pActor.data.id)) return true;
            AWPathPollResult current = AWPathfindingBootstrap.Finder.Poll(pActor.data.id);
            AWPathPollKind kind = current.Kind;
            if (kind == AWPathPollKind.Completed || kind == AWPathPollKind.Failed ||
                kind == AWPathPollKind.Cancelled)
            {
                TerminalPolls[pActor.data.id] = current;
                return true;
            }
            bool hasRetry = RetryContexts.TryGetValue(pActor.data.id,
                out RetryContext retry);
            return AWPathLifecycleRules.ShouldKeepMovementOwnership(kind,
                hasRetry, hasRetry && retry.Pending,
                hasLiveActorTarget: true);
        }

        internal static bool HasOwnership(Actor pActor)
        {
            return pActor?.data != null && HasOwnedPathState(pActor.data.id);
        }

        internal static string DescribeRuntimeState(Actor pActor)
        {
            if (pActor?.data == null) return "actor_invalid";
            try
            {
                long actorId = pActor.data.id;
                AWPathFinder finder = AWPathfindingBootstrap.Finder;
                AWPathSessionState session = finder == null
                    ? AWPathSessionState.None
                    : finder.ReadState(actorId);
                bool hasRetry = RetryContexts.TryGetValue(actorId,
                    out RetryContext retry);
                double retryDue = hasRetry && retry.Pending
                    ? retry.DueTime : 0d;
                double remaining = retryDue > 0d
                    ? Math.Max(0d, retryDue - Time.realtimeSinceStartupAsDouble)
                    : 0d;
                return "poll=" + session.DiagnosticPollKind +
                    ",failure=" + session.FailureReason +
                    ",session=" + session.RequestState +
                    ",retry=" + hasRetry +
                    ",retry_pending=" + (hasRetry && retry.Pending) +
                    ",retry_remaining=" + remaining.ToString("0.###") +
                    ",worker_waiting=" + session.IsLatestQueued +
                    ",worker_running=" + session.IsLatestRunning +
                    ",owned=" + HasOwnedPathState(actorId) +
                    ",current=" + (pActor.current_tile?.data?.tile_id ?? -1) +
                    ",target=" + (pActor.tile_target?.data?.tile_id ?? -1) +
                    ",moving=" + pActor.is_moving;
            }
            catch (Exception error)
            {
                return "diagnostic_error=" + error.GetType().Name;
            }
        }

        internal static bool ShouldPollNow(Actor pActor)
        {
            if (pActor?.data == null) return true;
            long actorId = pActor.data.id;
            if (AWPathLifecycleRules.ShouldPollEverySimulationPass(
                    schedulerActive: AWPerformanceSettings.Mode ==
                        AWSimulationMode.Large &&
                        AWSimulationStepContext.IsActive &&
                        Config.time_scale_asset?.multiplier > 0f,
                    customPathOwned: HasOwnedPathState(actorId)))
                return true;
            if (TerminalPolls.ContainsKey(actorId)) return true;
            if (TransportContexts.TryGetValue(actorId,
                    out TransportContext transport))
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (!AWPathLifecycleRules.ShouldPollWaiting(now,
                        transport.NextPollAt)) return false;
                TransportContexts[actorId] = transport.WithNextPoll(
                    now + AWPathLifecycleRules.NormalWaitingPollSeconds);
                return true;
            }
            if (!RetryContexts.TryGetValue(actorId,
                    out RetryContext context)) return true;
            return AWPathLifecycleRules.ShouldPollWaiting(
                Time.realtimeSinceStartupAsDouble, context.NextPollAt);
        }

        private static bool HasOwnedPathState(long pActorId)
        {
            return OwnedActors.Contains(pActorId);
        }

        // Compatibility surface for the cooperative actor runner. The
        // movement bridge remains the single owner of path polling; callers
        // prepare a serial commit whenever native actor state is involved.
        internal enum AWParallelPathMovementResult
        {
            NoPath,
            Handled,
            RequiresSerial
        }

        internal enum AWParallelSmoothMovementResult
        {
            Handled,
            RequiresSerial
        }

        internal enum AWPreparedSmoothMovementKind : byte
        {
            None,
            Calibration,
            VanillaPath,
            CustomPath,
            StopMovement
        }

        internal readonly struct AWPreparedPathMovement
        {
            internal AWPreparedPathMovement(Actor pActor, bool pVanilla)
            {
                Vanilla = pVanilla;
                Poll = default;
                Cursor = default;
                ActorId = pActor?.data?.id ?? -1L;
                CurrentTileId = pActor?.current_tile?.data?.tile_id ?? -1;
                TargetTileId = pActor?.tile_target?.data?.tile_id ?? -1;
                LocalPathIndex = pActor?.current_path_index ?? -1;
                HadGlobalPath = pActor?.current_path_global != null;
            }

            internal AWPreparedPathMovement(AWPathPollResult pPoll,
                AWPathFinder.ReadyPathCursor pCursor)
            {
                Vanilla = false;
                Poll = pPoll;
                Cursor = pCursor;
                ActorId = -1L;
                CurrentTileId = -1;
                TargetTileId = -1;
                LocalPathIndex = -1;
                HadGlobalPath = false;
            }

            internal bool Vanilla { get; }
            internal AWPathPollResult Poll { get; }
            internal AWPathFinder.ReadyPathCursor Cursor { get; }
            internal long ActorId { get; }
            internal int CurrentTileId { get; }
            internal int TargetTileId { get; }
            internal int LocalPathIndex { get; }
            internal bool HadGlobalPath { get; }
        }

        internal readonly struct AWPreparedSmoothMovement
        {
            internal AWPreparedSmoothMovement(
                AWPreparedSmoothMovementKind pKind,
                float pWalkedDistance = 0f,
                AWPathPollResult pPoll = default,
                AWPathFinder.ReadyPathCursor pCursor = default)
            {
                Kind = pKind;
                WalkedDistance = pWalkedDistance;
                Poll = pPoll;
                Cursor = pCursor;
            }

            internal AWPreparedSmoothMovementKind Kind { get; }
            internal float WalkedDistance { get; }
            internal AWPathPollResult Poll { get; }
            internal AWPathFinder.ReadyPathCursor Cursor { get; }
        }

        internal static AWParallelPathMovementResult
            TryRunParallelSafePathMovement(Actor pActor,
                out AWPreparedPathMovement pPrepared)
        {
            pPrepared = default;
            if (pActor?.data == null) return AWParallelPathMovementResult.NoPath;
            // RTS march callbacks update shared formation state. Keep every
            // active RTS march on the ordered simulation commit path.
            if (HasArmyMarchState(pActor))
            {
                pPrepared = new AWPreparedPathMovement(pActor, pVanilla: true);
                return AWParallelPathMovementResult.RequiresSerial;
            }
            if (pActor.isFollowingLocalPath() ||
                pActor.current_path_global != null)
            {
                pPrepared = new AWPreparedPathMovement(pActor, pVanilla: true);
                return AWParallelPathMovementResult.RequiresSerial;
            }

            if (!HasOwnedPathState(pActor.data.id))
                return AWParallelPathMovementResult.NoPath;

            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null)
                return AWParallelPathMovementResult.NoPath;

            AWPathPollResult poll = finder.OpenReadyCursor(
                pActor.data.id,
                out AWPathFinder.ReadyPathCursor cursor);
            if (poll.Kind != AWPathPollKind.StepReady &&
                poll.Kind != AWPathPollKind.Waiting)
                return AWParallelPathMovementResult.NoPath;

            if (poll.Kind == AWPathPollKind.StepReady &&
                !CanRunPathStepInParallel(pActor, poll.Step))
            {
                pPrepared = new AWPreparedPathMovement(poll, cursor);
                return AWParallelPathMovementResult.RequiresSerial;
            }

            HandlePoll(pActor, poll, ref cursor,
                pHandleNoRequest: true);
            return AWParallelPathMovementResult.Handled;
        }

        internal static PreparedNativePathCommitDecision
            CommitPreparedPathMovement(Actor pActor,
            AWPreparedPathMovement pPrepared)
        {
            if (pActor?.data == null) return PreparedNativePathCommitDecision.Drop;
            if (pPrepared.Vanilla)
            {
                bool actorAlive = false;
                try
                {
                    actorAlive = !pActor.isRekt() && pActor.isAlive();
                }
                catch { }
                WorldTile currentTile = pActor.current_tile;
                WorldTile targetTile = pActor.tile_target;
                var facts = new PreparedNativePathFacts(
                    actorExists: pActor.data != null,
                    actorAlive: actorAlive,
                    actorIdMatches: pActor.data != null &&
                        pActor.data.id == pPrepared.ActorId,
                    batchExists: pActor.batch != null,
                    currentTileValid: currentTile?.data != null,
                    targetTileValid: targetTile?.data != null,
                    currentRegionValid: currentTile?.region != null,
                    targetRegionValid: targetTile?.region != null,
                    currentTileId: currentTile?.data?.tile_id ?? -1,
                    preparedCurrentTileId: pPrepared.CurrentTileId,
                    currentTargetTileId: targetTile?.data?.tile_id ?? -1,
                    preparedTargetTileId: pPrepared.TargetTileId,
                    currentPathIndex: pActor.current_path_index,
                    preparedPathIndex: pPrepared.LocalPathIndex,
                    currentHasGlobalPath: pActor.current_path_global != null,
                    preparedHadGlobalPath: pPrepared.HadGlobalPath);
                PreparedNativePathCommitDecision decision =
                    PreparedNativePathCommitRules.Decide(facts);
                if (decision == PreparedNativePathCommitDecision.Drop)
                {
                    DropPreparedNativePath(pActor);
                    return decision;
                }
                if (decision == PreparedNativePathCommitDecision.RetryLater)
                    return decision;
                pActor.updatePathMovement();
                return PreparedNativePathCommitDecision.Commit;
            }

            AWPathFinder.ReadyPathCursor cursor = pPrepared.Cursor;
            HandlePoll(pActor, pPrepared.Poll, ref cursor,
                pHandleNoRequest: true);
            return PreparedNativePathCommitDecision.Commit;
        }

        private static void DropPreparedNativePath(Actor pActor)
        {
            if (pActor == null) return;
            try { pActor.clearOldPath(); }
            catch { }
            try { pActor.clearTileTarget(); }
            catch { }
            try { pActor.beh_tile_target = null; }
            catch { }
            TrySetNotMoving(pActor);
        }

        internal static AWParallelSmoothMovementResult
            TryRunParallelSafeSmoothMovement(Actor pActor, float pElapsed,
                out AWPreparedSmoothMovement pPrepared)
        {
            pPrepared = default;
            if (pActor?.data == null || pActor._update_done || pActor.is_immovable)
                return AWParallelSmoothMovementResult.Handled;
            if (HasArmyMarchState(pActor))
            {
                pPrepared = new AWPreparedSmoothMovement(
                    AWPreparedSmoothMovementKind.VanillaPath);
                return AWParallelSmoothMovementResult.RequiresSerial;
            }

            float movementBudget =
                pActor._current_combined_movement_speed * pElapsed;
            bool canFlip = pActor.asset.can_flip && pActor.checkFlip();
            float walkedDistance = 0f;
            AWPathFinder.ReadyPathCursor cursor = default;
            for (int i = 0;
                 i < AWPathLifecycleRules.MaximumSmoothPathStepsPerUpdate;
                 i++)
            {
                Vector2 current = pActor.current_position;
                Vector2 target = pActor.next_step_position;
                if (canFlip)
                    pActor.setFlip(current.x < target.x);
                float delta = Math.Max(0f, movementBudget - walkedDistance);
                float dx = target.x - current.x;
                float dy = target.y - current.y;
                float distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= delta * delta)
                {
                    if (delta > 0f && distanceSquared > 0f)
                    {
                        float scale = delta / Mathf.Sqrt(distanceSquared);
                        pActor.current_position = new Vector2(
                            current.x + dx * scale, current.y + dy * scale);
                    }
                    return AWParallelSmoothMovementResult.Handled;
                }

                pActor.current_position = target;
                walkedDistance += BoundaryDistance(distanceSquared);
                if (!TryContinueSmoothMovementInParallel(
                        pActor, ref cursor, walkedDistance, out pPrepared))
                    return AWParallelSmoothMovementResult.RequiresSerial;
                if (!pActor.is_moving)
                    return AWParallelSmoothMovementResult.Handled;
            }

            return AWParallelSmoothMovementResult.Handled;
        }

        internal static void CommitPreparedSmoothMovement(Actor pActor,
            float pElapsed, AWPreparedSmoothMovement pPrepared)
        {
            if (pActor?.data == null) return;
            switch (pPrepared.Kind)
            {
                case AWPreparedSmoothMovementKind.VanillaPath:
                    pActor.updatePathMovement();
                    break;
                case AWPreparedSmoothMovementKind.CustomPath:
                    {
                        AWPathFinder.ReadyPathCursor cursor = pPrepared.Cursor;
                        if (!HandlePoll(pActor, pPrepared.Poll, ref cursor,
                                pHandleNoRequest: false))
                            pActor.stopMovement();
                        break;
                    }
                case AWPreparedSmoothMovementKind.StopMovement:
                    pActor.stopMovement();
                    return;
                case AWPreparedSmoothMovementKind.None:
                    return;
            }

            if (pActor.is_moving)
                UpdateSmoothMovement(pActor, pElapsed,
                    pPrepared.WalkedDistance);
        }

        private static bool TryContinueSmoothMovementInParallel(Actor pActor,
            ref AWPathFinder.ReadyPathCursor pCursor, float pWalkedDistance,
            out AWPreparedSmoothMovement pPrepared)
        {
            if (pActor.isFollowingLocalPath() ||
                pActor.current_path_global != null)
            {
                pPrepared = new AWPreparedSmoothMovement(
                    AWPreparedSmoothMovementKind.VanillaPath,
                    pWalkedDistance);
                return false;
            }
            if (pActor.tile_target == null)
            {
                pPrepared = new AWPreparedSmoothMovement(
                    AWPreparedSmoothMovementKind.StopMovement,
                    pWalkedDistance);
                return false;
            }

            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            AWPathPollResult poll = pCursor.IsValid
                ? pCursor.Poll()
                : finder == null
                    ? new AWPathPollResult(AWPathPollKind.NoRequest)
                    : finder.OpenReadyCursor(pActor.data.id, out pCursor);
            if (poll.Kind == AWPathPollKind.StepReady)
            {
                if (!CanRunPathStepInParallel(pActor, poll.Step))
                {
                    pPrepared = new AWPreparedSmoothMovement(
                        AWPreparedSmoothMovementKind.CustomPath,
                        pWalkedDistance, poll, pCursor);
                    return false;
                }
                HandlePoll(pActor, poll, ref pCursor,
                    pHandleNoRequest: false);
                pPrepared = default;
                return true;
            }
            if (poll.Kind == AWPathPollKind.Waiting)
            {
                HandlePoll(pActor, poll, ref pCursor,
                    pHandleNoRequest: false);
                pPrepared = default;
                return true;
            }

            pPrepared = new AWPreparedSmoothMovement(
                AWPreparedSmoothMovementKind.CustomPath,
                pWalkedDistance, poll, pCursor);
            return false;
        }

        private static bool CanRunPathStepInParallel(Actor pActor,
            AWPathStep pStep)
        {
            if (pActor?.data == null || pActor.asset == null ||
                pActor.current_tile == null || pActor.asset.is_boat ||
                (pStep.Method != AWMovementMethod.Walk &&
                 pStep.Method != AWMovementMethod.Swim))
                return false;
            WorldTile tile = World.world?.tiles_list == null ||
                pStep.TileId < 0 ||
                pStep.TileId >= World.world.tiles_list.Length
                ? null : World.world.tiles_list[pStep.TileId];
            if (tile?.Type == null || tile.Type.damaged_when_walked)
                return false;
            return (pStep.Hazards & AWHazardFlags.Fire) != 0 ||
                   GetFastMoveBlockReason(tile) == SlowMoveReason.None;
        }

        private static bool HasArmyMarchState(Actor pActor)
        {
            try
            {
                return AWArmyMarchService.HasSerialMarchState(pActor);
            }
            catch
            {
                // If RTS ownership cannot be inspected, fail closed into the
                // serial path so a shared formation route is never advanced
                // from the worker stage.
                return true;
            }
        }

        public static bool ShouldUseCustomSmoothMovement(Actor pActor)
        {
            if (pActor?.data == null) return false;
            return AWPathLifecycleRules.ShouldUseCustomSmoothMovement(
                AWPerformanceSettings.EnableFramePriorityScheduler,
                HasOwnedPathState(pActor.data.id),
                pActor.isFollowingLocalPath(),
                pActor.current_path_global != null);
        }

        public static void Cancel(Actor pActor, AWPathFailureReason pReason)
        {
            if (pActor?.data == null) return;
            AWArmyMarchService.OnPathEnded(pActor);
            CancelTransport(pActor);
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            CancelFinderRequest(finder, pActor.data.id, pReason);
            AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
            finder?.ClearRecovery(pActor.data.id);
            RetryContexts.TryRemove(pActor.data.id, out _);
            TerminalPolls.TryRemove(pActor.data.id, out _);
            OwnedActors.Remove(pActor.data.id);
        }

        private static void CancelFinderRequest(AWPathFinder pFinder,
            long pActorId, AWPathFailureReason pReason)
        {
            if (pFinder == null) return;
            if (SubmissionTokens.TryRemove(pActorId,
                    out long submissionToken))
            {
                // A stale token is a completed/replaced request. Do not fall
                // back to actor-id cancellation, which could cancel a newer
                // request sharing the legacy actor API.
                pFinder.CancelOwned(pActorId, submissionToken, pReason);
                return;
            }
            pFinder.Cancel(pActorId, pReason);
        }

        public static void Clear()
        {
            foreach (TransportContext context in TransportContexts.Values)
            {
                Actor actor = context.Actor;
                if (actor != null && !actor.is_inside_boat)
                    TaxiManager.cancelTaxiRequestForActor(actor);
            }
            TransportContexts.Clear();
            RetryContexts.Clear();
            TerminalPolls.Clear();
            SubmissionTokens.Clear();
            OwnedActors.Clear();
            AWPathfindingBootstrap.Finder?.Clear(
                AWPathFailureReason.WorldCleared);
            AWArmyMarchService.ClearLegacy();
        }

        public static void UpdateSmoothMovement(Actor pActor, float pElapsed,
            float pWalkedDistance = 0f)
        {
            if (pActor?.asset == null) return;
            bool actorAlive = false;
            try { actorAlive = pActor.data != null && pActor.isAlive(); }
            catch { }
            bool batchExists = pActor.batch != null;
            bool queueExists = batchExists &&
                               pActor.batch.c_update_movement != null;
            bool p0TransportBoat = pActor.data != null && actorAlive &&
                pActor.asset.is_boat &&
                ArmyRtsTransportService.OwnsTransportBoat(pActor);
            if (!AWPathLifecycleRules.HasUsableMovementBatch(
                    pActor.data != null, actorAlive, batchExists,
                    queueExists) && !p0TransportBoat)
            {
                Cancel(pActor, AWPathFailureReason.CancelledByNewRequest);
                return;
            }
            pElapsed = AWPathLifecycleRules.NormalizeMovementElapsed(
                pElapsed, World.world?.delta_time ?? 0.02f,
                schedulerActive: AWPerformanceSettings.Mode ==
                    AWSimulationMode.Large && AWSimulationStepContext.IsActive &&
                    Config.time_scale_asset?.multiplier > 0f);
            float movementBudget = pActor._current_combined_movement_speed * pElapsed;
            bool canFlip = pActor.asset.can_flip && pActor.checkFlip();
            AWPathFinder.ReadyPathCursor customPathCursor = default;
            for (int i = 0;
                 i < AWPathLifecycleRules.MaximumSmoothPathStepsPerUpdate;
                 i++)
            {
                Vector2 current = pActor.current_position;
                Vector2 target = pActor.next_step_position;
                if (canFlip) pActor.setFlip(current.x < target.x);
                float movementDelta = Math.Max(0f, movementBudget - pWalkedDistance);
                float dx = target.x - current.x;
                float dy = target.y - current.y;
                float distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= movementDelta * movementDelta)
                {
                    if (movementDelta > 0f && distanceSquared > 0f)
                    {
                        float scale = movementDelta / Mathf.Sqrt(distanceSquared);
                        pActor.current_position = new Vector2(current.x + dx * scale,
                            current.y + dy * scale);
                    }
                    return;
                }

                pActor.current_position = target;
                float walked = BoundaryDistance(distanceSquared);
                ContinuePathMovementFromSmooth(pActor, ref customPathCursor);
                if (!pActor.is_moving) return;
                pWalkedDistance += walked;
            }
        }

        private static void ContinuePathMovementFromSmooth(Actor pActor,
            ref AWPathFinder.ReadyPathCursor pCustomPathCursor)
        {
            if (pActor.isFollowingLocalPath() || pActor.current_path_global != null)
            {
                pActor.updatePathMovement();
                return;
            }

            if (pActor.tile_target != null)
            {
                AWPathPollResult poll;
                if (pCustomPathCursor.IsValid)
                    poll = pCustomPathCursor.Poll();
                else
                {
                    AWPathFinder finder = AWPathfindingBootstrap.Finder;
                    if (finder == null)
                        poll = new AWPathPollResult(AWPathPollKind.NoRequest);
                    else if (SubmissionTokens.TryGetValue(pActor.data.id,
                                 out long submissionToken))
                        poll = finder.OpenReadyCursor(pActor.data.id,
                            submissionToken, out pCustomPathCursor);
                    else
                        poll = finder.OpenReadyCursor(pActor.data.id,
                            out pCustomPathCursor);
                }

                if (HandlePoll(pActor, poll, ref pCustomPathCursor,
                        pHandleNoRequest: false)) return;
            }

            pActor.stopMovement();
        }

        private static bool TryMove(Actor pActor, AWPathStep pStep)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || pStep.TileId < 0 || pStep.TileId >= tiles.Length) return false;
            WorldTile tile = tiles[pStep.TileId];
            if (tile?.Type == null || pActor.current_tile == null) return false;
            if (pStep.Method == AWMovementMethod.Transport)
                return StartTransport(pActor, tile);
            if ((pStep.Hazards & AWHazardFlags.Direct) == 0 &&
                Toolbox.SquaredDistTile(pActor.current_tile, tile) > 2) return false;
            if (pActor.asset.is_boat && !tile.isGoodForBoat()) return false;
            if (tile.Type.block && !pActor.ignoresBlocks()) return false;
            if (tile.Type.lava && pActor.asset.die_in_lava && !pActor.isImmuneToFire()) return false;
            bool boundedMilitaryWater = RetryContexts.TryGetValue(
                pActor.data.id, out RetryContext retryContext) &&
                retryContext.Options.BoundedMilitaryWater;
            if (tile.Type.ocean &&
                !AWNarrowWaterRecoveryRules.CanEnterDamagingWater(
                    pActor.isDamagedByOcean(), pActor.isInLiquid(),
                    boundedMilitaryWater,
                    pStep.Method == AWMovementMethod.Swim)) return false;

            if (tile.Type.damaged_when_walked) pActor.current_tile.tryToBreak();

            bool adjacentStep = (pStep.Hazards & AWHazardFlags.Direct) == 0;
            bool plannedFire = (pStep.Hazards & AWHazardFlags.Fire) != 0;
            bool p0TransportBoat = pActor.asset.is_boat &&
                ArmyRtsTransportService.OwnsTransportBoat(pActor);
            SlowMoveReason slowMoveReason = pActor.asset.is_boat
                ? SlowMoveReason.Boat
                : SlowMoveReason.None;
            bool useFastMove = false;
            if (!pActor.asset.is_boat)
            {
                if (plannedFire)
                    useFastMove = true;
                else
                {
                    slowMoveReason = GetFastMoveBlockReason(tile);
                    useFastMove = slowMoveReason == SlowMoveReason.None;
                }
            }

            if (useFastMove || p0TransportBoat)
            {
                bool moved = FastMoveTo(pActor, tile, adjacentStep);
                if (!moved) return false;
                AWPathfindingBootstrap.PathDiagnostics.OnFastStep();
            }
            else if (CanReplayMoveToSideEffects(slowMoveReason))
            {
                bool moved = FastMoveToWithMoveToSideEffects(pActor, tile,
                    adjacentStep);
                if (!moved) return false;
                AWPathfindingBootstrap.PathDiagnostics.OnFastStep();
            }
            else
            {
                pActor.moveTo(tile);
                AWPathfindingBootstrap.PathDiagnostics.OnVanillaStep();
            }
            return true;
        }

        private static SlowMoveReason GetFastMoveBlockReason(WorldTile pTile)
        {
            if (pTile.Type.step_action != null) return SlowMoveReason.TileStepAction;
            Building building = pTile.building;
            if (building?.asset == null || !building.asset.flora) return SlowMoveReason.None;

            BuildingAsset asset = building.asset;
            switch (asset.flora_type)
            {
                case FloraType.Fungi:
                    return WorldLawLibrary.world_law_exploding_mushrooms.isEnabled()
                        ? SlowMoveReason.FungiLaw
                        : SlowMoveReason.None;
                case FloraType.Plant:
                    if (asset.type == "type_flower" &&
                        WorldLawLibrary.world_law_nectar_nap.isEnabled())
                        return SlowMoveReason.FlowerNectarLaw;
                    return WorldLawLibrary.world_law_plants_tickles.isEnabled() ||
                           WorldLawLibrary.world_law_root_pranks.isEnabled()
                        ? SlowMoveReason.PlantLaw
                        : SlowMoveReason.None;
                default:
                    return SlowMoveReason.None;
            }
        }

        private static bool FastMoveTo(Actor pActor, WorldTile pTile,
            bool pAdjacentStep)
        {
            if (!SetMoveStepTile(pActor, pTile, pAdjacentStep)) return false;
            pActor.next_step_position = new Vector2(pTile.posV3.x, pTile.posV3.y);
            return true;
        }

        private static bool FastMoveToWithMoveToSideEffects(Actor pActor,
            WorldTile pTile, bool pAdjacentStep)
        {
            if (!pActor.has_attack_target && pActor.current_tile != null && pTile.isOnFire() &&
                !pActor.current_tile.isOnFire() && !pActor.isImmuneToFire())
            {
                pActor.cancelAllBeh();
                return false;
            }

            if (!SetMoveStepTile(pActor, pTile, pAdjacentStep)) return false;
            ApplyStepActionForCurrentTile(pActor);
            pActor.next_step_position = new Vector2(pTile.posV3.x, pTile.posV3.y);
            return true;
        }

        private static bool SetMoveStepTile(Actor pActor, WorldTile pTile,
            bool pAdjacentStep)
        {
            bool actorExists = pActor?.data != null;
            bool actorAlive = false;
            try { actorAlive = actorExists && pActor.isAlive(); }
            catch { }
            bool batchExists = pActor?.batch != null;
            bool movementQueueExists = batchExists &&
                                       pActor.batch.c_update_movement != null;
            bool p0TransportBoat = actorExists && actorAlive &&
                pActor.asset?.is_boat == true &&
                ArmyRtsTransportService.OwnsTransportBoat(pActor);
            if ((!AWPathLifecycleRules.HasUsableMovementBatch(actorExists,
                    actorAlive, batchExists, movementQueueExists) &&
                 !p0TransportBoat) ||
                pTile?.data == null || pActor.current_tile?.data == null)
                return false;
            if (!pActor._is_moving)
            {
                pActor._is_moving = true;
                if (movementQueueExists)
                    pActor.batch.c_update_movement.Add(pActor);
            }

            pActor._next_step_tile = pTile;
            if (p0TransportBoat || pAdjacentStep)
                pActor.current_tile = pTile;
            else if (Toolbox.SquaredDistTile(pActor.current_tile, pTile) > 4f)
                pActor.dirty_current_tile = true;
            else
                pActor.current_tile = pTile;
            return true;
        }

        private static void ApplyStepActionForCurrentTile(Actor pActor)
        {
            WorldTile currentTile = pActor.current_tile;
            var tileType = currentTile?.Type;
            if (tileType == null) return;

            if (tileType.step_action != null && Randy.randomChance(tileType.step_action_chance))
                tileType.step_action(currentTile, pActor);

            Building building = currentTile.building;
            if (building?.asset == null || !building.asset.flora) return;
            BuildingAsset asset = building.asset;
            switch (asset.flora_type)
            {
                case FloraType.Fungi:
                    if (WorldLawLibrary.world_law_exploding_mushrooms.isEnabled())
                    {
                        MapAction.damageWorld(currentTile, 5, AssetManager.terraform.get("grenade"));
                        EffectsLibrary.spawnAtTileRandomScale("fx_explosion_small", currentTile,
                            0.1f, 0.15f);
                    }
                    break;
                case FloraType.Plant:
                    if (asset.type == "type_flower" &&
                        WorldLawLibrary.world_law_nectar_nap.isEnabled() &&
                        Randy.randomChance(0.1f))
                    {
                        pActor.makeSleep(10f);
                        break;
                    }
                    if (WorldLawLibrary.world_law_plants_tickles.isEnabled() &&
                        Randy.randomChance(0.3f))
                        pActor.tryToGetSurprised(currentTile);
                    if (WorldLawLibrary.world_law_root_pranks.isEnabled() &&
                        Randy.randomChance(0.2f))
                        pActor.makeStunned();
                    break;
            }
        }

        private static bool CanReplayMoveToSideEffects(SlowMoveReason pReason)
        {
            return pReason == SlowMoveReason.TileStepAction ||
                   pReason == SlowMoveReason.FungiLaw ||
                   pReason == SlowMoveReason.FlowerNectarLaw ||
                   pReason == SlowMoveReason.PlantLaw;
        }

        private static bool StartTransport(Actor pActor, WorldTile pTarget)
        {
            if (pActor?.current_tile?.data == null || pTarget?.data == null ||
                !AWDockTransportRules.CanCreatePhysicalRoute(
                    pActor.current_tile.data.tile_id, pTarget.data.tile_id,
                    pActor.current_tile.isSameIsland(pTarget),
                    pActor.is_inside_boat)) return false;
            if (!AWDockTransportService.TryResolveRoute(pActor.current_tile,
                    pTarget, out AWDockRouteCandidate route)) return false;
            if (!RetryContexts.TryGetValue(pActor.data.id, out RetryContext retry)) return false;
            if (!AWDockTaxiRouteService.TryCreateOrJoinRequest(
                    pActor, pActor.current_tile, pTarget,
                    out TaxiRequest request) || request == null)
                return false;
            AWPathfindingBootstrap.PathDiagnostics.OnDockRequest();

            double now = Time.realtimeSinceStartupAsDouble;
            long actorId = pActor.data.id;
            TransportContexts[actorId] = new TransportContext(pActor, pTarget.data.tile_id,
                retry.Options, now, pObservedInsideBoat: false,
                pNextPollAt: now, pEntryDockId: route.Entry.Id,
                pExitDockId: route.Exit.Id);
            TrySetNotMoving(pActor);
            pActor.next_step_position = pActor.current_tile.posV3;
            return true;
        }

        private static void ProcessTransport(Actor pActor)
        {
            if (pActor?.data == null ||
                !TransportContexts.TryGetValue(pActor.data.id, out TransportContext context))
                return;
            if (pActor.isRekt())
            {
                RemoveTransportContext(pActor.data.id);
                return;
            }

            WorldTile target = pActor.tile_target;
            if (target?.data == null ||
                target.data.tile_id != context.TargetTileId)
            {
                FailTransport(pActor, AWPathFailureReason.TransportFailed, pCancelTaxi: true);
                return;
            }
            if (!AWDockTransportService.IsEndpointLive(context.EntryDockId) ||
                !AWDockTransportService.IsEndpointLive(context.ExitDockId))
            {
                FailTransport(pActor, AWPathFailureReason.TransportFailed,
                    pCancelTaxi: true);
                return;
            }
            TaxiRequest request = TaxiManager.getRequestForActor(pActor);
            double now = Time.realtimeSinceStartupAsDouble;
            bool reachedDestination = pActor.current_tile != null &&
                                      pActor.current_tile.isSameIsland(target);
            AWDockPassengerState nextState = AWDockTransportRules.NextState(
                context.State, alive: true, targetValid: true,
                insideBoat: pActor.is_inside_boat, requestExists: request != null,
                reachedDestination: reachedDestination,
                timedOut: request != null && now - context.StartedAt >=
                    TransportWaitTimeoutSeconds);
            if (nextState != context.State)
            {
                context = context.WithState(nextState);
                TransportContexts[pActor.data.id] = context;
            }
            if (nextState == AWDockPassengerState.Failed)
            {
                FailTransport(pActor, AWPathFailureReason.TransportFailed,
                    pCancelTaxi: request != null);
                return;
            }
            if (pActor.is_inside_boat)
            {
                if (!context.ObservedInsideBoat)
                    TransportContexts[pActor.data.id] = context.WithObservedInsideBoat();
                return;
            }

            if (request != null)
            {
                if (now - context.StartedAt < TransportWaitTimeoutSeconds) return;
                FailTransport(pActor, AWPathFailureReason.TransportFailed, pCancelTaxi: true);
                return;
            }

            if (pActor.current_tile != null && pActor.current_tile.isSameIsland(target))
            {
                RemoveTransportContext(pActor.data.id);
                TerminalPolls.TryRemove(pActor.data.id, out _);
                AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                AWPathWorkClass workClass = RetryContexts.TryGetValue(
                    pActor.data.id, out RetryContext retry)
                    ? retry.WorkClass
                    : ClassifyWork(pActor, pActor.data.id);
                if (SubmitCore(pActor, target, context.Options,
                        pIsRecovery: true,
                        pRetainedWorkClass: workClass) ==
                    ExecuteEvent.False)
                    HandleFailure(pActor, AWPathFailureReason.Timeout);
                return;
            }

            FailTransport(pActor, AWPathFailureReason.TransportFailed, pCancelTaxi: false);
        }

        private static void FailTransport(Actor pActor, AWPathFailureReason pReason,
            bool pCancelTaxi)
        {
            if (pCancelTaxi && !pActor.is_inside_boat)
                CancelTaxiForActor(pActor);
            RemoveTransportContext(pActor.data.id);
            CancelFinderRequest(AWPathfindingBootstrap.Finder,
                pActor.data.id, pReason);
            HandleFailure(pActor, pReason);
        }

        private static void CancelTransport(Actor pActor)
        {
            if (pActor?.data == null || !TransportContexts.ContainsKey(pActor.data.id)) return;
            if (!pActor.is_inside_boat) CancelTaxiForActor(pActor);
            RemoveTransportContext(pActor.data.id);
        }

        private static void CancelTaxiForActor(Actor pActor)
        {
            TaxiRequest request = TaxiManager.getRequestForActor(pActor);
            if (request == null) return;
            if (request.countActors() > 1)
                request.embarkToBoat(pActor);
            else
                TaxiManager.cancelRequest(request);
        }

        private static void RemoveTransportContext(long pActorId)
        {
            TransportContexts.TryRemove(pActorId, out _);
        }

        private static void HandleFailure(Actor pActor, AWPathFailureReason pReason)
        {
            TrySetNotMoving(pActor);
            if (TryStartNarrowWaterRecovery(pActor, pReason)) return;

            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder != null)
            {
                AWPathRecoveryScheduleResult central =
                    finder.TryScheduleRecovery(pActor.data.id, pReason,
                        out float centralDelay);
                if (central == AWPathRecoveryScheduleResult.Scheduled)
                {
                    AWPathfindingBootstrap.PathDiagnostics.OnBoatRetry();
                    MarkCentralRetryPending(pActor, centralDelay);
                    return;
                }
                if (central == AWPathRecoveryScheduleResult.AlreadyPending)
                {
                    MarkCentralRetryPending(pActor, 0.25f);
                    return;
                }
                if (central == AWPathRecoveryScheduleResult.Exhausted)
                {
                    ResetAfterTerminalFailure(pActor);
                    return;
                }
            }

            double now = Time.realtimeSinceStartupAsDouble;
            AWPathRetryDecision retry = AWPathfindingBootstrap.RecoveryManager.OnFailure(
                pActor.data.id, pReason, now);
            if (retry.ShouldRetry)
            {
                AWPathfindingBootstrap.PathDiagnostics.OnBoatRetry();
                if (RetryContexts.TryGetValue(pActor.data.id, out RetryContext context))
                    RetryContexts[pActor.data.id] = new RetryContext(
                        context.TargetTileId, context.Options,
                        context.GenerationId, pPending: true,
                        retry.DueTime, context.SubmittedAt,
                        pAcceptedNoProgressAt: -1d,
                        context.WorkClass,
                        pNextPollAt: retry.DueTime);
                pActor.timer_action = retry.DelaySeconds;
                return;
            }
            ResetAfterTerminalFailure(pActor);
        }

        private static void MarkCentralRetryPending(Actor pActor,
            float pDelaySeconds)
        {
            if (pActor?.data == null) return;
            if (RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context))
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double delay = Math.Max(0.05d, pDelaySeconds);
                RetryContexts[pActor.data.id] = new RetryContext(
                    context.TargetTileId, context.Options,
                    context.GenerationId, pPending: true,
                    now + delay, context.SubmittedAt,
                    pAcceptedNoProgressAt: -1d, context.WorkClass,
                    pNextPollAt: now + delay);
                pActor.timer_action = (float)delay;
            }
            else
            {
                pActor.timer_action = Math.Max(0.05f, pDelaySeconds);
            }
        }

        private static bool TryStartNarrowWaterRecovery(Actor pActor,
            AWPathFailureReason pReason)
        {
            if (pReason != AWPathFailureReason.Unreachable ||
                pActor?.data == null ||
                !RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context)) return false;
            bool military = false;
            try { military = pActor.isWarrior() || pActor.hasArmy(); }
            catch { }
            bool isBoat = pActor.asset?.is_boat == true;
            bool waterCreature = false;
            bool damagedByOcean = false;
            try
            {
                waterCreature = pActor.isWaterCreature();
                damagedByOcean = pActor.isDamagedByOcean();
            }
            catch { }
            if (!AWNarrowWaterRecoveryRules.CanStart(military, isBoat,
                    waterCreature, damagedByOcean,
                    context.Options.BoundedMilitaryWater)) return false;

            WorldTile target = pActor.tile_target;
            if (target?.data == null ||
                target.data.tile_id != context.TargetTileId) return false;
            if (target?.Type == null || target.Type.liquid ||
                target.Type.ocean) return false;
            AWPathRequestOptions recoveryOptions = context.Options
                .WithBoundedMilitaryWater(
                    AWNarrowWaterRecoveryRules.MaximumConsecutiveWaterTiles);
            return SubmitCore(pActor, target, recoveryOptions,
                       pIsRecovery: true,
                       pRetainedWorkClass: context.WorkClass) !=
                   ExecuteEvent.False;
        }

        private static void ResetAfterTerminalFailure(Actor pActor)
        {
            if (pActor?.data == null) return;
            bool continueBehaviour =
                AWPathLifecycleRules.ShouldContinueBehaviourAfterTerminalFailure(
                    pActor.ai?.task?.id);
            RetryContexts.TryRemove(pActor.data.id, out _);
            TerminalPolls.TryRemove(pActor.data.id, out _);
            OwnedActors.Remove(pActor.data.id);
            AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
            pActor.clearOldPath();
            pActor.clearTileTarget();
            pActor.beh_tile_target = continueBehaviour
                ? pActor.current_tile
                : null;
            TrySetNotMoving(pActor);
            pActor.timer_action = 0f;
            if (!continueBehaviour)
            {
                try { pActor.cancelAllBeh(); }
                catch { }
            }
        }

        private static bool TryStartDueRetry(Actor pActor)
        {
            if (pActor?.data == null || !RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context) || !context.Pending) return false;
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder != null && finder.IsRecoveryPending(pActor.data.id))
            {
                TrySetNotMoving(pActor);
                pActor.timer_action = 0.25f;
                return true;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < context.DueTime)
            {
                TrySetNotMoving(pActor);
                pActor.timer_action = (float)Math.Max(0.01d, context.DueTime - now);
                return true;
            }
            WorldTile target = pActor.tile_target;
            if (target?.data == null ||
                target.data.tile_id != context.TargetTileId)
            {
                Cancel(pActor, AWPathFailureReason.CancelledByNewRequest);
                TrySetNotMoving(pActor);
                return true;
            }
            RetryContexts[pActor.data.id] = new RetryContext(
                context.TargetTileId, context.Options, context.GenerationId,
                pPending: false, 0d, context.SubmittedAt,
                pAcceptedNoProgressAt: -1d,
                context.WorkClass, pNextPollAt: now);
            if (SubmitCore(pActor, target, context.Options,
                    pIsRecovery: true,
                    pRetainedWorkClass: context.WorkClass) ==
                ExecuteEvent.False)
                HandleFailure(pActor, AWPathFailureReason.Timeout);
            return true;
        }

        private static void ReleaseStaleOwnership(Actor pActor)
        {
            if (pActor?.data == null) return;
            long actorId = pActor.data.id;
            RetryContexts.TryRemove(actorId, out _);
            TerminalPolls.TryRemove(actorId, out _);
            OwnedActors.Remove(actorId);
            AWPathfindingBootstrap.RecoveryManager.Clear(actorId);
            pActor.clearOldPath();
            pActor.clearTileTarget();
            TrySetNotMoving(pActor);
            pActor.timer_action = 0f;
        }

        private static void MarkRetryProgress(long pActorId)
        {
            if (!RetryContexts.TryGetValue(pActorId, out RetryContext context)) return;
            double now = Time.realtimeSinceStartupAsDouble;
            RetryContexts[pActorId] = new RetryContext(context.TargetTileId,
                context.Options, context.GenerationId, pPending: false, 0d,
                now, pAcceptedNoProgressAt: now, context.WorkClass,
                pNextPollAt: now);
        }

        private static void TrySetNotMoving(Actor pActor)
        {
            if (pActor == null) return;
            try
            {
                bool batchExists = pActor.batch != null;
                bool queueExists = batchExists &&
                                   pActor.batch.c_update_movement != null;
                if (batchExists && queueExists)
                    pActor.setNotMoving();
                else
                    pActor._is_moving = false;
            }
            catch
            {
                try { pActor._is_moving = false; }
                catch { }
            }
        }

        private static float BoundaryDistance(float pDistanceSquared)
        {
            if (pDistanceSquared <= 0f) return 0f;
            if (pDistanceSquared > 0.999f && pDistanceSquared < 1.001f) return 1f;
            if (pDistanceSquared > 1.999f && pDistanceSquared < 2.001f)
                return DiagonalTileDistance;
            return Mathf.Sqrt(pDistanceSquared);
        }

        internal static AWActorTraversalProfile CaptureProfile(Actor pActor)
        {
            bool immune = pActor.isImmuneToFire();
            float staminaRegen = SimGlobals.m == null
                ? 0.5f
                : SimGlobals.m.stamina_change / Math.Max(0.01f, SimGlobals.m.interval_stamina);
            bool military = false;
            try { military = pActor.isWarrior() || pActor.hasArmy(); }
            catch { }
            return new AWActorTraversalProfile(pActor.isFlying(), pActor.asset.is_boat,
                pActor.isWaterCreature(), pActor.asset.force_land_creature, immune,
                pActor.isDamagedByOcean(), pActor.asset.die_in_lava && !immune,
                pActor.hasStatus("burning"), pActor.isInLiquid(), pActor.isInWater(),
                pActor.getHealth(), pActor.getMaxHealth(), pActor.getStamina(),
                pActor.getMaxStamina(), pActor.stats?["speed"] ?? 5f,
                pActor.getWaterDamage() * 3.333f, staminaRegen, military);
        }

        private static AWPathWorkClass ClassifyWork(Actor pActor,
            long pActorId)
        {
            if (pActor?.data == null) return AWPathWorkClass.Ambient;
            bool warrior = false;
            bool hasArmy = false;
            bool boat = false;
            bool schoolJourney = string.Equals(pActor.ai?.task?.id,
                HistoricalSchoolContent.EducationTravelTaskId,
                StringComparison.Ordinal);
            try
            {
                warrior = pActor.isWarrior();
                hasArmy = pActor.hasArmy();
                boat = pActor.asset?.is_boat == true ||
                       pActor.is_inside_boat;
            }
            catch
            {
            }
            return AWPathWorkClassRules.Classify(warrior, hasArmy, boat,
                TransportContexts.ContainsKey(pActorId), schoolJourney);
        }

        private enum SlowMoveReason
        {
            None,
            Boat,
            TileStepAction,
            FungiLaw,
            FlowerNectarLaw,
            PlantLaw
        }

        private readonly struct RetryContext
        {
            public RetryContext(int pTargetTileId, AWPathRequestOptions pOptions,
                int pGenerationId, bool pPending, double pDueTime,
                double pSubmittedAt, double pAcceptedNoProgressAt,
                AWPathWorkClass pWorkClass, double pNextPollAt)
            {
                TargetTileId = pTargetTileId;
                Options = pOptions;
                GenerationId = pGenerationId;
                Pending = pPending;
                DueTime = pDueTime;
                SubmittedAt = pSubmittedAt;
                AcceptedNoProgressAt = pAcceptedNoProgressAt;
                WorkClass = pWorkClass;
                NextPollAt = pNextPollAt;
            }

            public int TargetTileId { get; }
            public AWPathRequestOptions Options { get; }
            public int GenerationId { get; }
            public bool Pending { get; }
            public double DueTime { get; }
            public double SubmittedAt { get; }
            public double AcceptedNoProgressAt { get; }
            public AWPathWorkClass WorkClass { get; }
            public double NextPollAt { get; }
        }

        private readonly struct TransportContext
        {
            public TransportContext(Actor pActor, int pTargetTileId,
                AWPathRequestOptions pOptions, double pStartedAt,
                bool pObservedInsideBoat, double pNextPollAt,
                AWDockPassengerState pState = AWDockPassengerState.WaitingBoat,
                long pEntryDockId = -1L, long pExitDockId = -1L)
            {
                Actor = pActor;
                TargetTileId = pTargetTileId;
                Options = pOptions;
                StartedAt = pStartedAt;
                ObservedInsideBoat = pObservedInsideBoat;
                NextPollAt = pNextPollAt;
                State = pState;
                EntryDockId = pEntryDockId;
                ExitDockId = pExitDockId;
            }

            public Actor Actor { get; }
            public int TargetTileId { get; }
            public AWPathRequestOptions Options { get; }
            public double StartedAt { get; }
            public bool ObservedInsideBoat { get; }
            public double NextPollAt { get; }
            public AWDockPassengerState State { get; }
            public long EntryDockId { get; }
            public long ExitDockId { get; }

            public TransportContext WithObservedInsideBoat()
            {
                return new TransportContext(Actor, TargetTileId, Options, StartedAt,
                    pObservedInsideBoat: true, pNextPollAt: NextPollAt,
                    pState: State, pEntryDockId: EntryDockId,
                    pExitDockId: ExitDockId);
            }

            public TransportContext WithNextPoll(double pNextPollAt)
            {
                return new TransportContext(Actor, TargetTileId, Options,
                    StartedAt, ObservedInsideBoat, pNextPollAt, State,
                    EntryDockId, ExitDockId);
            }

            public TransportContext WithState(AWDockPassengerState pState)
            {
                return new TransportContext(Actor, TargetTileId, Options,
                    StartedAt, ObservedInsideBoat, NextPollAt, pState,
                    EntryDockId, ExitDockId);
            }
        }
    }
}
