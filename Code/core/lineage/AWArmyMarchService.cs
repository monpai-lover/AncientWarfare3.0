using System;
using System.Collections.Generic;
using AncientWarfare3.core.pathfinding;
using AncientWarfare3.core.performance;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Keeps one strategic route per Army. A completed provider route is cloned
    /// into each member's local path; short corrections are only a reconnect path.
    /// </summary>
    internal static class AWArmyMarchService
    {
        private static readonly Dictionary<long, MarchState> States =
            new Dictionary<long, MarchState>();
        private static long _budgetBucket = long.MinValue;
        private static int _correctionsThisBucket;

        public static void OnLeaderPathSubmitted(Actor pActor, WorldTile pTarget,
            int pGenerationId)
        {
            if (!TryGetMarchLeader(pActor, out Army army) || pTarget?.data == null) return;
            int targetId = pTarget.data.tile_id;
            if (States.TryGetValue(army.id, out MarchState current) &&
                current.UsesProvider &&
                !ArmyMarchRules.ShouldInvalidateRoute(current.TargetTileId,
                    targetId, current.GenerationId, pGenerationId))
            {
                BeginLeaderTrail(current, pActor.current_tile);
                return;
            }
            if (!States.TryGetValue(army.id, out current) ||
                ArmyMarchRules.ShouldInvalidateRoute(current.TargetTileId,
                    targetId, current.GenerationId, pGenerationId))
            {
                if (current != null)
                    CancelInstalledActorPaths(current);
                current = new MarchState(army.id, targetId, pGenerationId);
                States[army.id] = current;
            }
            BeginLeaderTrail(current, pActor.current_tile);
        }

        public static void OnLeaderPathStep(Actor pActor, AWPathStep pStep,
            int pGenerationId)
        {
            if (!TryGetMarchLeader(pActor, out Army army) || pActor.tile_target == null) return;

            int targetId = pActor.tile_target.data?.tile_id ?? -1;
            if (targetId < 0) return;
            int generation = pGenerationId;
            if (!States.TryGetValue(army.id, out MarchState state) ||
                ArmyMarchRules.ShouldInvalidateRoute(state.TargetTileId, targetId,
                    state.GenerationId, generation))
            {
                if (state != null)
                    CancelInstalledActorPaths(state);
                state = new MarchState(army.id, targetId, generation);
                States[army.id] = state;
            }
            if (state.LandTrailPausedForTransport) return;

            AppendLeaderTrailStep(state, pStep);
            if (state.UsesProvider) return;

            if (state.Route.Count > 0)
            {
                WorldTile[] tiles = World.world?.tiles_list;
                int previousId = state.Route[state.Route.Count - 1].TileId;
                if (tiles != null && previousId >= 0 && previousId < tiles.Length &&
                    pStep.TileId >= 0 && pStep.TileId < tiles.Length)
                {
                    WorldTile previous = tiles[previousId];
                    WorldTile current = tiles[pStep.TileId];
                    if (previous != null && current != null)
                    {
                        state.DirectionX = Math.Sign(current.x - previous.x);
                        state.DirectionY = Math.Sign(current.y - previous.y);
                    }
                }
            }
            if (ArmySharedPathRules.ShouldTrimRecordedRoute(
                    state.UsesProvider, state.Route.Count,
                    ArmyMarchRules.MaxRouteSteps))
                TrimRecordedRoute(state);
            state.Route.Add(pStep);
            state.Cursor = state.Route.Count - 1;
            state.HasPlan = true;
            UpdateFormationAnchor(army, state, pStep.TileId);
        }

        private static void BeginLeaderTrail(MarchState pState,
            WorldTile pStart)
        {
            if (pState == null) return;
            pState.LeaderTrail.Clear();
            pState.FollowerCursorByActor.Clear();
            pState.LeaderTrailBaseSequence = 0L;
            pState.LeaderTrailNextSequence = 0L;
            pState.LeaderTrailCompleted = false;
            pState.RetainedDeploymentAssignmentKey = "";
            if (pStart?.data != null)
                AppendLeaderTrailStep(pState, new AWPathStep(
                    pStart.data.tile_id, AWMovementMethod.Walk));
        }

        private static void AppendLeaderTrailStep(MarchState pState,
            AWPathStep pStep)
        {
            if (pState == null || pStep.TileId < 0) return;
            if (pState.LeaderTrail.Count > 0)
            {
                LeaderTrailStep previous = pState.LeaderTrail[
                    pState.LeaderTrail.Count - 1];
                if (previous.Step.TileId == pStep.TileId) return;
                UpdateTrailDirection(pState, previous.Step.TileId,
                    pStep.TileId);
            }
            long sequence = pState.LeaderTrailNextSequence;
            if (pState.LeaderTrailNextSequence < long.MaxValue)
                pState.LeaderTrailNextSequence++;
            pState.LeaderTrail.Add(new LeaderTrailStep(sequence, pStep));
            if (pState.LeaderTrail.Count <=
                ArmySharedPathRules.MaximumTrailSteps)
            {
                pState.LeaderTrailBaseSequence =
                    pState.LeaderTrail[0].Sequence;
                return;
            }
            pState.LeaderTrail.RemoveAt(0);
            pState.LeaderTrailBaseSequence =
                pState.LeaderTrail[0].Sequence;
        }

        private static void UpdateTrailDirection(MarchState pState,
            int pPreviousTileId, int pCurrentTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || pPreviousTileId < 0 ||
                pPreviousTileId >= tiles.Length || pCurrentTileId < 0 ||
                pCurrentTileId >= tiles.Length) return;
            WorldTile previous = tiles[pPreviousTileId];
            WorldTile current = tiles[pCurrentTileId];
            if (previous == null || current == null) return;
            pState.DirectionX = Math.Sign(current.x - previous.x);
            pState.DirectionY = Math.Sign(current.y - previous.y);
        }

        public static void OnPathEnded(Actor pActor)
        {
            if (pActor?.army?.data == null) return;
            if (!States.TryGetValue(pActor.army.id, out MarchState state)) return;
            if (state.UsesProvider) return;
            bool actorIsCaptain = IsCaptain(pActor, pActor.army);
            bool deploymentActive = ArmyDeploymentService.
                TryGetActiveAssignmentKey(pActor,
                    out string activeAssignmentKey);
            if (ArmyMarchRules.ShouldRetainCompletedLeaderTrail(
                    state.UsesProvider, actorIsCaptain,
                    deploymentActive,
                    HasLivingFollowers(pActor.army, pActor)))
            {
                state.LeaderTrailCompleted = true;
                state.RetainedDeploymentAssignmentKey = activeAssignmentKey;
                return;
            }
            if (actorIsCaptain)
            {
                States.Remove(pActor.army.id);
                return;
            }
            state.PendingFollowerStartedByActor.Remove(pActor.data.id);
        }

        public static ArmyFollowerTargetResult ResolveFollowerTarget(
            Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || pActor.army?.data == null || IsCaptain(pActor, pActor.army))
                return ArmyFollowerTargetResult.Unavailable;
            if (!States.TryGetValue(pActor.army.id, out MarchState state) ||
                !state.HasPlan)
                return ArmyFollowerTargetResult.Unavailable;
            // Prefer the recorded leader trail whenever a march plan exists.
            // The loose-escort hold radius is only a fallback for armies that
            // have not produced a usable trail yet; applying it here makes
            // every follower hold at march start while the captain departs.
            if (state.InstallStatusByActor.TryGetValue(pActor.data.id,
                    out ArmySharedRouteInstallStatus installStatus) &&
                ArmySharedPathRules.ShouldPublishProviderReconnectTarget(
                    installStatus,
                    state.SharedRouteReconnectTileByActor.ContainsKey(
                        pActor.data.id)) &&
                ResolveProviderReconnectTarget(pActor, state,
                    out pTarget))
            {
                return pTarget == pActor.current_tile
                    ? ArmyFollowerTargetResult.Hold
                    : ArmyFollowerTargetResult.Move;
            }
            if (state.InstallStatusByActor.TryGetValue(pActor.data.id,
                    out installStatus) &&
                installStatus == ArmySharedRouteInstallStatus.Arrived)
            {
                bool formationTargetAvailable =
                    ArmyFormationService.TryGetFollowerTarget(pActor,
                        out pTarget);
                bool formationTargetReached = pTarget?.data != null &&
                                              pTarget == pActor.current_tile;
                if (ArmySharedPathRules.ShouldHoldAtSharedRouteDesired(
                        installStatus, formationTargetReached))
                    return ArmyFollowerTargetResult.Hold;
                return formationTargetAvailable && pTarget?.data != null
                    ? ArmyFollowerTargetResult.Move
                    : ArmyFollowerTargetResult.Unavailable;
            }

            ArmyFollowerTargetResult trailResult =
                ResolveFollowerTrailTarget(pActor, state, out pTarget);
            if (trailResult != ArmyFollowerTargetResult.Unavailable)
                return trailResult;
            if (state.Route.Count == 0)
                return ArmyFollowerTargetResult.Unavailable;

            bool hasFormationTarget =
                ArmyFormationService.TryGetFollowerTarget(pActor,
                    out pTarget);
            if (pTarget?.data == null)
                return ArmyFollowerTargetResult.Unavailable;
            if (!hasFormationTarget || pTarget == pActor.current_tile)
                return ArmyFollowerTargetResult.Hold;
            return ArmyFollowerTargetResult.Move;
        }

        public static bool TryGetFollowerTarget(Actor pActor,
            out WorldTile pTarget)
        {
            return ResolveFollowerTarget(pActor, out pTarget) ==
                   ArmyFollowerTargetResult.Move;
        }

        private static ArmyFollowerTargetResult ResolveFollowerTrailTarget(
            Actor pActor,
            MarchState pState, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || pActor.current_tile?.data == null ||
                pState?.LeaderTrail == null)
                return ArmyFollowerTargetResult.Unavailable;
            if (pState.LandTrailPausedForTransport)
            {
                pTarget = pActor.current_tile;
                return ArmyFollowerTargetResult.Hold;
            }
            if (
                pState.LeaderTrail.Count == 0 ||
                !TryGetOrAssignFollowerSlot(pState, pActor.data.id,
                    out int slot))
                return ArmyFollowerTargetResult.Unavailable;

            long oldest = pState.LeaderTrail[0].Sequence;
            long newest = pState.LeaderTrail[
                pState.LeaderTrail.Count - 1].Sequence;
            bool assigned = pState.FollowerCursorByActor.TryGetValue(
                pActor.data.id, out long cursor);
            if (!assigned)
                cursor = FindClosestLeaderTrailSequence(pState,
                    pActor.current_tile, newest);
            cursor = ArmySharedPathRules.ClampCursor(cursor, oldest,
                newest);
            if (!TryResolveTrailSlot(pActor, pState, cursor, slot,
                    out WorldTile desired, out int rowBehind))
                return ArmyFollowerTargetResult.Unavailable;

            long maximum = ArmySharedPathRules.MaximumSequenceForRow(
                newest, oldest, rowBehind);
            if (!assigned && cursor > maximum)
            {
                int remaining = ArmyFormationRules.LocalRadius * 2 + 1;
                do
                {
                    cursor = maximum;
                    if (!TryResolveTrailSlot(pActor, pState, cursor, slot,
                            out desired, out rowBehind))
                        return ArmyFollowerTargetResult.Unavailable;
                    maximum = ArmySharedPathRules.MaximumSequenceForRow(
                        newest, oldest, rowBehind);
                } while (cursor > maximum && remaining-- > 0);
            }
            bool reached = Toolbox.SquaredDistTile(pActor.current_tile,
                desired) <= 0f;
            long advanced = ArmySharedPathRules.AdvanceCursor(cursor,
                oldest, maximum, reachedCurrentTarget: reached,
                transportActive: false);
            if (advanced != cursor &&
                TryResolveTrailSlot(pActor, pState, advanced, slot,
                    out WorldTile advancedTarget, out _))
            {
                cursor = advanced;
                desired = advancedTarget;
            }
            pState.FollowerCursorByActor[pActor.data.id] = cursor;
            pTarget = desired;
            if (pTarget?.data == null)
                return ArmyFollowerTargetResult.Unavailable;
            float distance = Toolbox.SquaredDistTile(pActor.current_tile,
                pTarget);
            return distance <= 0f
                ? ArmyFollowerTargetResult.Hold
                : ArmyFollowerTargetResult.Move;
        }

        public static void OnTransportStarted(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out MarchState state)) return;
            CancelInstalledActorPaths(state);
            state.LandTrailPausedForTransport = true;
        }

        public static void OnTransportCompleted(Army pArmy)
        {
            RebaseAfterTransport(pArmy);
        }

        public static void OnTransportCancelled(Army pArmy)
        {
            RebaseAfterTransport(pArmy);
        }

        private static void RebaseAfterTransport(Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out MarchState state) ||
                !state.LandTrailPausedForTransport) return;
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            if (captain?.current_tile?.data == null) return;
            BeginLeaderTrail(state, captain.current_tile);
            state.LandTrailPausedForTransport = false;
        }

        public static void OnVanillaLeaderPathStep(Actor pActor)
        {
            if (!TryGetMarchLeader(pActor, out Army army) ||
                pActor.current_tile?.data == null ||
                pActor.tile_target?.data == null) return;
            int targetTileId = pActor.tile_target.data.tile_id;
            bool hasAnyState = States.TryGetValue(army.id,
                out MarchState state);
            bool targetMatches = hasAnyState &&
                                 state.TargetTileId == targetTileId;
            if (state?.UsesProvider == true)
            {
                if (ArmyMarchRules.ShouldPreserveProviderRouteForVanillaStep(
                        usesProvider: true, targetMatches,
                        ArmyRtsControllerService.HasActiveMission(army.id)))
                    return;
                ArmyRouteProviderService.Cancel(army.id,
                    ArmyRouteCancelReason.MissionCancelled);
                CancelInstalledActorPaths(state);
                States.Remove(army.id);
                state = null;
                hasAnyState = false;
            }
            ArmyDeploymentService.TryGetActiveAssignmentKey(pActor,
                out string activeAssignmentKey);
            bool hasState = ArmyMarchRules.ShouldReuseVanillaLeaderTrail(
                hasAnyState && targetMatches,
                state?.LeaderTrailCompleted == true,
                state?.RetainedDeploymentAssignmentKey ?? "",
                activeAssignmentKey);
            if (ArmyMarchRules.ShouldBootstrapVanillaLeaderTrail(
                    hasState, targetTileId >= 0))
            {
                if (state != null)
                    CancelInstalledActorPaths(state);
                state = new MarchState(army.id, targetTileId,
                    pGenerationId: 0);
                States[army.id] = state;
                BeginLeaderTrail(state, pActor.current_tile);
            }
            if (state == null || state.UsesProvider ||
                state.LandTrailPausedForTransport) return;
            if (state.LeaderTrail.Count == 0)
            {
                BeginLeaderTrail(state, pActor.current_tile);
                return;
            }
            AppendLeaderTrailStep(state, new AWPathStep(
                pActor.current_tile.data.tile_id, AWMovementMethod.Walk));
            UpdateFormationAnchor(army, state,
                pActor.current_tile.data.tile_id);
        }

        private static bool TryResolveTrailSlot(Actor pActor,
            MarchState pState, long pSequence, int pSlot,
            out WorldTile pTarget, out int pRowBehind)
        {
            pTarget = null;
            pRowBehind = 0;
            if (!TryGetLeaderTrailIndex(pState, pSequence,
                    out int index)) return false;
            WorldTile[] tiles = World.world?.tiles_list;
            int tileId = pState.LeaderTrail[index].Step.TileId;
            if (tiles == null || tileId < 0 || tileId >= tiles.Length ||
                tiles[tileId]?.data == null) return false;
            ResolveTrailDirection(pState, index, tiles,
                out int directionX, out int directionY);
            return ArmyFormationService.TryResolveSharedPathTarget(
                pActor, tiles[tileId], directionX, directionY, pSlot,
                out pTarget, out pRowBehind);
        }

        private static bool TryGetLeaderTrailIndex(MarchState pState,
            long pSequence, out int pIndex)
        {
            pIndex = -1;
            if (pState?.LeaderTrail == null ||
                pState.LeaderTrail.Count == 0) return false;
            long delta = pSequence - pState.LeaderTrail[0].Sequence;
            if (delta < 0L || delta >= pState.LeaderTrail.Count)
                return false;
            pIndex = (int)delta;
            return pState.LeaderTrail[pIndex].Sequence == pSequence;
        }

        private static long FindClosestLeaderTrailSequence(
            MarchState pState, WorldTile pActorTile, long pMaximum)
        {
            long bestSequence = pState.LeaderTrail[0].Sequence;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < pState.LeaderTrail.Count; i++)
            {
                LeaderTrailStep step = pState.LeaderTrail[i];
                if (step.Sequence > pMaximum) break;
                WorldTile[] tiles = World.world?.tiles_list;
                if (tiles == null || step.Step.TileId < 0 ||
                    step.Step.TileId >= tiles.Length) continue;
                WorldTile tile = tiles[step.Step.TileId];
                if (tile?.data == null) continue;
                float distance = Toolbox.SquaredDistTile(pActorTile, tile);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestSequence = step.Sequence;
            }
            return bestSequence;
        }

        private static void ResolveTrailDirection(MarchState pState,
            int pIndex, WorldTile[] pTiles, out int pDirectionX,
            out int pDirectionY)
        {
            pDirectionX = pState.DirectionX;
            pDirectionY = pState.DirectionY;
            int fromIndex = pIndex > 0 ? pIndex - 1 : pIndex;
            int toIndex = pIndex + 1 < pState.LeaderTrail.Count
                ? pIndex + 1
                : pIndex;
            int fromId = pState.LeaderTrail[fromIndex].Step.TileId;
            int toId = pState.LeaderTrail[toIndex].Step.TileId;
            if (fromId < 0 || fromId >= pTiles.Length || toId < 0 ||
                toId >= pTiles.Length || pTiles[fromId] == null ||
                pTiles[toId] == null) return;
            int directionX = Math.Sign(pTiles[toId].x -
                                       pTiles[fromId].x);
            int directionY = Math.Sign(pTiles[toId].y -
                                       pTiles[fromId].y);
            if (directionX == 0 && directionY == 0) return;
            pDirectionX = directionX;
            pDirectionY = directionY;
        }

        public static ArmyRouteHandle SubmitStrategicRoute(Army pArmy,
            WorldTile pTarget)
        {
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            if (pArmy?.data == null || captain?.current_tile?.data == null ||
                pTarget?.data == null)
                return ArmyRouteHandle.Rejected(pArmy?.id ?? -1L,
                    "invalid_route_input");
            var request = new ArmyRouteRequest(pArmy.id,
                captain.current_tile.data.tile_id,
                pTarget.data.tile_id);
            ArmyRouteHandle handle =
                ArmyRouteProviderService.Submit(request);
            if (!handle.Accepted) return handle;
            var state = new MarchState(pArmy.id,
                pTarget.data.tile_id,
                AWPathfindingBootstrap.Cache.GenerationId,
                pUsesProvider: true);
            state.LandTrailPausedForTransport =
                ArmyRtsTransportService.HasActiveVoyage(pArmy);
            if (States.TryGetValue(pArmy.id,
                    out MarchState previousState))
                CancelInstalledActorPaths(previousState);
            States[pArmy.id] = state;
            return handle;
        }

        public static ArmyRoutePoll PollStrategicRoute(Army pArmy)
        {
            if (pArmy?.data == null)
                return new ArmyRoutePoll(ArmyRoutePollKind.NoRequest);
            ArmyRoutePoll poll = ArmyRouteProviderService.Poll(pArmy.id);
            if (!States.TryGetValue(pArmy.id, out MarchState state) ||
                !state.UsesProvider) return poll;
            if (poll.Kind == ArmyRoutePollKind.StepReady)
                AppendProviderStep(pArmy, state, poll.TileId,
                    poll.MovementMethod, poll.Estimate);
            else if (poll.Kind == ArmyRoutePollKind.Completed)
            {
                if (!state.ProviderComplete &&
                    state.SharedRouteRevision < int.MaxValue)
                    state.SharedRouteRevision++;
                state.ProviderComplete = true;
            }
            else if (poll.Kind == ArmyRoutePollKind.Failed ||
                     poll.Kind == ArmyRoutePollKind.Cancelled ||
                     poll.Kind == ArmyRoutePollKind.NoRequest)
            {
                CancelInstalledActorPaths(state);
                States.Remove(pArmy.id);
            }
            return poll;
        }

        public static bool TryGetCompletedLandRouteCost(Army pArmy,
            out float pCost)
        {
            pCost = 0f;
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out MarchState state) ||
                !state.UsesProvider || !state.ProviderComplete ||
                state.ContainsTransportStep ||
                float.IsNaN(state.LandRouteCost) ||
                float.IsInfinity(state.LandRouteCost)) return false;
            pCost = Math.Max(0f, state.LandRouteCost);
            return true;
        }

        private static void AppendProviderStep(Army pArmy,
            MarchState pState, int pTileId,
            AWMovementMethod pMovementMethod,
            AWTraversalEstimate pEstimate)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || pTileId < 0 || pTileId >= tiles.Length)
                return;
            if (pState.Route.Count > 0)
            {
                int previousId = pState.Route[pState.Route.Count - 1].TileId;
                if (previousId >= 0 && previousId < tiles.Length &&
                    tiles[previousId] != null && tiles[pTileId] != null)
                {
                    pState.DirectionX = Math.Sign(
                        tiles[pTileId].x - tiles[previousId].x);
                    pState.DirectionY = Math.Sign(
                        tiles[pTileId].y - tiles[previousId].y);
                }
            }
            if (ArmySharedPathRules.ShouldTrimRecordedRoute(
                    pState.UsesProvider, pState.Route.Count,
                    ArmyMarchRules.MaxRouteSteps))
                TrimRecordedRoute(pState);
            pState.Route.Add(new AWPathStep(pTileId, pMovementMethod,
                pEstimate));
            if (pMovementMethod == AWMovementMethod.Transport)
                pState.ContainsTransportStep = true;
            else
            {
                float stepCost = pEstimate.TimeSeconds + pEstimate.RiskCost;
                if (!float.IsNaN(stepCost) && !float.IsInfinity(stepCost) &&
                    stepCost >= 0f)
                    pState.LandRouteCost += stepCost;
            }
            pState.HasPlan = true;
        }

        private static void TrimRecordedRoute(MarchState pState)
        {
            if (pState?.Route == null || pState.Route.Count == 0) return;
            pState.Route.RemoveAt(0);
            if (pState.Cursor > 0) pState.Cursor--;
        }

        public static bool TryStartCompleteSharedRoute(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                pActor.current_tile?.data == null ||
                !States.TryGetValue(army.id, out MarchState state) ||
                !state.UsesProvider)
                return false;
            if (state.Route.Count == 0)
            {
                RecordInstallStatus(state, pActor,
                    ArmySharedRouteInstallStatus.RouteEmpty);
                return false;
            }
            WorldTile finalCenter = FindRouteTile(state,
                state.Route.Count - 1);
            bool transportActive = state.LandTrailPausedForTransport ||
                                   ArmyRtsTransportService.
                                       HasActiveVoyage(army);
            if (!ArmySharedPathRules.CanInstallCompleteProviderRoute(
                    state.ProviderComplete, state.Route.Count,
                    state.ContainsTransportStep, transportActive))
            {
                RecordInstallStatus(state, pActor,
                    state.ContainsTransportStep || transportActive
                        ? ArmySharedRouteInstallStatus.TransportOwned
                        : ArmySharedRouteInstallStatus.ProviderPending);
                return false;
            }

            int revision = state.SharedRouteRevision;
            bool hasInstalled = state.
                SharedRouteRevisionByActor.TryGetValue(pActor.data.id,
                    out int installedRevision);
            bool atInstalledEndpoint = hasInstalled &&
                installedRevision == revision &&
                state.SharedRouteEndTileByActor.TryGetValue(
                    pActor.data.id, out int installedEndTileId) &&
                pActor.current_tile.data.tile_id == installedEndTileId;
            int localPathCount = SafeLocalPathCount(pActor);
            bool followingLocalPath = localPathCount > 0 &&
                                      pActor.isFollowingLocalPath();
            if (hasInstalled &&
                ArmySharedPathRules.ShouldReuseInstalledSharedRoute(
                    installedRevision, revision, localPathCount,
                    followingLocalPath, atInstalledEndpoint))
            {
                if (atInstalledEndpoint)
                {
                    RecordInstallStatus(state, pActor,
                        ArmySharedRouteInstallStatus.Arrived);
                    return false;
                }
                RecordInstallStatus(state, pActor,
                    ArmySharedRouteInstallStatus.Following);
                return true;
            }
            if (hasInstalled && installedRevision == revision)
            {
                ArmySharedRouteInstallStatus currentStatus =
                    ArmySharedPathRules.ResolveCurrentInstallStatus(
                        providerAvailable: true, transportActive: false,
                        hasMatchingRevision: true,
                        atInstalledEndpoint: false,
                        localPathCount: localPathCount,
                        actorFollowingLocalPath: followingLocalPath,
                        recordedStatus:
                            ArmySharedRouteInstallStatus.Installed);
                bool combatActive = ArmyRtsControllerService.
                    HasImmediateCombatPriority(pActor);
                if (!ArmySharedPathRules.
                        ShouldRecoverStaleInstalledRoute(currentStatus,
                            combatActive, transportActive: false))
                {
                    RecordInstallStatus(state, pActor, currentStatus);
                    return false;
                }
                ResetActorSharedRoute(pActor);
            }
            if (IsFollowingProviderDestination(pActor, state))
            {
                state.SharedRouteRevisionByActor[pActor.data.id] =
                    revision;
                state.SharedRouteEndTileByActor[pActor.data.id] =
                    state.TargetTileId;
                state.SharedRouteAttemptRevisionByActor.Remove(
                    pActor.data.id);
                RecordInstallStatus(state, pActor,
                    ArmySharedRouteInstallStatus.Following);
                return true;
            }
            state.SharedRouteRevisionByActor.Remove(pActor.data.id);
            state.SharedRouteEndTileByActor.Remove(pActor.data.id);
            state.SharedRouteReconnectTileByActor.Remove(pActor.data.id);
            if (!TryBuildActorRoute(pActor, army, state,
                    out List<WorldTile> route, out WorldTile routeEnd,
                    out WorldTile reconnectTarget) ||
                route.Count == 0 || routeEnd?.data == null)
            {
                state.SharedRouteAttemptRevisionByActor[pActor.data.id] =
                    revision;
                if (reconnectTarget?.data != null)
                {
                    state.SharedRouteReconnectTileByActor[pActor.data.id] =
                        reconnectTarget.data.tile_id;
                    RecordInstallStatus(state, pActor,
                        ArmySharedRouteInstallStatus.ReconnectRequired);
                }
                else
                {
                    RecordInstallStatus(state, pActor,
                        ArmySharedRouteInstallStatus.BuildFailed);
                }
                return false;
            }

            if (AWPathMovementBridge.HasOwnership(pActor))
                AWPathMovementBridge.Cancel(pActor,
                    AWPathFailureReason.CancelledByNewRequest);
            pActor.stopMovement();
            pActor.clearOldPath();
            pActor.beh_tile_target = routeEnd;
            pActor.setTileTarget(routeEnd);
            for (int i = 0; i < route.Count; i++)
                pActor.current_path.Add(route[i]);
            pActor.current_path_index = 0;
            pActor.current_path_global = null;
            pActor.split_path = SplitPathStatus.Normal;
            state.SharedRouteRevisionByActor[pActor.data.id] = revision;
            state.SharedRouteEndTileByActor[pActor.data.id] =
                routeEnd.data.tile_id;
            state.SharedRouteReconnectTileByActor.Remove(pActor.data.id);
            state.SharedRouteAttemptRevisionByActor.Remove(pActor.data.id);
            pActor.updatePathMovement();
            bool started = pActor.is_moving ||
                           pActor.isFollowingLocalPath();
            RecordInstallStatus(state, pActor, started
                ? ArmySharedRouteInstallStatus.Installed
                : ArmySharedRouteInstallStatus.MovementRejected);
            return started;
        }

        public static bool NeedsCompleteSharedRoute(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                pActor.current_tile?.data == null ||
                !States.TryGetValue(army.id, out MarchState state) ||
                !state.UsesProvider) return false;
            bool transportActive = state.LandTrailPausedForTransport ||
                                   ArmyRtsTransportService.HasActiveVoyage(
                                       army);
            bool providerReady = ArmySharedPathRules.
                CanInstallCompleteProviderRoute(state.ProviderComplete,
                    state.Route.Count, state.ContainsTransportStep,
                    transportActive);
            int revision = state.SharedRouteRevision;
            bool hasMatchingRevision = state.SharedRouteRevisionByActor.
                TryGetValue(pActor.data.id, out int installedRevision) &&
                installedRevision == revision;
            bool atInstalledEndpoint = hasMatchingRevision &&
                state.SharedRouteEndTileByActor.TryGetValue(
                    pActor.data.id, out int endpointTileId) &&
                pActor.current_tile.data.tile_id == endpointTileId;
            bool followingInstalledPath = hasMatchingRevision &&
                SafeLocalPathCount(pActor) > 0 &&
                pActor.isFollowingLocalPath();
            if (!ArmySharedPathRules.ShouldInstallCompleteRouteForActor(
                    providerReady, transportActive, hasMatchingRevision,
                    atInstalledEndpoint, followingInstalledPath)) return false;
            return !state.SharedRouteAttemptRevisionByActor.TryGetValue(
                       pActor.data.id, out int attemptedRevision) ||
                   attemptedRevision != revision;
        }

        public static bool HasActiveCompleteSharedRoute(Actor pActor)
        {
            Army army = pActor?.army;
            return pActor?.data != null && army?.data != null &&
                   States.TryGetValue(army.id, out MarchState state) &&
                   state.UsesProvider &&
                   state.SharedRouteRevisionByActor.TryGetValue(
                       pActor.data.id, out int installedRevision) &&
                   installedRevision == state.SharedRouteRevision &&
                   SafeLocalPathCount(pActor) > 0 &&
                   pActor.isFollowingLocalPath();
        }

        public static ArmySharedRouteInstallStatus
            GetSharedRouteInstallStatus(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !States.TryGetValue(army.id, out MarchState state) ||
                !state.UsesProvider)
                return ArmySharedRouteInstallStatus.Unavailable;
            long actorId = pActor.data.id;
            ArmySharedRouteInstallStatus recorded =
                state.InstallStatusByActor.TryGetValue(actorId,
                    out ArmySharedRouteInstallStatus status)
                    ? status
                    : ArmySharedRouteInstallStatus.NotAttempted;
            bool transportActive = state.LandTrailPausedForTransport ||
                                   ArmyRtsTransportService.HasActiveVoyage(
                                       army);
            bool matching = state.SharedRouteRevisionByActor.TryGetValue(
                                actorId, out int installedRevision) &&
                            installedRevision == state.SharedRouteRevision;
            bool atEndpoint = matching &&
                pActor.current_tile?.data != null &&
                state.SharedRouteEndTileByActor.TryGetValue(actorId,
                    out int endpointTileId) &&
                pActor.current_tile.data.tile_id == endpointTileId;
            int localPathCount = SafeLocalPathCount(pActor);
            bool following = localPathCount > 0 &&
                             pActor.isFollowingLocalPath();
            return ArmySharedPathRules.ResolveCurrentInstallStatus(
                providerAvailable: true,
                transportActive: transportActive,
                hasMatchingRevision: matching,
                atInstalledEndpoint: atEndpoint,
                localPathCount: localPathCount,
                actorFollowingLocalPath: following,
                recordedStatus: recorded);
        }

        public static bool ResetActorSharedRoute(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                !States.TryGetValue(army.id, out MarchState state))
                return false;
            long actorId = pActor.data.id;
            if (AWPathMovementBridge.HasOwnership(pActor))
                AWPathMovementBridge.Cancel(pActor,
                    AWPathFailureReason.CancelledByNewRequest);
            pActor.stopMovement();
            pActor.beh_tile_target = null;
            pActor.clearOldPath();
            pActor.clearTileTarget();
            state.SharedRouteRevisionByActor.Remove(actorId);
            state.SharedRouteEndTileByActor.Remove(actorId);
            state.SharedRouteReconnectTileByActor.Remove(actorId);
            state.SharedRouteAttemptRevisionByActor.Remove(actorId);
            state.InstallStatusByActor.Remove(actorId);
            state.FollowerCursorByActor.Remove(actorId);
            state.PendingFollowerStartedByActor.Remove(actorId);
            return true;
        }

        public static bool TryAdvanceInstalledProviderSwimEntry(
            Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                pActor.current_tile?.data == null ||
                !States.TryGetValue(army.id, out MarchState state) ||
                !state.UsesProvider ||
                !state.SharedRouteRevisionByActor.ContainsKey(
                    pActor.data.id) ||
                pActor.current_path_index < 0 ||
                pActor.current_path_index >= pActor.current_path.Count)
                return false;
            WorldTile next = pActor.current_path[
                pActor.current_path_index];
            bool military = false;
            bool damagedByOcean = false;
            bool alreadyInLiquid = false;
            try
            {
                military = pActor.isWarrior() || pActor.hasArmy();
                damagedByOcean = pActor.isDamagedByOcean();
                alreadyInLiquid = pActor.isInLiquid();
            }
            catch { }
            if (!ArmySharedPathRules.ShouldAdvanceInstalledProviderSwim(
                    sharedRouteInstalled: true, actorMilitary: military,
                    nextTileOcean: next?.Type?.ocean == true,
                    damagedByOcean: damagedByOcean,
                    alreadyInLiquid: alreadyInLiquid)) return false;
            if (next?.Type == null || next.Type.block || next.Type.lava)
                return false;
            try
            {
                if (next.hasWallsAround()) return false;
            }
            catch { return false; }
            if (next.Type.damaged_when_walked)
                pActor.current_tile.tryToBreak();
            pActor.current_path_index++;
            pActor.moveTo(next);
            RecordInstallStatus(state, pActor,
                ArmySharedRouteInstallStatus.Following);
            return true;
        }

        private static void RecordInstallStatus(MarchState pState,
            Actor pActor, ArmySharedRouteInstallStatus pStatus)
        {
            if (pState == null || pActor?.data == null) return;
            pState.InstallStatusByActor[pActor.data.id] = pStatus;
        }

        private static int SafeLocalPathCount(Actor pActor)
        {
            try { return Math.Max(0, pActor?.current_path?.Count ?? 0); }
            catch { return 0; }
        }

        private static bool IsFollowingProviderDestination(Actor pActor,
            MarchState pState)
        {
            return pActor?.tile_target?.data != null &&
                   pActor.tile_target.data.tile_id == pState.TargetTileId &&
                   pActor.isFollowingLocalPath();
        }

        private static bool TryBuildActorRoute(Actor pActor, Army pArmy,
            MarchState pState, out List<WorldTile> pRoute,
            out WorldTile pRouteEnd, out WorldTile pReconnectTarget)
        {
            pRoute = new List<WorldTile>(pState.Route.Count +
                                         ArmySharedPathRules.
                                             LocalReconnectRadius);
            pRouteEnd = null;
            pReconnectTarget = null;
            int startIndex = FindClosestProviderRouteIndex(pState,
                pActor.current_tile);
            if (startIndex < 0) return false;
            pReconnectTarget = FindRouteTile(pState, startIndex);
            bool captain = IsCaptain(pActor, pArmy);
            int slot = 0;
            if (!captain && !TryGetOrAssignFollowerSlot(pState,
                    pActor.data.id, out slot)) return false;

            if (TryBuildActorRouteCore(pActor, pArmy, pState, startIndex,
                    slot, useFormationLanes: true, pRoute,
                    out pRouteEnd))
            {
                pReconnectTarget = null;
                return true;
            }
            pRoute.Clear();
            pRouteEnd = null;
            if (TryBuildActorRouteCore(pActor, pArmy, pState, startIndex,
                    slot, useFormationLanes: false, pRoute,
                    out pRouteEnd))
            {
                pReconnectTarget = null;
                return true;
            }
            pRoute.Clear();
            pRouteEnd = null;
            return false;
        }

        private static bool TryBuildActorRouteCore(Actor pActor,
            Army pArmy, MarchState pState, int pStartIndex, int pSlot,
            bool useFormationLanes, List<WorldTile> pRoute,
            out WorldTile pRouteEnd)
        {
            pRouteEnd = null;
            bool captain = IsCaptain(pActor, pArmy);
            WorldTile previous = pActor.current_tile;
            for (int i = pStartIndex; i < pState.Route.Count; i++)
            {
                WorldTile center = FindRouteTile(pState, i);
                if (center?.data == null) return false;
                WorldTile desired = center;
                if (!captain && useFormationLanes)
                {
                    ResolveProviderDirection(pState, i,
                        out int directionX, out int directionY);
                    if (!ArmyFormationService.
                            TryResolveProviderRouteTarget(pActor, center,
                                directionX, directionY, pSlot,
                                out desired))
                        return false;
                }

                if (i == pStartIndex && previous != desired)
                {
                    if (!AppendReconnectPrefix(previous, desired,
                            pRoute, out previous)) return false;
                }
                else if (!AppendRouteTransition(previous, desired, center,
                             pRoute, out previous)) return false;
                pRouteEnd = previous;
            }
            return pRouteEnd?.data != null;
        }

        private static bool ResolveProviderReconnectTarget(Actor pActor,
            MarchState pState, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || pActor.current_tile?.data == null ||
                pState == null ||
                !pState.SharedRouteReconnectTileByActor.TryGetValue(
                    pActor.data.id, out int reconnectTileId)) return false;
            WorldTile[] tiles = World.world?.tiles_list;
            if (tiles == null || reconnectTileId < 0 ||
                reconnectTileId >= tiles.Length ||
                tiles[reconnectTileId]?.data == null) return false;
            pTarget = ArmyFormationService.ClampFollowerCorrectionTarget(
                pActor.current_tile, tiles[reconnectTileId]);
            return pTarget?.data != null;
        }

        private static bool AppendReconnectPrefix(WorldTile pStart,
            WorldTile pTarget, List<WorldTile> pRoute,
            out WorldTile pLast)
        {
            pLast = pStart;
            int remaining = ArmySharedPathRules.LocalReconnectRadius * 2;
            while (pLast != pTarget && remaining-- > 0)
            {
                WorldTile step = SelectDirectStep(pLast, pTarget);
                if (step == null) return false;
                pRoute.Add(step);
                pLast = step;
            }
            return pLast == pTarget;
        }

        private static bool AppendRouteTransition(WorldTile pStart,
            WorldTile pTarget, WorldTile pCenter,
            List<WorldTile> pRoute, out WorldTile pLast)
        {
            pLast = pStart;
            if (pStart?.data == null || pTarget?.data == null ||
                pCenter?.data == null) return false;
            bool providerStepValidated = pTarget == pCenter ||
                                         IsAdjacent(pTarget, pCenter);
            if (ArmySharedPathRules.ShouldAppendAdjacentProviderStep(
                    IsAdjacent(pStart, pTarget),
                    providerStepValidated))
            {
                pRoute.Add(pTarget);
                pLast = pTarget;
                return true;
            }
            int remaining = ArmySharedPathRules.RequiredAdjacentSteps(
                pStart.x, pStart.y, pTarget.x, pTarget.y);
            while (pLast != pTarget && remaining-- > 0)
            {
                WorldTile step = SelectProviderBridgeStep(pLast, pTarget,
                    pCenter, providerStepValidated);
                if (step == null) return false;
                pRoute.Add(step);
                pLast = step;
            }
            return pLast == pTarget;
        }

        private static WorldTile SelectProviderBridgeStep(
            WorldTile pCurrent, WorldTile pTarget, WorldTile pCenter,
            bool pProviderStepValidated)
        {
            if (pCurrent?.data == null || pTarget?.data == null ||
                pCenter?.data == null) return null;
            float bestDistance = Toolbox.SquaredDistTile(pCurrent, pTarget);
            WorldTile best = null;
            WorldTile[] neighbours = pCurrent.neighboursAll;
            int count = Math.Min(8, neighbours?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                WorldTile candidate = neighbours[i];
                bool hasWalls = true;
                try
                {
                    hasWalls = candidate?.data == null ||
                               candidate.hasWallsAround();
                }
                catch { }
                float distance = candidate?.data != null
                    ? Toolbox.SquaredDistTile(candidate, pTarget)
                    : float.MaxValue;
                bool withinEnvelope = candidate?.data != null &&
                    Math.Abs(candidate.x - pCenter.x) <=
                    ArmySharedPathRules.ProviderFormationEnvelopeRadius &&
                    Math.Abs(candidate.y - pCenter.y) <=
                    ArmySharedPathRules.ProviderFormationEnvelopeRadius;
                if (!ArmySharedPathRules.ShouldAppendProviderBridgeStep(
                        pProviderStepValidated,
                        candidate?.Type == null || candidate.Type.block,
                        candidate?.Type?.lava == true, hasWalls,
                        distance < bestDistance, withinEnvelope)) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }

        private static int FindClosestProviderRouteIndex(MarchState pState,
            WorldTile pCurrent)
        {
            if (pState?.Route == null || pCurrent?.data == null)
                return -1;
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < pState.Route.Count; i++)
            {
                WorldTile tile = FindRouteTile(pState, i);
                if (tile?.data == null) continue;
                float distance = Toolbox.SquaredDistTile(pCurrent, tile);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestIndex = i;
            }
            return bestIndex;
        }

        private static void ResolveProviderDirection(MarchState pState,
            int pIndex, out int pDirectionX, out int pDirectionY)
        {
            pDirectionX = pState.DirectionX;
            pDirectionY = pState.DirectionY;
            int fromIndex = pIndex > 0 ? pIndex - 1 : pIndex;
            int toIndex = pIndex + 1 < pState.Route.Count
                ? pIndex + 1
                : pIndex;
            WorldTile from = FindRouteTile(pState, fromIndex);
            WorldTile to = FindRouteTile(pState, toIndex);
            if (from?.data == null || to?.data == null) return;
            int x = Math.Sign(to.x - from.x);
            int y = Math.Sign(to.y - from.y);
            if (x == 0 && y == 0) return;
            pDirectionX = x;
            pDirectionY = y;
        }

        private static WorldTile FindRouteTile(MarchState pState,
            int pIndex)
        {
            if (pState?.Route == null || pIndex < 0 ||
                pIndex >= pState.Route.Count) return null;
            int tileId = pState.Route[pIndex].TileId;
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && tileId >= 0 && tileId < tiles.Length
                ? tiles[tileId]
                : null;
        }

        private static bool IsAdjacent(WorldTile pFirst,
            WorldTile pSecond)
        {
            return pFirst?.data != null && pSecond?.data != null &&
                   ArmySharedPathRules.AreRouteTilesAdjacent(
                       pSecond.x - pFirst.x, pSecond.y - pFirst.y);
        }

        private static bool SafeSameIsland(WorldTile pFirst,
            WorldTile pSecond)
        {
            try
            {
                return pFirst?.data != null && pSecond?.data != null &&
                       pFirst.isSameIsland(pSecond);
            }
            catch { return false; }
        }

        private static void UpdateFormationAnchor(Army pArmy,
            MarchState pState, int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            if (pArmy?.data == null || pState == null || tiles == null ||
                pTileId < 0 || pTileId >= tiles.Length ||
                tiles[pTileId]?.data == null) return;
            ArmyFormationService.SetAnchor(pArmy, tiles[pTileId],
                pState.DirectionX, pState.DirectionY);
        }

        public static ArmyFollowerStepResult TryStepFollowerDirect(
            Actor pActor)
        {
            ArmyFollowerTargetResult targetResult =
                ResolveFollowerTarget(pActor, out WorldTile target);
            if (targetResult == ArmyFollowerTargetResult.Hold)
                return ArmyFollowerStepResult.Hold;
            if (targetResult != ArmyFollowerTargetResult.Move)
                return ArmyFollowerStepResult.Unavailable;
            return TryStepFollowerDirect(pActor, target);
        }

        public static ArmyFollowerStepResult TryStepFollowerDirect(Actor pActor,
            WorldTile pTarget)
        {
            bool validActor = pActor?.data != null &&
                              pActor.current_tile?.data != null;
            bool validTarget = pTarget?.data != null;
            float targetDistance = validActor && validTarget
                ? Toolbox.SquaredDistTile(pActor.current_tile, pTarget)
                : -1f;
            WorldTile step = targetDistance > 0f &&
                             targetDistance <=
                             ArmySharedPathRules.LocalReconnectRadius *
                             ArmySharedPathRules.LocalReconnectRadius
                ? SelectDirectStep(pActor.current_tile, pTarget)
                : null;
            double now = Time.realtimeSinceStartupAsDouble;
            bool correctionReady = validActor && step != null &&
                                   ArmyFormationService.CanIssueCorrection(
                                       pActor, now);
            bool budgetAvailable = correctionReady &&
                                   TryConsumeCorrectionBudget(now);
            ArmyFollowerStepResult result =
                ArmySharedPathRules.ResolveDirectStepResult(
                    validActor, validTarget, targetDistance,
                    step != null, correctionReady, budgetAvailable);
            if (result != ArmyFollowerStepResult.Stepped) return result;
            ArmyFormationService.RecordCorrection(pActor, now,
                ArmyMarchRules.FollowerCorrectionCooldownSeconds);
            pActor.beh_tile_target = null;
            pActor.clearOldPath();
            pActor.clearTileTarget();
            pActor.moveTo(step);
            ArmyRtsBenchmark.RecordFormationCorrection();
            return ArmyFollowerStepResult.Stepped;
        }

        private static WorldTile SelectDirectStep(WorldTile pCurrent,
            WorldTile pTarget)
        {
            if (pCurrent == null || pTarget == null) return null;
            float bestDistance = Toolbox.SquaredDistTile(pCurrent, pTarget);
            WorldTile best = null;
            WorldTile[] neighbours = pCurrent.neighboursAll;
            int count = Math.Min(8, neighbours?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                WorldTile candidate = neighbours[i];
                if (!IsSafeFormationCandidate(pCurrent, candidate)) continue;
                float distance = Toolbox.SquaredDistTile(candidate, pTarget);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }

        private static void ExpirePendingFollowerCorrections(MarchState pState,
            double pNow)
        {
            if (pState.PendingFollowerStartedByActor.Count <
                ArmyMarchRules.MaxConcurrentFollowerCorrectionsPerArmy) return;
            int remaining = ArmyMarchRules.MaxConcurrentFollowerCorrectionsPerArmy;
            while (remaining-- > 0)
            {
                long expiredActorId = -1L;
                foreach (KeyValuePair<long, double> pair in
                         pState.PendingFollowerStartedByActor)
                {
                    if (!ArmyMarchRules.IsFollowerCorrectionExpired(
                            pair.Value, pNow)) continue;
                    expiredActorId = pair.Key;
                    break;
                }
                if (expiredActorId < 0) return;
                pState.PendingFollowerStartedByActor.Remove(expiredActorId);
            }
        }

        private static bool TryGetOrAssignFollowerSlot(MarchState pState,
            long pActorId, out int pSlot)
        {
            if (pState.Slots.TryGetValue(pActorId, out pSlot)) return true;

            pSlot = -1;
            if (pState.Slots.Count >= ArmyMarchRules.MaxTrackedFollowers)
            {
                int evictionChecks = pState.FollowerOrder.Count;
                while (evictionChecks-- > 0 && pState.FollowerOrder.Count > 0)
                {
                    long evictedActorId = pState.FollowerOrder.Dequeue();
                    if (pState.PendingFollowerStartedByActor.ContainsKey(
                            evictedActorId))
                    {
                        pState.FollowerOrder.Enqueue(evictedActorId);
                        continue;
                    }
                    if (!pState.Slots.TryGetValue(evictedActorId,
                            out int evictedSlot)) continue;
                    pState.Slots.Remove(evictedActorId);
                    pState.NextAllowedByActor.Remove(evictedActorId);
                    pState.FollowerCursorByActor.Remove(evictedActorId);
                    pSlot = evictedSlot;
                    break;
                }
            }

            if (pSlot < 0)
            {
                if (pState.Slots.Count >= ArmyMarchRules.MaxTrackedFollowers)
                    return false;
                pSlot = pState.NextSlot++;
            }
            pState.Slots[pActorId] = pSlot;
            pState.FollowerOrder.Enqueue(pActorId);
            return true;
        }

        public static bool HasActiveMarch(Actor pActor)
        {
            Army army = pActor?.army;
            if (army?.data == null ||
                !States.TryGetValue(army.id, out MarchState state))
                return false;
            return ArmyMarchRules.ShouldUseFollowerMarch(
                hasArmy: true,
                hasMarchPlan: state.HasPlan,
                hasValidTarget: state.Route.Count > 0,
                sameIslandAsCaptain: IsOnCaptainIsland(pActor, army));
        }

        public static bool HasOwnedMarch(Actor pActor)
        {
            Army army = pActor?.army;
            if (pActor?.data == null || army?.data == null ||
                IsCaptain(pActor, army) ||
                !States.TryGetValue(army.id, out MarchState state))
                return false;
            return ArmyMarchRules.ShouldOwnFollowerMarch(
                hasArmy: true,
                hasMarchPlan: state.HasPlan,
                hasRouteSteps: state.Route.Count > 0,
                sameIslandAsCaptain: IsOnCaptainIsland(pActor, army));
        }

        public static void ClearArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            ClearArmy(pArmy.id);
        }

        public static void ClearArmy(long pArmyId)
        {
            if (pArmyId < 0L) return;
            ArmyRouteProviderService.Cancel(pArmyId,
                ArmyRouteCancelReason.MissionCancelled);
            if (States.TryGetValue(pArmyId, out MarchState state))
                CancelInstalledActorPaths(state);
            States.Remove(pArmyId);
        }

        public static void ClearRetainedDeploymentTrail(Army pArmy,
            string pClosingAssignmentKey)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out MarchState state) ||
                !ArmyMarchRules.ShouldClearRetainedDeploymentTrail(
                    state.UsesProvider, state.LeaderTrailCompleted,
                    state.RetainedDeploymentAssignmentKey,
                    pClosingAssignmentKey)) return;
            CancelInstalledActorPaths(state);
            States.Remove(pArmy.id);
        }

        public static void ReleaseCompletedDeploymentTrailIfUnused(
            Army pArmy)
        {
            if (pArmy?.data == null ||
                !States.TryGetValue(pArmy.id, out MarchState state)) return;
            Actor captain = null;
            try { captain = pArmy.getCaptain(); }
            catch { }
            if (!ArmyMarchRules.ShouldReleaseCompletedLeaderTrail(
                    state.UsesProvider, state.LeaderTrailCompleted,
                    HasLivingFollowers(pArmy, captain))) return;
            CancelInstalledActorPaths(state);
            States.Remove(pArmy.id);
        }

        public static void ClearLegacy()
        {
            var remove = new List<long>();
            foreach (KeyValuePair<long, MarchState> pair in States)
                if (!pair.Value.UsesProvider) remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
            {
                if (States.TryGetValue(remove[i], out MarchState state))
                    CancelInstalledActorPaths(state);
                States.Remove(remove[i]);
            }
            _budgetBucket = long.MinValue;
            _correctionsThisBucket = 0;
        }

        public static void Clear()
        {
            foreach (MarchState state in States.Values)
                CancelInstalledActorPaths(state);
            States.Clear();
            _budgetBucket = long.MinValue;
            _correctionsThisBucket = 0;
        }

        private static void CancelInstalledActorPaths(MarchState pState)
        {
            if (pState == null) return;
            var actorIds = new List<long>(
                pState.SharedRouteRevisionByActor.Keys);
            for (int i = 0; i < actorIds.Count; i++)
            {
                long actorId = actorIds[i];
                Actor actor = FindActor(actorId);
                if (actor?.data == null || actor.army?.data == null ||
                    actor.army.id != pState.ArmyId ||
                    !pState.SharedRouteEndTileByActor.TryGetValue(actorId,
                        out int routeEndTileId)) continue;
                bool ownsInstalledRoute;
                try
                {
                    ownsInstalledRoute =
                        actor.tile_target?.data?.tile_id == routeEndTileId;
                }
                catch { ownsInstalledRoute = false; }
                if (ownsInstalledRoute) actor.stopMovement();
            }
            pState.SharedRouteRevisionByActor.Clear();
            pState.SharedRouteEndTileByActor.Clear();
            pState.SharedRouteReconnectTileByActor.Clear();
            pState.SharedRouteAttemptRevisionByActor.Clear();
        }

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0L
                    ? World.world?.units?.get(pActorId)
                    : null;
            }
            catch { return null; }
        }

        private static bool IsCaptain(Actor pActor, Army pArmy)
        {
            try { return pArmy?.getCaptain() == pActor; }
            catch { return false; }
        }

        private static bool IsOnCaptainIsland(Actor pActor, Army pArmy)
        {
            try
            {
                Actor captain = pArmy?.getCaptain();
                return pActor?.current_tile != null &&
                       captain?.current_tile != null &&
                       pActor.current_tile.isSameIsland(
                           captain.current_tile);
            }
            catch { return false; }
        }

        private static bool HasLivingFollowers(Army pArmy,
            Actor pCaptain)
        {
            if (pArmy?.data == null) return false;
            int count;
            try { count = pArmy.units.Count; }
            catch { return false; }
            for (int i = 0; i < count; i++)
            {
                Actor actor = null;
                try { actor = pArmy.units[i]; }
                catch { }
                if (actor?.data == null || actor == pCaptain ||
                    actor.army != pArmy) continue;
                try
                {
                    if (!actor.isRekt() && actor.isAlive() &&
                        actor.isWarrior()) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool TryGetMarchLeader(Actor pActor, out Army pArmy)
        {
            pArmy = pActor?.army;
            if (!ArmyMarchRules.ShouldInspectMarchLeader(
                    pActor?.data != null, pArmy?.data != null,
                    pActor?.ai?.task?.id)) return false;
            return IsCaptain(pActor, pArmy);
        }

        private static bool TryConsumeCorrectionBudget(double pNow)
        {
            long bucket = (long)Math.Floor(pNow / 0.1d);
            if (bucket != _budgetBucket)
            {
                _budgetBucket = bucket;
                _correctionsThisBucket = 0;
            }
            if (_correctionsThisBucket >= ArmyMarchRules.MaxFollowerCorrectionsPerTick)
                return false;
            _correctionsThisBucket++;
            return true;
        }

        private static bool IsSafeFormationCandidate(WorldTile pBaseTile,
            WorldTile pCandidate)
        {
            if (pBaseTile?.Type == null || pCandidate?.Type == null ||
                pCandidate.Type.block || pCandidate.Type.lava) return false;
            try
            {
                if (pCandidate.hasWallsAround()) return false;
            }
            catch { return false; }
            bool baseLiquid = pBaseTile.Type.liquid || pBaseTile.Type.ocean;
            bool candidateLiquid = pCandidate.Type.liquid ||
                                   pCandidate.Type.ocean;
            if (baseLiquid != candidateLiquid) return false;
            if (baseLiquid) return true;
            try { return pCandidate.isSameIsland(pBaseTile); }
            catch { return false; }
        }

        private readonly struct LeaderTrailStep
        {
            public LeaderTrailStep(long pSequence, AWPathStep pStep)
            {
                Sequence = pSequence;
                Step = pStep;
            }

            public long Sequence { get; }
            public AWPathStep Step { get; }
        }

        private sealed class MarchState
        {
            public MarchState(long pArmyId, int pTargetTileId,
                int pGenerationId, bool pUsesProvider = false)
            {
                ArmyId = pArmyId;
                TargetTileId = pTargetTileId;
                GenerationId = pGenerationId;
                HasPlan = true;
                UsesProvider = pUsesProvider;
            }

            public readonly long ArmyId;
            public readonly int TargetTileId;
            public readonly int GenerationId;
            public readonly List<AWPathStep> Route = new List<AWPathStep>();
            public readonly List<LeaderTrailStep> LeaderTrail =
                new List<LeaderTrailStep>();
            public readonly Dictionary<long, long> FollowerCursorByActor =
                new Dictionary<long, long>();
            public readonly Dictionary<long, int> Slots = new Dictionary<long, int>();
            public readonly Queue<long> FollowerOrder = new Queue<long>();
            public readonly Dictionary<long, double> NextAllowedByActor =
                new Dictionary<long, double>();
            public readonly Dictionary<long, double>
                PendingFollowerStartedByActor = new Dictionary<long, double>();
            public readonly Dictionary<long, int>
                SharedRouteRevisionByActor = new Dictionary<long, int>();
            public readonly Dictionary<long, int>
                SharedRouteEndTileByActor = new Dictionary<long, int>();
            public readonly Dictionary<long, int>
                SharedRouteReconnectTileByActor =
                    new Dictionary<long, int>();
            public readonly Dictionary<long, int>
                SharedRouteAttemptRevisionByActor =
                    new Dictionary<long, int>();
            public readonly Dictionary<long, ArmySharedRouteInstallStatus>
                InstallStatusByActor =
                    new Dictionary<long, ArmySharedRouteInstallStatus>();
            public int Cursor;
            public int NextSlot = 1;
            public int DirectionX;
            public int DirectionY = 1;
            public long LeaderTrailBaseSequence;
            public long LeaderTrailNextSequence;
            public bool LeaderTrailCompleted;
            public string RetainedDeploymentAssignmentKey = "";
            public bool HasPlan;
            public readonly bool UsesProvider;
            public bool ProviderComplete;
            public bool ContainsTransportStep;
            public float LandRouteCost;
            public int SharedRouteRevision;
            public bool LandTrailPausedForTransport;
        }
    }
}
