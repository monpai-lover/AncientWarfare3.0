using System;
using ai;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathMovementBridge
    {
        public static ExecuteEvent Submit(Actor pActor, WorldTile pTarget, bool pPathOnWater,
            bool pWalkOnBlocks, bool pWalkOnLava, int pLimitPathfindingRegions)
        {
            AWPathFinder finder = AWPathfindingBootstrap.Finder;
            if (finder == null || pActor?.data == null || pTarget?.data == null ||
                pActor.current_tile?.data == null) return ExecuteEvent.False;
            if (pActor.current_tile == pTarget)
            {
                finder.Cancel(pActor.data.id, AWPathFailureReason.CancelledByNewRequest);
                pActor.clearOldPath();
                pActor.setTileTarget(pTarget);
                pActor.moveTo(pTarget);
                return ExecuteEvent.True;
            }

            AWTraversalGeneration generation = AWPathfindingBootstrap.Cache.Pin();
            if (generation == null) return ExecuteEvent.False;
            try
            {
                var options = new AWPathRequestOptions(pPathOnWater, pWalkOnBlocks,
                    pWalkOnLava, pLimitPathfindingRegions);
                var request = new AWPathRequest(pActor.data.id, pActor.current_tile.data.tile_id,
                    pTarget.data.tile_id, options, CaptureProfile(pActor), generation,
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
            AWPathPollResult poll = finder.Poll(pActor.data.id);
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
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                    return;
                case AWPathPollKind.Waiting:
                    pActor.setNotMoving();
                    pActor.next_step_position = pActor.current_tile?.posV3 ?? pActor.next_step_position;
                    pActor.timer_action = 0.05f;
                    return;
                case AWPathPollKind.Completed:
                    pActor.stopMovement();
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
                    pActor.setNotMoving();
                    return;
            }
        }

        public static bool IsUsing(Actor pActor)
        {
            if (pActor?.data == null || AWPathfindingBootstrap.Finder == null) return false;
            AWPathPollKind kind = AWPathfindingBootstrap.Finder.Poll(pActor.data.id).Kind;
            return kind == AWPathPollKind.Waiting || kind == AWPathPollKind.StepReady;
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
                pActor.timer_action = retry.DelaySeconds;
                return;
            }
            pActor.cancelAllBeh();
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
    }
}
