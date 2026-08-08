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
        private static readonly ConcurrentDictionary<long, TransportContext> TransportContexts =
            new ConcurrentDictionary<long, TransportContext>();
        private static readonly AWPathOwnershipIndex OwnedActors =
            new AWPathOwnershipIndex();

        internal static int ActorGateCount => 0;

        public static ExecuteEvent Submit(Actor pActor, WorldTile pTarget, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions)
        {
            if (pActor?.data == null) return ExecuteEvent.False;
            return SubmitCore(pActor, pTarget,
                new AWPathRequestOptions(pPathOnWater, pWalkOnBlocks,
                    pWalkOnLava, pLimitPathfindingRegions),
                pIsRecovery: false);
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
            bool wasMoving = pActor.is_moving;
            long terrainRevision = AWPathfindingBootstrap.Cache.SourceRevision;
            long worldGeneration = AWAsyncRuntime.WorldGeneration;
            bool insideBoat = pActor.is_inside_boat;
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            bool reused;
            try
            {
                var reuseKey = new AWPathReuseKey(actorId,
                    AWPathfindingBootstrap.Cache.StartRegion(
                        pActor.current_tile.data.tile_id),
                    new AWPathRequestKey(targetTileId,
                        pOptions.PathOnWater, pOptions.WalkOnBlocks,
                        pOptions.WalkOnLava,
                        pOptions.LimitPathfindingRegions,
                        pOptions.BoundedMilitaryWater,
                        pOptions.MaximumConsecutiveWaterTiles),
                    terrainRevision, worldGeneration, insideBoat);
                reused = finder.TryReuse(reuseKey);
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
                    bool physicalTransportAvailable =
                        AWDockTransportService.TryResolveRoute(
                            pActor.current_tile, pTarget,
                            out AWDockRouteCandidate dockRoute);
                    var request = new AWPathRequest(actorId,
                        pActor.current_tile.data.tile_id, targetTileId,
                        pOptions, profile, generation, now,
                        workClass, terrainRevision, worldGeneration,
                        insideBoat, physicalTransportAvailable,
                        dockRoute.EstimatedRouteTiles);
                    diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                    bool accepted;
                    try
                    {
                        accepted = finder.Request(request,
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
                if (AWPathLifecycleRules.ShouldClearPreviousPathAfterSubmission(
                        accepted: true, reused))
                    pActor.clearOldPath();
                pActor.setTileTarget(pTarget);
                if (AWPathLifecycleRules.ShouldResetActorAfterPathSubmission(
                        accepted: true, reused, wasMoving))
                {
                    pActor.next_step_position = pActor.current_tile.posV3;
                    TrySetNotMoving(pActor);
                }
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
            pFinder?.Cancel(actorId,
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
            if (pActor?.data == null) return;
            UpdateCore(pActor);
        }

        private static void UpdateCore(Actor pActor)
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
                poll = finder.OpenReadyCursor(pActor.data.id, out cursor);
            HandlePoll(pActor, poll, ref cursor, pHandleNoRequest: true);
        }

        private static bool HandlePoll(Actor pActor, AWPathPollResult pPoll,
            ref AWPathFinder.ReadyPathCursor pCursor, bool pHandleNoRequest)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null) return false;
            if (pPoll.Kind == AWPathPollKind.StepReady)
            {
                if (TryMove(pActor, pPoll.Step))
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
                    finder.Cancel(pActor.data.id, AWPathFailureReason.UnsafeStep);
                    pCursor = default;
                    HandleFailure(pActor, AWPathFailureReason.UnsafeStep);
                }

                if (pActor.tile_target == null)
                {
                    AWArmyMarchService.OnPathEnded(pActor);
                    finder.Cancel(pActor.data.id, AWPathFailureReason.CancelledByNewRequest);
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

            pFinder.Cancel(pActor.data.id, AWPathFailureReason.Timeout);
            AWArmyMarchService.OnPathEnded(pActor);
            HandleFailure(pActor, AWPathFailureReason.Timeout);
            return true;
        }

        public static bool IsUsing(Actor pActor)
        {
            return pActor?.data != null && IsUsingCore(pActor);
        }

        private static bool IsUsingCore(Actor pActor)
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

        internal static bool ShouldPollNow(Actor pActor)
        {
            if (pActor?.data == null) return true;
            return ShouldPollNowCore(pActor);
        }

        private static bool ShouldPollNowCore(Actor pActor)
        {
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
            internal AWPreparedPathMovement(bool pVanilla)
            {
                Vanilla = pVanilla;
                Poll = default;
                Cursor = default;
            }

            internal AWPreparedPathMovement(AWPathPollResult pPoll,
                AWPathFinder.ReadyPathCursor pCursor)
            {
                Vanilla = false;
                Poll = pPoll;
                Cursor = pCursor;
            }

            internal bool Vanilla { get; }
            internal AWPathPollResult Poll { get; }
            internal AWPathFinder.ReadyPathCursor Cursor { get; }
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
            if (pActor == null || pActor.data == null)
                return AWParallelPathMovementResult.NoPath;
            // AW3 army marching updates a shared, non-thread-safe formation
            // ledger from OnLeaderPathStep. Keep military actors on the
            // ordered simulation commit path; civilians retain Cultiway's
            // parallel-safe movement path.
            if (HasArmyMarchState(pActor))
            {
                pPrepared = new AWPreparedPathMovement(pVanilla: true);
                return AWParallelPathMovementResult.RequiresSerial;
            }
            if (pActor.isFollowingLocalPath() ||
                pActor.current_path_global != null)
            {
                pPrepared = new AWPreparedPathMovement(pVanilla: true);
                return AWParallelPathMovementResult.RequiresSerial;
            }

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

        internal static bool CommitPreparedPathMovement(Actor pActor,
            AWPreparedPathMovement pPrepared)
        {
            if (pActor == null || pActor.data == null)
                return false;
            if (pPrepared.Vanilla)
            {
                pActor.updatePathMovement();
                return true;
            }

            AWPathFinder.ReadyPathCursor cursor = pPrepared.Cursor;
            HandlePoll(pActor, pPrepared.Poll, ref cursor,
                pHandleNoRequest: true);
            return true;
        }

        internal static AWParallelSmoothMovementResult
            TryRunParallelSafeSmoothMovement(Actor pActor, float pElapsed,
                out AWPreparedSmoothMovement pPrepared)
        {
            pPrepared = default;
            if (pActor == null || pActor.data == null ||
                pActor._update_done || pActor.is_immovable)
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
            for (int i = 0; i < AWPathLifecycleRules.MaximumSmoothPathStepsPerUpdate; i++)
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
            if (pActor == null || pActor.data == null) return;
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
                UpdateSmoothMovementCore(pActor, pElapsed,
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
            AWPathPollResult poll;
            if (pCursor.IsValid)
            {
                poll = pCursor.Poll();
            }
            else if (finder != null)
            {
                poll = finder.OpenReadyCursor(
                    pActor.data.id, out pCursor);
            }
            else
            {
                pPrepared = new AWPreparedSmoothMovement(
                    AWPreparedSmoothMovementKind.StopMovement,
                    pWalkedDistance);
                return false;
            }
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
                return pActor?.army?.data != null;
            }
            catch
            {
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
            CancelCore(pActor, pReason);
        }

        private static void CancelCore(Actor pActor,
            AWPathFailureReason pReason)
        {
            AWArmyMarchService.OnPathEnded(pActor);
            CancelTransport(pActor);
            AWPathfindingBootstrap.Finder?.Cancel(pActor.data.id, pReason);
            AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
            RetryContexts.TryRemove(pActor.data.id, out _);
            TerminalPolls.TryRemove(pActor.data.id, out _);
            OwnedActors.Remove(pActor.data.id);
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
            OwnedActors.Clear();
            AWArmyMarchService.ClearLegacy();
        }

        public static void UpdateSmoothMovement(Actor pActor, float pElapsed,
            float pWalkedDistance = 0f)
        {
            if (pActor?.data == null) return;
            UpdateSmoothMovementCore(pActor, pElapsed, pWalkedDistance);
        }

        private static void UpdateSmoothMovementCore(Actor pActor,
            float pElapsed, float pWalkedDistance)
        {
            if (pActor?.asset == null) return;
            bool actorAlive = false;
            try { actorAlive = pActor.data != null && pActor.isAlive(); }
            catch { }
            bool batchExists = pActor.batch != null;
            bool queueExists = batchExists &&
                               pActor.batch.c_update_movement != null;
            if (!AWPathLifecycleRules.HasUsableMovementBatch(
                    pActor.data != null, actorAlive, batchExists,
                    queueExists))
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
                    poll = finder == null
                        ? new AWPathPollResult(AWPathPollKind.NoRequest)
                        : finder.OpenReadyCursor(pActor.data.id, out pCustomPathCursor);
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
                HasPathRelevantTerrainChanged(pStep, tile)) return false;
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

            if (useFastMove)
            {
                if (!FastMoveTo(pActor, tile, adjacentStep)) return false;
                AWPathfindingBootstrap.PathDiagnostics.OnFastStep();
            }
            else if (CanReplayMoveToSideEffects(slowMoveReason))
            {
                if (!FastMoveToWithMoveToSideEffects(pActor, tile,
                        adjacentStep)) return false;
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
            if (!AWPathLifecycleRules.HasUsableMovementBatch(actorExists,
                    actorAlive, batchExists, movementQueueExists) ||
                pTile?.data == null || pActor.current_tile?.data == null)
                return false;
            if (!pActor._is_moving)
            {
                pActor._is_moving = true;
                pActor.batch.c_update_movement.Add(pActor);
            }

            pActor._next_step_tile = pTile;
            if (pAdjacentStep)
                SetCurrentTile(pActor, pTile);
            else if (Toolbox.SquaredDistTile(pActor.current_tile, pTile) > 4f)
                pActor.dirty_current_tile = true;
            else
                SetCurrentTile(pActor, pTile);

            return true;
        }

        private static bool HasPathRelevantTerrainChanged(AWPathStep pStep,
            WorldTile pTile)
        {
            AWPathTileFlags current = CaptureRuntimeTileFlags(pTile) &
                                      AWPathTileFlagsExtensions.RuntimeRelevant;
            AWPathTileFlags planned = pStep.PlannedTileFlags &
                                      AWPathTileFlagsExtensions.RuntimeRelevant;
            return current != planned;
        }

        private static AWPathTileFlags CaptureRuntimeTileFlags(WorldTile pTile)
        {
            if (pTile?.data == null) return AWPathTileFlags.None;
            AWPathTileFlags flags = AWPathTileFlags.Exists;
            TileTypeBase type = pTile.Type;
            if (type != null)
            {
                flags |= AWPathTileFlags.HasType;
                if (type.block) flags |= AWPathTileFlags.Block;
                if (type.lava) flags |= AWPathTileFlags.Lava;
                if (type.ocean) flags |= AWPathTileFlags.Ocean;
                if (type.liquid) flags |= AWPathTileFlags.Liquid;
                if (type.damage_units) flags |= AWPathTileFlags.DamageUnits;
            }
            try
            {
                if (pTile.isOnFire()) flags |= AWPathTileFlags.Fire;
            }
            catch
            {
                // World teardown may make the live fire collection unavailable.
            }
            return flags;
        }

        private static void SetCurrentTile(
            Actor pActor,
            WorldTile pTile)
        {
            WorldTile previousTile = pActor.current_tile;
            if (ReferenceEquals(previousTile, pTile))
            {
                return;
            }

            pActor.current_tile = pTile;
            AWActorZoneMembershipDirtyIndex.Mark(
                pActor,
                AWActorZoneDirtyKind.Spatial);
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
                    pActor.is_inside_boat) ||
                !AWDockTransportService.TryResolveRoute(pActor.current_tile,
                    pTarget, out _)) return false;
            if (!RetryContexts.TryGetValue(pActor.data.id, out RetryContext retry)) return false;
            TaxiManager.newRequest(pActor, pTarget);
            if (TaxiManager.getRequestForActor(pActor) == null) return false;
            AWPathfindingBootstrap.PathDiagnostics.OnDockRequest();

            double now = Time.realtimeSinceStartupAsDouble;
            long actorId = pActor.data.id;
            AWDockTransportService.TryResolveRoute(pActor.current_tile, pTarget,
                out AWDockRouteCandidate route);
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
            AWPathfindingBootstrap.Finder?.Cancel(pActor.data.id, pReason);
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
                pActor.getWaterDamage() * 3.333f, staminaRegen, military,
                pHasFastSwimming: pActor.hasTag("fast_swimming"));
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
