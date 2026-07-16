using System;
using System.Collections.Generic;
using ai;
using life.taxi;
using UnityEngine;

namespace AncientWarfare3.core.pathfinding
{
    internal static class AWPathMovementBridge
    {
        private const float DiagonalTileDistance = 1.41421356237f;
        private const double TransportWaitTimeoutSeconds = 120d;
        private static readonly Dictionary<long, RetryContext> RetryContexts =
            new Dictionary<long, RetryContext>();
        private static readonly Dictionary<long, AWPathPollResult> TerminalPolls =
            new Dictionary<long, AWPathPollResult>();
        private static readonly Dictionary<long, TransportContext> TransportContexts =
            new Dictionary<long, TransportContext>();
        private static readonly Queue<long> TransportQueue = new Queue<long>();
        private static readonly HashSet<long> QueuedTransportActors = new HashSet<long>();

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
            if (TransportContexts.TryGetValue(pActor.data.id, out TransportContext transport))
            {
                if (transport.TargetTileId == pTarget.data.tile_id &&
                    transport.Options.Equals(pOptions)) return ExecuteEvent.True;
                CancelTransport(pActor);
            }
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
            if (TransportContexts.ContainsKey(pActor.data.id))
            {
                ProcessTransport(pActor);
                return;
            }
            AWPathFinder.ReadyPathCursor cursor = default;
            AWPathPollResult poll;
            if (TerminalPolls.TryGetValue(pActor.data.id, out poll))
                TerminalPolls.Remove(pActor.data.id);
            else
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
                    finder.Cancel(pActor.data.id, AWPathFailureReason.CancelledByNewRequest);
                    pCursor = default;
                    pActor.stopMovement();
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                }
                return true;
            }

            if (!pHandleNoRequest)
            {
                if (pPoll.Kind != AWPathPollKind.Waiting) return false;
                SetWaiting(pActor);
                return true;
            }

            switch (pPoll.Kind)
            {
                case AWPathPollKind.Waiting:
                    SetWaiting(pActor);
                    return true;
                case AWPathPollKind.Completed:
                    pActor.stopMovement();
                    RetryContexts.Remove(pActor.data.id);
                    AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                    return true;
                case AWPathPollKind.Failed:
                    pActor.setNotMoving();
                    HandleFailure(pActor, pPoll.FailureReason);
                    return true;
                case AWPathPollKind.Cancelled:
                    pActor.setNotMoving();
                    AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
                    return true;
                default:
                    if (!TryStartDueRetry(pActor)) pActor.setNotMoving();
                    return true;
            }
        }

        private static void SetWaiting(Actor pActor)
        {
            pActor.setNotMoving();
            pActor.next_step_position = pActor.current_tile?.posV3 ?? pActor.next_step_position;
            pActor.timer_action = 0.05f;
        }

        public static bool IsUsing(Actor pActor)
        {
            if (pActor?.data == null || AWPathfindingBootstrap.Finder == null) return false;
            if (TransportContexts.ContainsKey(pActor.data.id)) return true;
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
            CancelTransport(pActor);
            AWPathfindingBootstrap.Finder?.Cancel(pActor.data.id, pReason);
            AWPathfindingBootstrap.RecoveryManager.Clear(pActor.data.id);
            RetryContexts.Remove(pActor.data.id);
            TerminalPolls.Remove(pActor.data.id);
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
            TransportQueue.Clear();
            QueuedTransportActors.Clear();
            RetryContexts.Clear();
            TerminalPolls.Clear();
        }

        public static void ProcessTransports(int pBudget = 64)
        {
            int budget = Math.Max(0, pBudget);
            while (budget-- > 0 && TransportQueue.Count > 0)
            {
                long actorId = TransportQueue.Dequeue();
                QueuedTransportActors.Remove(actorId);
                if (!TransportContexts.TryGetValue(actorId, out TransportContext context))
                    continue;
                ProcessTransport(context.Actor);
                if (TransportContexts.ContainsKey(actorId) &&
                    QueuedTransportActors.Add(actorId))
                    TransportQueue.Enqueue(actorId);
            }
        }

        public static void UpdateSmoothMovement(Actor pActor, float pElapsed,
            float pWalkedDistance = 0f)
        {
            if (pActor?.asset == null) return;
            float movementBudget = pActor._current_combined_movement_speed * pElapsed;
            bool canFlip = pActor.asset.can_flip && pActor.checkFlip();
            AWPathFinder.ReadyPathCursor customPathCursor = default;
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
                Toolbox.SquaredDistTile(pActor.current_tile, tile) > 2) return false;
            if (pActor.asset.is_boat && !tile.isGoodForBoat()) return false;
            if (tile.Type.block && !pActor.ignoresBlocks()) return false;
            if (tile.Type.lava && pActor.asset.die_in_lava && !pActor.isImmuneToFire()) return false;
            if (tile.Type.ocean && pActor.isDamagedByOcean() && !pActor.isInLiquid()) return false;

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
                FastMoveTo(pActor, tile, adjacentStep);
                AWPathfindingBootstrap.PathDiagnostics.OnFastStep();
            }
            else if (CanReplayMoveToSideEffects(slowMoveReason))
            {
                FastMoveToWithMoveToSideEffects(pActor, tile, adjacentStep);
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

        private static void FastMoveTo(Actor pActor, WorldTile pTile, bool pAdjacentStep)
        {
            SetMoveStepTile(pActor, pTile, pAdjacentStep);
            pActor.next_step_position = new Vector2(pTile.posV3.x, pTile.posV3.y);
        }

        private static void FastMoveToWithMoveToSideEffects(Actor pActor, WorldTile pTile,
            bool pAdjacentStep)
        {
            if (!pActor.has_attack_target && pActor.current_tile != null && pTile.isOnFire() &&
                !pActor.current_tile.isOnFire() && !pActor.isImmuneToFire())
            {
                pActor.cancelAllBeh();
                return;
            }

            SetMoveStepTile(pActor, pTile, pAdjacentStep);
            ApplyStepActionForCurrentTile(pActor);
            pActor.next_step_position = new Vector2(pTile.posV3.x, pTile.posV3.y);
        }

        private static void SetMoveStepTile(Actor pActor, WorldTile pTile, bool pAdjacentStep)
        {
            if (!pActor._is_moving)
            {
                pActor._is_moving = true;
                pActor.batch.c_update_movement.Add(pActor);
            }

            pActor._next_step_tile = pTile;
            if (pAdjacentStep)
                pActor.current_tile = pTile;
            else if (Toolbox.SquaredDistTile(pActor.current_tile, pTile) > 4f)
                pActor.dirty_current_tile = true;
            else
                pActor.current_tile = pTile;
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
            if (pActor.is_inside_boat || pActor.current_tile.isSameIsland(pTarget)) return false;
            if (!RetryContexts.TryGetValue(pActor.data.id, out RetryContext retry)) return false;
            TaxiManager.newRequest(pActor, pTarget);
            if (TaxiManager.getRequestForActor(pActor) == null) return false;

            double now = World.world?.getCurSessionTime() ?? 0d;
            long actorId = pActor.data.id;
            TransportContexts[actorId] = new TransportContext(pActor, pTarget.data.tile_id,
                retry.Options, now, pObservedInsideBoat: false);
            if (QueuedTransportActors.Add(actorId)) TransportQueue.Enqueue(actorId);
            pActor.setNotMoving();
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

            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || context.TargetTileId < 0 ||
                context.TargetTileId >= tiles.Length || tiles[context.TargetTileId] == null)
            {
                FailTransport(pActor, AWPathFailureReason.TransportFailed, pCancelTaxi: true);
                return;
            }
            WorldTile target = tiles[context.TargetTileId];
            if (pActor.is_inside_boat)
            {
                if (!context.ObservedInsideBoat)
                    TransportContexts[pActor.data.id] = context.WithObservedInsideBoat();
                return;
            }

            TaxiRequest request = TaxiManager.getRequestForActor(pActor);
            if (request != null)
            {
                double now = World.world?.getCurSessionTime() ?? 0d;
                if (now - context.StartedAt < TransportWaitTimeoutSeconds) return;
                FailTransport(pActor, AWPathFailureReason.TransportFailed, pCancelTaxi: true);
                return;
            }

            if (pActor.current_tile != null && pActor.current_tile.isSameIsland(target))
            {
                RemoveTransportContext(pActor.data.id);
                TerminalPolls.Remove(pActor.data.id);
                AWPathfindingBootstrap.RecoveryManager.OnProgress(pActor.data.id);
                if (SubmitCore(pActor, target, context.Options, pIsRecovery: true) ==
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
            TransportContexts.Remove(pActorId);
        }

        private static void HandleFailure(Actor pActor, AWPathFailureReason pReason)
        {
            pActor.setNotMoving();
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

        private readonly struct TransportContext
        {
            public TransportContext(Actor pActor, int pTargetTileId,
                AWPathRequestOptions pOptions, double pStartedAt, bool pObservedInsideBoat)
            {
                Actor = pActor;
                TargetTileId = pTargetTileId;
                Options = pOptions;
                StartedAt = pStartedAt;
                ObservedInsideBoat = pObservedInsideBoat;
            }

            public Actor Actor { get; }
            public int TargetTileId { get; }
            public AWPathRequestOptions Options { get; }
            public double StartedAt { get; }
            public bool ObservedInsideBoat { get; }

            public TransportContext WithObservedInsideBoat()
            {
                return new TransportContext(Actor, TargetTileId, Options, StartedAt,
                    pObservedInsideBoat: true);
            }
        }
    }
}
