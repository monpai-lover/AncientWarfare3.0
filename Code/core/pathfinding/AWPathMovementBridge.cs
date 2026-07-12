using System;
using System.Collections.Generic;
using ai;
using UnityEngine;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathMovementBridge
    {
        private const float DiagonalTileDistance = 1.41421356237f;
        private static readonly Dictionary<long, RetryContext> RetryContexts =
            new Dictionary<long, RetryContext>();
        private static readonly Dictionary<long, AWPathPollResult> TerminalPolls =
            new Dictionary<long, AWPathPollResult>();

        public static ExecuteEvent Submit(Actor pActor, WorldTile pTarget, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions)
        {
            return SubmitCore(pActor, pTarget, new AWPathRequestOptions(pPathOnWater,
                pWalkOnBlocks, pWalkOnLava, pLimitPathfindingRegions), pIsRecovery: false);
        }

        private static ExecuteEvent SubmitCore(Actor pActor, WorldTile pTarget,
            AWPathRequestOptions pOptions, bool pIsRecovery)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null || pTarget?.data == null ||
                pActor.current_tile?.data == null) return ExecuteEvent.False;
            if (pActor.current_tile == pTarget)
            {
                finder.Cancel(pActor.data.id, AWPathFailureReason.CancelledByNewRequest);
                RetryContexts.Remove(pActor.data.id);
                AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
                pActor.clearOldPath();
                pActor.setTileTarget(pTarget);
                pActor.moveTo(pTarget);
                return ExecuteEvent.True;
            }

            AWTraversalGeneration generation = AWPathfindingBootstrap.Cache.Pin();
            if (generation == null) return ExecuteEvent.False;
            try
            {
                if (!pIsRecovery && RetryContexts.TryGetValue(pActor.data.id,
                        out RetryContext previous) &&
                    (previous.TargetTileId != pTarget.data.tile_id ||
                     !previous.Options.Equals(pOptions)))
                    AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
                RetryContexts[pActor.data.id] = new RetryContext(pTarget.data.tile_id,
                    pOptions, pPending: false, 0d);
                var request = new AWPathRequest(pActor.data.id, pActor.current_tile.data.tile_id,
                    pTarget.data.tile_id, pOptions, CaptureProfile(pActor), generation,
                    World.world?.getCurSessionTime() ?? 0d);
                pActor.clearOldPath();
                pActor.setTileTarget(pTarget);
                pActor.next_step_position = pActor.current_tile.posV3;
                pActor.setNotMoving();
                return finder.Request(request, out _) ? ExecuteEvent.True : ExecuteEvent.False;
            }
            finally
            {
                generation.Dispose();
            }
        }

        public static void Update(Actor pActor)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null) return;
            AWPathPollResult poll;
            if (TerminalPolls.TryGetValue(pActor.data.id, out poll))
                TerminalPolls.Remove(pActor.data.id);
            else
                poll = finder.Poll(pActor.data.id);
            switch (poll.Kind)
            {
                case AWPathPollKind.StepReady:
                    if (!TryMove(pActor, poll.Step))
                    {
                        finder.Cancel(pActor.data.id, AWPathFailureReason.UnsafeStep);
                        pActor.cancelAllBeh();
                        return;
                    }
                    finder.Consume(pActor.data.id);
                    MarkRetryProgress(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                    return;
                case AWPathPollKind.Waiting:
                    pActor.setNotMoving();
                    pActor.next_step_position = pActor.current_tile?.posV3 ?? pActor.next_step_position;
                    pActor.timer_action = 0.05f;
                    return;
                case AWPathPollKind.Completed:
                    pActor.stopMovement();
                    RetryContexts.Remove(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                    return;
                case AWPathPollKind.Failed:
                    pActor.setNotMoving();
                    HandleFailure(pActor, poll.FailureReason);
                    return;
                case AWPathPollKind.Cancelled:
                    pActor.setNotMoving();
                    AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
                    return;
                default:
                    if (!TryStartDueRetry(pActor)) pActor.setNotMoving();
                    return;
            }
        }

        public static bool IsUsing(Actor pActor)
        {
            if (pActor?.data == null || AWPathfindingBootstrap.Finder == null) return false;
            if (TerminalPolls.ContainsKey(pActor.data.id)) return true;
            AWPathPollResult current = AWPathfindingBootstrap.Finder.Poll(pActor.data.id);
            AWPathPollKind kind = current.Kind;
            if (kind == AWPathPollKind.Completed || kind == AWPathPollKind.Failed ||
                kind == AWPathPollKind.Cancelled)
            {
                TerminalPolls[pActor.data.id] = current;
                return true;
            }
            return kind == AWPathPollKind.Waiting || kind == AWPathPollKind.StepReady ||
                   RetryContexts.TryGetValue(pActor.data.id, out RetryContext retry) && retry.Pending;
        }

        public static void Cancel(Actor pActor, AWPathFailureReason pReason)
        {
            if (pActor?.data == null) return;
            AWPathfindingBootstrap.Finder?.Cancel(pActor.data.id, pReason);
            AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
            RetryContexts.Remove(pActor.data.id);
            TerminalPolls.Remove(pActor.data.id);
        }

        public static void Clear()
        {
            RetryContexts.Clear();
            TerminalPolls.Clear();
        }

        public static void UpdateSmoothMovement(Actor pActor, float pElapsed,
            float pWalkedDistance = 0f)
        {
            if (pActor?.asset == null) return;
            float movementBudget = pActor._current_combined_movement_speed * pElapsed;
            bool canFlip = pActor.asset.can_flip && pActor.checkFlip();
            for (int i = 0; i < 256; i++)
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
                if (pActor.isFollowingLocalPath() || pActor.current_path_global != null)
                    pActor.updatePathMovement();
                else if (pActor.tile_target != null)
                    Update(pActor);
                else
                    pActor.stopMovement();
                if (!pActor.is_moving) return;
                pWalkedDistance += walked;
            }
        }

        private static bool TryMove(Actor pActor, AWPathStep pStep)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || pStep.TileId < 0 || pStep.TileId >= tiles.Length) return false;
            WorldTile tile = tiles[pStep.TileId];
            if (tile?.Type == null || pActor.current_tile == null) return false;
            if ((pStep.Hazards & AWHazardFlags.Direct) == 0 &&
                Toolbox.SquaredDistTile(pActor.current_tile, tile) > 2) return false;
            if (pActor.asset.is_boat && !tile.isGoodForBoat()) return false;
            if (tile.Type.block && !pActor.ignoresBlocks()) return false;
            if (tile.Type.lava && pActor.asset.die_in_lava && !pActor.isImmuneToFire()) return false;
            if (tile.Type.ocean && pActor.isDamagedByOcean() && !pActor.isInLiquid()) return false;
            pActor.moveTo(tile);
            return true;
        }

        private static void HandleFailure(Actor pActor, AWPathFailureReason pReason)
        {
            double now = World.world?.getCurSessionTime() ?? 0d;
            AWPathRetryDecision retry = AWPathfindingBootstrap.RecoveryManager.OnFailure(
                pActor.data.id, pReason, now);
            if (retry.ShouldRetry)
            {
                if (RetryContexts.TryGetValue(pActor.data.id, out RetryContext context))
                    RetryContexts[pActor.data.id] = new RetryContext(context.TargetTileId,
                        context.Options, pPending: true, retry.DueTime);
                pActor.timer_action = retry.DelaySeconds;
                return;
            }
            RetryContexts.Remove(pActor.data.id);
            pActor.cancelAllBeh();
        }

        private static bool TryStartDueRetry(Actor pActor)
        {
            if (pActor?.data == null || !RetryContexts.TryGetValue(pActor.data.id,
                    out RetryContext context) || !context.Pending) return false;
            double now = World.world?.getCurSessionTime() ?? 0d;
            if (now < context.DueTime)
            {
                pActor.setNotMoving();
                pActor.timer_action = (float)Math.Max(0.01d, context.DueTime - now);
                return true;
            }
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || context.TargetTileId < 0 || context.TargetTileId >= tiles.Length ||
                tiles[context.TargetTileId] == null)
            {
                RetryContexts.Remove(pActor.data.id);
                pActor.cancelAllBeh();
                return true;
            }
            RetryContexts[pActor.data.id] = new RetryContext(context.TargetTileId,
                context.Options, pPending: false, 0d);
            if (SubmitCore(pActor, tiles[context.TargetTileId], context.Options,
                    pIsRecovery: true) == ExecuteEvent.False)
                HandleFailure(pActor, AWPathFailureReason.Timeout);
            return true;
        }

        private static void MarkRetryProgress(long pActorId)
        {
            if (!RetryContexts.TryGetValue(pActorId, out RetryContext context)) return;
            RetryContexts[pActorId] = new RetryContext(context.TargetTileId,
                context.Options, pPending: false, 0d);
        }

        private static float BoundaryDistance(float pDistanceSquared)
        {
            if (pDistanceSquared <= 0f) return 0f;
            if (pDistanceSquared > 0.999f && pDistanceSquared < 1.001f) return 1f;
            if (pDistanceSquared > 1.999f && pDistanceSquared < 2.001f)
                return DiagonalTileDistance;
            return Mathf.Sqrt(pDistanceSquared);
        }

        private static AWActorTraversalProfile CaptureProfile(Actor pActor)
        {
            bool immune = pActor.isImmuneToFire();
            float staminaRegen = SimGlobals.m == null
                ? 0.5f
                : SimGlobals.m.stamina_change / Math.Max(0.01f, SimGlobals.m.interval_stamina);
            return new AWActorTraversalProfile(pActor.isFlying(), pActor.asset.is_boat,
                pActor.isWaterCreature(), pActor.asset.force_land_creature, immune,
                pActor.isDamagedByOcean(), pActor.asset.die_in_lava && !immune,
                pActor.hasStatus("burning"), pActor.isInLiquid(), pActor.isInWater(),
                pActor.getHealth(), pActor.getMaxHealth(), pActor.getStamina(),
                pActor.getMaxStamina(), pActor.stats?["speed"] ?? 5f,
                pActor.getWaterDamage() * 3.333f, staminaRegen);
        }

        private readonly struct RetryContext
        {
            public RetryContext(int pTargetTileId, AWPathRequestOptions pOptions,
                bool pPending, double pDueTime)
            {
                TargetTileId = pTargetTileId;
                Options = pOptions;
                Pending = pPending;
                DueTime = pDueTime;
            }

            public int TargetTileId { get; }
            public AWPathRequestOptions Options { get; }
            public bool Pending { get; }
            public double DueTime { get; }
        }
    }
}
