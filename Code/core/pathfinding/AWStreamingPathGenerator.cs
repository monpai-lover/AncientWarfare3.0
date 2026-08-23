// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWStreamingPathGenerator : IAWPathSegmentGenerator
    {
        private const float Epsilon = 0.001f;
        [ThreadStatic] private static SearchWorkspace _threadWorkspace;
        [ThreadStatic] private static ReusablePathSteps _threadStraightPath;
        private readonly AWPathfindingConfig _config;
        private readonly AWRegionRouteCache _regionRouteCache;

        public AWStreamingPathGenerator(AWPathfindingConfig pConfig = null)
        {
            _config = pConfig ?? AWPathfindingConfig.Default;
            _regionRouteCache = new AWRegionRouteCache(_config.RegionRouteCacheSize);
        }

        public void Generate(AWPathRequest pRequest, CancellationToken pCancellation)
        {
            AWPathGenerationResult result = GenerateSegment(pRequest,
                pCancellation, int.MaxValue);
            if (pRequest == null) return;
            if (!result.Succeeded)
            {
                if (result.FailureReason ==
                    AWPathFailureReason.CancelledByNewRequest)
                    pRequest.Stream.Cancel(result.FailureReason);
                else
                    pRequest.Stream.Fail(result.FailureReason, result.Error);
                return;
            }

            for (int index = 0; index < result.Steps.Count; index++)
                if (!pRequest.Stream.AddStep(result.Steps[index])) return;
            // The legacy whole-path entry point treats a bounded region result
            // as a complete request. The session scheduler uses
            // GenerateSegment directly and keeps that stream open for the
            // low-watermark continuation.
            if (result.ReachedTarget || result.Steps.Count > 0)
                pRequest.Stream.Complete();
        }

        public AWPathGenerationResult GenerateSegment(AWPathRequest pRequest,
            CancellationToken pCancellation, int pMaximumSteps)
        {
            if (pRequest == null)
                return AWPathGenerationResult.Failure(
                    AWPathFailureReason.InvalidTarget);
            SearchWorkspace workspace = _threadWorkspace ??=
                new SearchWorkspace(OpenEntryComparer.Instance);
            try
            {
                pCancellation.ThrowIfCancellationRequested();
                long currentTerrainRevision;
                long currentWorldGeneration;
#if AW3_RULES_TESTS
                // Rule tests intentionally link only the pure pathfinding
                // model. The request snapshot is the authoritative revision
                // in that environment; production uses the live world state.
                currentTerrainRevision = pRequest.TerrainRevision;
                currentWorldGeneration = pRequest.ReuseKey.WorldGeneration;
#else
                currentTerrainRevision = AWPathfindingBootstrap.Cache?.
                    SourceRevision ?? pRequest.TerrainRevision;
                currentWorldGeneration =
                    AncientWarfare3.core.asyncwork.AWAsyncRuntime.WorldGeneration;
#endif
                if (pRequest.TryTakeCachedSegment(pMaximumSteps,
                        currentTerrainRevision, currentWorldGeneration,
                        pRequest.InsideBoat,
                        out IReadOnlyList<AWPathStep> cachedSteps,
                        out bool cachedComplete))
                {
                    int cachedEnd = cachedSteps.Count > 0
                        ? cachedSteps[cachedSteps.Count - 1].TileId
                        : pRequest.StartTileId;
                    return AWPathGenerationResult.Success(cachedEnd,
                        cachedComplete, cachedSteps);
                }
                if (!pRequest.Generation.TryGet(pRequest.StartTileId, out AWTileTraversalSnapshot start))
                    return AWPathGenerationResult.Failure(
                        AWPathFailureReason.InvalidStart);
                if (!pRequest.Generation.TryGet(pRequest.TargetTileId, out AWTileTraversalSnapshot target))
                    return AWPathGenerationResult.Failure(
                        AWPathFailureReason.InvalidTarget);
                if (pRequest.StartTileId == pRequest.TargetTileId)
                    return AWPathGenerationResult.Success(
                        pRequest.TargetTileId, true,
                        Array.Empty<AWPathStep>());
                if (pRequest.Options.BoundedMilitaryWater &&
                    (target.Liquid || target.Ocean))
                    return AWPathGenerationResult.Failure(
                        AWPathFailureReason.Unreachable);
                if (!AWTraversalRules.CanEnter(target, pRequest.Profile, pRequest.Options))
                    return AWPathGenerationResult.Failure(
                        AWPathFailureReason.Unreachable);

                // A physical boat is an explicit movement mode. Decide this
                // before land A* so a cross-island request cannot spend its
                // entire node budget searching disconnected terrain.
                if (pRequest.PhysicalTransportAvailable &&
                    CanUseVanillaTransport(start, target, pRequest.Profile) &&
                    !AWNarrowWaterRecoveryRules.ShouldTryBoundedCrossingBeforeTransport(
                        pRequest.Profile.IsMilitary, pRequest.Options.BoundedMilitaryWater))
                {
                    var transportEstimate = new AWTraversalEstimate(0f, 0f, 0f, 0f,
                        AWHazardFlags.Transport);
                    return AWPathGenerationResult.Success(target.Id, true,
                        new[] { new AWPathStep(target.Id, AWMovementMethod.Transport,
                            transportEstimate) });
                }

                // ArmyRouteProvider marks RTS/P0 routes as bounded military
                // water. Keep those requests on their established A* and
                // boarding path so actor-path optimizations cannot alter RTS
                // movement, landing, or return-home behavior.
                bool useCultiwayActorRouting =
                    !pRequest.Options.BoundedMilitaryWater;
                if (useCultiwayActorRouting &&
                    TryBuildStraightSegment(pRequest, start, target,
                        Math.Min(Math.Max(1, pMaximumSteps),
                            _config.SegmentTargetSteps), _config,
                        out IReadOnlyList<AWPathStep> straightSteps,
                        out int straightEnd))
                {
                    bool straightReachedTarget = straightEnd == target.Id;
                    return AWPathGenerationResult.Success(straightEnd, straightReachedTarget,
                        straightSteps);
                }
                float direct = AWTraversalRules.Distance(start.X, start.Y,
                    target.X, target.Y);
                // Cultiway selects routing stages using grid (Manhattan)
                // distance.  Using Euclidean distance here leaves diagonal
                // requests in the expensive destination-wide A* stage even
                // when their grid distance already requires a corridor.
                int stageDistance = Math.Abs(target.X - start.X) +
                    Math.Abs(target.Y - start.Y);
                bool longRange = stageDistance > _config.ShortRangeTiles;
                int primaryLimit = longRange
                    ? _config.MaxNodesLong
                    : _config.MaxNodesShort;
                int searchTargetId = target.Id;
                AWRegionCorridor corridor = null;
                if (stageDistance > _config.LongRangeTiles &&
                    useCultiwayActorRouting)
                    ResolveLongRangeSearch(pRequest, start, target,
                        out searchTargetId, out corridor);
                // Match Cultiway's routing weights: accurate short-range A*
                // and a small long-range heuristic bias. RTS bounded-water
                // requests stay on this established generator but never use
                // the ordinary actor corridor or straight-line shortcuts.
                float heuristicWeight = longRange
                    ? Math.Max(1f, _config.LongRangeHeuristicWeight)
                    : 1f;
                SearchResult result = Search(pRequest, start, target, searchTargetId,
                    Math.Max(1, primaryLimit), float.PositiveInfinity,
                    corridor, heuristicWeight,
                    pCancellation, workspace);
#if !AW3_RULES_TESTS
                AWPathfindingBootstrap.PathDiagnostics.AddExpandedNodes(result.ExpandedNodes);
#endif
                bool recoveryAllowed = useCultiwayActorRouting &&
                    !result.Success &&
                    pRequest.TryBeginRecovery(target.Id,
                        currentTerrainRevision, currentWorldGeneration);
                if (recoveryAllowed && corridor != null)
                {
                    AWRegionCorridor widened = corridor.Expand();
                    result = Search(pRequest, start, target, searchTargetId,
                        Math.Max(1, primaryLimit), float.PositiveInfinity,
                        widened, heuristicWeight, pCancellation, workspace);
#if !AW3_RULES_TESTS
                    AWPathfindingBootstrap.PathDiagnostics.AddExpandedNodes(result.ExpandedNodes);
#endif
                    if (result.Success) corridor = widened;
                }
                if (!result.Success && result.HitNodeLimit && longRange &&
                    recoveryAllowed)
                {
#if !AW3_RULES_TESTS
                    AWPathfindingBootstrap.PathDiagnostics.OnFallback();
#endif
                    float detour = Math.Max(_config.FallbackCorridorMinDetour,
                        direct * _config.FallbackCorridorDetourScale);
                    result = Search(pRequest, start, target, searchTargetId,
                        Math.Max(_config.MaxNodesLongFallback, _config.MaxNodesLong),
                        direct + detour, corridor, heuristicWeight,
                        pCancellation, workspace);
#if !AW3_RULES_TESTS
                    AWPathfindingBootstrap.PathDiagnostics.AddExpandedNodes(result.ExpandedNodes);
#endif
                }

                if (!result.Success)
                {
                    return AWPathGenerationResult.Failure(result.HitNodeLimit
                        ? AWPathFailureReason.SearchLimitExceeded
                        : AWPathFailureReason.Unreachable);
                }

                int stepCount = workspace.BuildPath(result.NodeIndex);
                int maximumSteps = Math.Max(1, pMaximumSteps);
                int outputCount = Math.Min(stepCount, maximumSteps);
                if (outputCount == stepCount)
                {
                    return AWPathGenerationResult.Success(pEndTileId: stepCount > 0
                            ? workspace.PathStep(stepCount - 1).TileId
                            : pRequest.StartTileId,
                        pReachedTarget: result.ReachedTarget &&
                            searchTargetId == target.Id,
                        pSteps: workspace.PathView(outputCount));
                }
                // A region lookahead is an intermediate objective. Its cached
                // route must never be reported as completion of the request's
                // actual target.
                var fullRoute = new AWPathStep[stepCount];
                for (int i = 0; i < stepCount; i++)
                {
                    pCancellation.ThrowIfCancellationRequested();
                    fullRoute[i] = workspace.PathStep(i);
                }
                // Cache every route that is split across segments. A region
                // waypoint is deliberately marked non-terminal; after its
                // cached steps are consumed the next segment searches toward
                // the actual request target.
                pRequest.CacheRoute(fullRoute, outputCount,
                    searchTargetId == target.Id, currentTerrainRevision,
                    currentWorldGeneration, pRequest.InsideBoat);
                IReadOnlyList<AWPathStep> steps = workspace.PathView(outputCount);
                int endTileId = outputCount > 0
                    ? steps[outputCount - 1].TileId
                    : pRequest.StartTileId;
                bool reachedTarget = result.ReachedTarget && searchTargetId == target.Id &&
                    outputCount == stepCount;
                return AWPathGenerationResult.Success(endTileId,
                    reachedTarget, steps);
            }
            catch (OperationCanceledException)
            {
                return AWPathGenerationResult.Failure(
                    AWPathFailureReason.CancelledByNewRequest);
            }
            catch (Exception error)
            {
                return AWPathGenerationResult.Failure(
                    AWPathFailureReason.GeneratorException, error);
            }
        }

        private static bool CanUseVanillaTransport(AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, AWActorTraversalProfile pProfile)
        {
            return !pProfile.CanFly && !pProfile.IsBoat && !pProfile.IsWaterCreature &&
                   !pStart.Liquid && !pStart.Ocean &&
                   !pTarget.Liquid && !pTarget.Ocean && pTarget.Ground &&
                   pStart.IslandId >= 0 && pTarget.IslandId >= 0 &&
                   pStart.IslandId != pTarget.IslandId;
        }

        private void ResolveLongRangeSearch(AWPathRequest pRequest,
            AWTileTraversalSnapshot pStart, AWTileTraversalSnapshot pTarget,
            out int pSearchTargetId, out AWRegionCorridor pCorridor)
        {
            pSearchTargetId = ResolveGeometricWaypoint(pRequest.Generation,
                pStart, pTarget, _config.RegionCorridorLookaheadTiles);
            pCorridor = null;
            AWRegionTopologySnapshot topology = pRequest.Generation.RegionTopology;
            if (topology == null || pStart.RegionId < 0 || pTarget.RegionId < 0)
                return;
            int[] route = _regionRouteCache.GetOrBuild(pRequest.Generation,
                pStart.Id, pTarget.Id, ResolveTraversalClass(pRequest));
            if (route == null || route.Length == 0) return;
            int lookahead = ResolveRegionLookahead(pRequest, pStart, route,
                _config.RegionCorridorLookaheadTiles);
            if (lookahead >= 0 && lookahead != pStart.Id)
                pSearchTargetId = lookahead;
            pCorridor = AWRegionCorridor.Create(topology, route);
        }

        private static int ResolveGeometricWaypoint(
            AWTraversalGeneration pGeneration,
            AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, int pLookaheadTiles)
        {
            int distance = Math.Abs(pTarget.X - pStart.X) +
                           Math.Abs(pTarget.Y - pStart.Y);
            int lookahead = Math.Max(1, pLookaheadTiles);
            if (distance <= lookahead) return pTarget.Id;

            float ratio = lookahead / (float)distance;
            int x = (int)Math.Round(pStart.X +
                (pTarget.X - pStart.X) * ratio);
            int y = (int)Math.Round(pStart.Y +
                (pTarget.Y - pStart.Y) * ratio);
            if (TryGetAt(pGeneration, x, y,
                    out AWTileTraversalSnapshot waypoint))
                return waypoint.Id;

            for (int radius = 1; radius <= 3; radius++)
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    continue;
                if (TryGetAt(pGeneration, x + dx, y + dy, out waypoint))
                    return waypoint.Id;
            }
            return pTarget.Id;
        }

        private static int ResolveRegionLookahead(AWPathRequest pRequest,
            AWTileTraversalSnapshot pStart, int[] pRoute, int pLookaheadTiles)
        {
            int selected = -1;
            AWRegionTopologySnapshot topology = pRequest.Generation.RegionTopology;
            for (int i = 1; i < pRoute.Length; i++)
            {
                if (!topology.TryGetRegion(pRoute[i], out AWRegionNode region)) continue;
                int candidate = pRequest.Profile.IsBoat
                    ? region.CenterTileId
                    : region.LandTileId >= 0 ? region.LandTileId : region.CenterTileId;
                if (candidate < 0 ||
                    !pRequest.Generation.TryGet(candidate, out AWTileTraversalSnapshot tile) ||
                    !AWTraversalRules.CanEnter(tile, pRequest.Profile, pRequest.Options))
                    continue;
                selected = candidate;
                if (AWTraversalRules.Distance(pStart.X, pStart.Y, tile.X, tile.Y) >=
                    Math.Max(1, pLookaheadTiles)) break;
            }
            return selected;
        }

        private static int ResolveTraversalClass(AWPathRequest pRequest)
        {
            int result = 0;
            if (pRequest.Profile.IsBoat) result |= 1;
            if (pRequest.Profile.IsWaterCreature) result |= 1 << 1;
            if (pRequest.Profile.CanFly) result |= 1 << 2;
            if (pRequest.Options.WalkOnBlocks) result |= 1 << 3;
            if (pRequest.Options.PathOnWater) result |= 1 << 4;
            if (pRequest.Options.WalkOnLava) result |= 1 << 5;
            return result;
        }

        private static bool TryBuildStraightSegment(AWPathRequest pRequest,
            AWTileTraversalSnapshot pStart, AWTileTraversalSnapshot pTarget,
            int pMaximumSteps, AWPathfindingConfig pConfig,
            out IReadOnlyList<AWPathStep> pSteps, out int pEndTileId)
        {
            pMaximumSteps = Math.Max(1, pMaximumSteps);
            ReusablePathSteps buffer = _threadStraightPath ??=
                new ReusablePathSteps(pMaximumSteps);
            buffer.Clear();
            int count = 0;
            int x = pStart.X, y = pStart.Y;
            int targetX = pTarget.X, targetY = pTarget.Y;
            int dx = Math.Abs(targetX - x), dy = Math.Abs(targetY - y);
            int sx = x < targetX ? 1 : -1, sy = y < targetY ? 1 : -1;
            int error = dx - dy, currentId = pStart.Id;
            while ((x != targetX || y != targetY) && count < pMaximumSteps)
            {
                int previousX = x, previousY = y, doubled = error * 2;
                if (doubled > -dy) { error -= dy; x += sx; }
                if (doubled < dx) { error += dx; y += sy; }
                if (!TryGetAt(pRequest.Generation, x, y, out AWTileTraversalSnapshot next) ||
                    !IsFastTileSafe(next, pRequest.Profile, pRequest.Options))
                { pSteps = null; pEndTileId = pStart.Id; return false; }
                bool diagonal = previousX != x && previousY != y;
                if (diagonal && (!TryGetAt(pRequest.Generation, x, previousY, out AWTileTraversalSnapshot sideX) ||
                    !TryGetAt(pRequest.Generation, previousX, y, out AWTileTraversalSnapshot sideY) ||
                    !IsFastTileSafe(sideX, pRequest.Profile, pRequest.Options) ||
                    !IsFastTileSafe(sideY, pRequest.Profile, pRequest.Options)))
                { pSteps = null; pEndTileId = pStart.Id; return false; }
                pRequest.Generation.TryGet(currentId, out AWTileTraversalSnapshot current);
                AWTraversalEstimate estimate = AWTraversalRules.Estimate(current, next,
                    pRequest.Profile, pRequest.Options, pConfig);
                AWMovementMethod method = pRequest.Profile.IsBoat
                    ? AWMovementMethod.Sail
                    : next.Liquid || next.Ocean
                        ? AWMovementMethod.Swim
                        : AWMovementMethod.Walk;
                buffer.Add(new AWPathStep(next.Id, method,
                    estimate, pPlannedTileFlags: AWPathTileFlagsExtensions.FromSnapshot(next)));
                count++;
                currentId = next.Id;
            }
            pEndTileId = currentId;
            pSteps = buffer;
            return count > 0 || pStart.Id == pTarget.Id;
        }

        private static bool TryGetAt(AWTraversalGeneration pGeneration, int pX, int pY,
            out AWTileTraversalSnapshot pTile)
        {
            pTile = default;
            if (pGeneration == null || pX < 0 || pY < 0 || pX >= pGeneration.Width || pY >= pGeneration.Height)
                return false;
            return pGeneration.TryGet(pY * pGeneration.Width + pX, out pTile);
        }

        private static bool IsFastTileSafe(AWTileTraversalSnapshot pTile,
            AWActorTraversalProfile pProfile, AWPathRequestOptions pOptions)
        {
            if (!AWTraversalRules.CanEnter(pTile, pProfile, pOptions) || !pTile.HasType || pTile.DamageUnits)
                return false;
            if (pProfile.IsBoat)
                return (pTile.Ocean || pTile.GoodForBoat) && !pTile.Lava && !pTile.Block;
            if (pTile.Block && !pOptions.WalkOnBlocks && !pProfile.CanFly) return false;
            if (pTile.Lava && pProfile.DiesInLava && !pOptions.WalkOnLava) return false;
            if (pTile.Fire && !pProfile.ImmuneToFire && !pProfile.Burning) return false;
            // The fast segment does not carry the A* water-run and drowning
            // state. Land actors therefore keep using the full search for any
            // liquid traversal, including bounded military crossings.
            if ((pTile.Liquid || pTile.Ocean) && !pProfile.CanFly &&
                !pProfile.IsWaterCreature) return false;
            return true;
        }

        private sealed class AWRegionCorridor
        {
            private readonly HashSet<int> _regionIds;
            private readonly AWRegionTopologySnapshot _topology;
            private readonly int _expansionDepth;

            private AWRegionCorridor(HashSet<int> pRegionIds,
                AWRegionTopologySnapshot pTopology, int pExpansionDepth)
            {
                _regionIds = pRegionIds;
                _topology = pTopology;
                _expansionDepth = pExpansionDepth;
            }

            internal bool Contains(int pRegionId)
            {
                // Unclassified tiles are deliberately left unrestricted; this
                // preserves legacy maps whose region snapshot is incomplete.
                return pRegionId < 0 || _regionIds.Contains(pRegionId);
            }

            internal static AWRegionCorridor Create(
                AWRegionTopologySnapshot pTopology, int[] pRoute)
            {
                var ids = new HashSet<int>(pRoute ?? Array.Empty<int>());
                AddNeighbourRing(pTopology, ids);
                return new AWRegionCorridor(ids, pTopology, 1);
            }

            internal AWRegionCorridor Expand()
            {
                if (_expansionDepth >= 2) return this;
                var ids = new HashSet<int>(_regionIds);
                AddNeighbourRing(_topology, ids);
                return new AWRegionCorridor(ids, _topology, _expansionDepth + 1);
            }

            private static void AddNeighbourRing(AWRegionTopologySnapshot pTopology,
                HashSet<int> pIds)
            {
                if (pTopology == null || pIds == null) return;
                int[] source = new int[pIds.Count];
                pIds.CopyTo(source);
                for (int i = 0; i < source.Length; i++)
                {
                    if (!pTopology.TryGetRegion(source[i], out AWRegionNode region)) continue;
                    for (int n = 0; n < region.Neighbours.Length; n++)
                        pIds.Add(region.Neighbours[n]);
                }
            }
        }

        private SearchResult Search(AWPathRequest pRequest, AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pFinalTarget, int pSearchTargetId,
            int pMaxNodes, float pCorridorLimit,
            AWRegionCorridor pRegionCorridor, float pHeuristicWeight,
            CancellationToken pCancellation, SearchWorkspace pWorkspace)
        {
            if (!pRequest.Generation.TryGet(pSearchTargetId, out AWTileTraversalSnapshot searchTarget))
                return SearchResult.Failure(false, 0);
            pWorkspace.Reset(pMaxNodes, _config.MaxLabelsPerTile);
            float startHeuristic = Heuristic(pStart, searchTarget,
                pRequest.Profile) * Math.Max(1f, pHeuristicWeight);
            int startIndex = pWorkspace.AddStart(SearchNode.Start(pStart.Id,
                startHeuristic, pStart.Liquid || pStart.Ocean ? 1 : 0));
            pWorkspace.Open.Enqueue(new OpenEntry(startIndex, startHeuristic,
                startHeuristic));

            int expanded = 0;
            int segmentIndex = -1;
            while (pWorkspace.Open.Count > 0 && expanded < pMaxNodes)
            {
                if ((expanded & 63) == 0) pCancellation.ThrowIfCancellationRequested();
                OpenEntry entry = pWorkspace.Open.Dequeue();
                SearchNode current = pWorkspace.Node(entry.NodeIndex);
                if (!pWorkspace.IsActive(current.TileId, entry.NodeIndex)) continue;
                expanded++;
                if (current.TileId == searchTarget.Id)
                    return SearchResult.SuccessResult(entry.NodeIndex,
                        pReachedTarget: true, expanded);
                if (pRequest.Options.LimitPathfindingRegions > 0 &&
                    current.RegionTransitions >=
                    pRequest.Options.LimitPathfindingRegions)
                {
                    if (segmentIndex < 0 ||
                        PreferSegmentCandidate(current,
                            pWorkspace.Node(segmentIndex)))
                        segmentIndex = entry.NodeIndex;
                    continue;
                }
                if (!pRequest.Generation.TryGet(current.TileId, out AWTileTraversalSnapshot currentTile))
                    continue;

                for (int i = 0; i < currentTile.NeighborCount; i++)
                {
                    int neighborId = currentTile.GetNeighbor(i);
                    if (!pRequest.Generation.TryGet(neighborId, out AWTileTraversalSnapshot neighbor))
                        continue;
                    if (!AWTraversalRules.CanEnter(neighbor, pRequest.Profile, pRequest.Options)) continue;
                    if (pRegionCorridor != null && neighbor.Id != pRequest.TargetTileId &&
                        !pRegionCorridor.Contains(neighbor.RegionId)) continue;
                    if (!float.IsPositiveInfinity(pCorridorLimit) &&
                        !AWTraversalRules.IsInsideFallbackCorridor(neighbor.X, neighbor.Y,
                            pStart.X, pStart.Y, pFinalTarget.X, pFinalTarget.Y,
                            pCorridorLimit - AWTraversalRules.Distance(pStart.X,
                                pStart.Y, pFinalTarget.X, pFinalTarget.Y)))
                        continue;

                    AWTraversalEstimate estimate = AWTraversalRules.Estimate(currentTile, neighbor,
                        pRequest.Profile, pRequest.Options, _config);
                    if (float.IsInfinity(estimate.RiskCost)) continue;
                    bool enteringWater = neighbor.Liquid || neighbor.Ocean;
                    float nextHealthCost = current.HealthCost +
                                           estimate.HealthCost;
                    if (pRequest.Options.BoundedMilitaryWater &&
                        (!AWNarrowWaterRecoveryRules.CanAdvance(
                             current.WaterRun, enteringWater,
                             nextHealthCost >= pRequest.Profile.Health,
                             neighbor.Lava) ||
                         enteringWater && current.WaterRun >=
                         pRequest.Options.MaximumConsecutiveWaterTiles))
                        continue;
                    float time = current.Time + estimate.TimeSeconds;
                    float stamina = current.StaminaCost + estimate.StaminaCost;
                    float health = current.HealthCost + estimate.HealthCost;
                    float risk = current.Risk + estimate.RiskCost;
                    float g = time + risk;
                    float h = Heuristic(neighbor, searchTarget,
                        pRequest.Profile) * Math.Max(1f, pHeuristicWeight);
                    AWMovementMethod method = pRequest.Profile.IsBoat
                        ? AWMovementMethod.Sail
                        : neighbor.Liquid || neighbor.Ocean
                            ? AWMovementMethod.Swim
                            : AWMovementMethod.Walk;
                    int regionTransitions = current.RegionTransitions +
                        (currentTile.RegionId >= 0 && neighbor.RegionId >= 0 &&
                         currentTile.RegionId != neighbor.RegionId ? 1 : 0);
                    var node = new SearchNode(neighbor.Id, entry.NodeIndex, method, estimate,
                        time, stamina, health, risk, g, h,
                        AWNarrowWaterRecoveryRules.NextWaterRun(
                            current.WaterRun, enteringWater),
                        regionTransitions);
                    if (!pWorkspace.TryAddLabel(node,
                            pRequest.Options.LimitPathfindingRegions > 0,
                            out int nodeIndex))
                        continue;
                    pWorkspace.Open.Enqueue(new OpenEntry(nodeIndex, node.F, h));
                }
            }
            if (segmentIndex >= 0)
                return SearchResult.SuccessResult(segmentIndex,
                    pReachedTarget: false, expanded);
            return SearchResult.Failure(pWorkspace.Open.Count > 0 &&
                                        expanded >= pMaxNodes, expanded);
        }

        private static bool PreferSegmentCandidate(SearchNode pCandidate,
            SearchNode pCurrent)
        {
            int heuristic = pCandidate.H.CompareTo(pCurrent.H);
            return heuristic < 0 || heuristic == 0 &&
                   pCandidate.F < pCurrent.F;
        }

        private static bool Dominates(SearchNode pLeft, SearchNode pRight,
            bool pRegionBounded)
        {
            return pLeft.G <= pRight.G + Epsilon &&
                   pLeft.StaminaCost <= pRight.StaminaCost + Epsilon &&
                   pLeft.HealthCost <= pRight.HealthCost + Epsilon &&
                   pLeft.Risk <= pRight.Risk + Epsilon &&
                   pLeft.WaterRun <= pRight.WaterRun &&
                   (!pRegionBounded || pLeft.RegionTransitions <=
                    pRight.RegionTransitions);
        }

        private static float Heuristic(AWTileTraversalSnapshot pFrom,
            AWTileTraversalSnapshot pTarget, AWActorTraversalProfile pProfile)
        {
            return AWTraversalRules.Distance(pFrom.X, pFrom.Y, pTarget.X, pTarget.Y) /
                   Math.Max(0.01f, pProfile.MovementSpeed);
        }

        private readonly struct SearchNode
        {
            public SearchNode(int pTileId, int pParentIndex, AWMovementMethod pMethod,
                AWTraversalEstimate pEstimate, float pTime, float pStaminaCost,
                float pHealthCost, float pRisk, float pG, float pH,
                int pWaterRun, int pRegionTransitions)
            {
                TileId = pTileId;
                ParentIndex = pParentIndex;
                Method = pMethod;
                Estimate = pEstimate;
                Time = pTime;
                StaminaCost = pStaminaCost;
                HealthCost = pHealthCost;
                Risk = pRisk;
                G = pG;
                H = pH;
                WaterRun = Math.Max(0, pWaterRun);
                RegionTransitions = Math.Max(0, pRegionTransitions);
            }

            public int TileId { get; }
            public int ParentIndex { get; }
            public AWMovementMethod Method { get; }
            public AWTraversalEstimate Estimate { get; }
            public float Time { get; }
            public float StaminaCost { get; }
            public float HealthCost { get; }
            public float Risk { get; }
            public float G { get; }
            public float H { get; }
            public int WaterRun { get; }
            public int RegionTransitions { get; }
            public float F => G + H;

            public static SearchNode Start(int pTileId, float pH,
                int pWaterRun)
            {
                return new SearchNode(pTileId, -1, AWMovementMethod.Walk, default,
                    0f, 0f, 0f, 0f, 0f, pH, pWaterRun, 0);
            }
        }

        private readonly struct OpenEntry
        {
            public OpenEntry(int pNodeIndex, float pF, float pH)
            {
                NodeIndex = pNodeIndex;
                F = pF;
                H = pH;
            }

            public int NodeIndex { get; }
            public float F { get; }
            public float H { get; }
        }

        private sealed class OpenEntryComparer : IComparer<OpenEntry>
        {
            public static readonly OpenEntryComparer Instance = new OpenEntryComparer();

            public int Compare(OpenEntry pLeft, OpenEntry pRight)
            {
                int value = pLeft.F.CompareTo(pRight.F);
                if (value != 0) return value;
                value = pLeft.H.CompareTo(pRight.H);
                return value != 0 ? value : pLeft.NodeIndex.CompareTo(pRight.NodeIndex);
            }
        }

        private sealed class ReusablePathSteps : IReadOnlyList<AWPathStep>
        {
            private AWPathStep[] _items;
            private int _count;

            internal ReusablePathSteps(int pCapacity)
            {
                _items = new AWPathStep[Math.Max(1, pCapacity)];
            }

            internal void Clear() => _count = 0;

            internal void EnsureCapacity(int pCount)
            {
                if (pCount <= _items.Length) return;
                int capacity = _items.Length;
                while (capacity < pCount) capacity *= 2;
                Array.Resize(ref _items, capacity);
            }

            internal void SetCount(int pCount)
            {
                EnsureCapacity(pCount);
                _count = Math.Max(0, pCount);
            }

            internal void Add(AWPathStep pStep)
            {
                EnsureCapacity(_count + 1);
                _items[_count++] = pStep;
            }

            public int Count => _count;

            public AWPathStep this[int pIndex]
            {
                get
                {
                    if (pIndex < 0 || pIndex >= _count)
                        throw new ArgumentOutOfRangeException(nameof(pIndex));
                    return _items[pIndex];
                }
                internal set
                {
                    if (pIndex < 0 || pIndex >= _count)
                        throw new ArgumentOutOfRangeException(nameof(pIndex));
                    _items[pIndex] = value;
                }
            }

            IEnumerator<AWPathStep> IEnumerable<AWPathStep>.GetEnumerator()
            {
                return ((IEnumerable<AWPathStep>)new ArraySegment<AWPathStep>(
                    _items, 0, _count)).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<AWPathStep>)this).GetEnumerator();
            }
        }

        private sealed class SearchWorkspace
        {
            private const int InitialNodeCapacity = 256;
            private const int InitialPathCapacity = 64;
            private SearchNode[] _nodes = new SearchNode[InitialNodeCapacity];
            private readonly ReusablePathSteps _path =
                new ReusablePathSteps(InitialPathCapacity);
            private int[] _labelKeys = Array.Empty<int>();
            private int[] _labelEpochs = Array.Empty<int>();
            private byte[] _labelCounts = Array.Empty<byte>();
            private int[] _labelNodeIndices = Array.Empty<int>();
            private int _labelMask;
            private int _labelStride = 1;
            private int _labelSlotCount;
            private int _epoch;
            private int _nodeCount;

            public SearchWorkspace(IComparer<OpenEntry> pComparer)
            {
                Open = new AWBinaryHeap<OpenEntry>(128, pComparer);
            }

            public AWBinaryHeap<OpenEntry> Open { get; }

            public void Reset(int pMaxNodes, int pMaxLabelsPerTile)
            {
                _nodeCount = 0;
                Open.Clear();
                int stride = Math.Max(1, Math.Min(byte.MaxValue,
                    pMaxLabelsPerTile));
                int requestedNodes = Math.Max(1, pMaxNodes);
                int nodeCapacity = requestedNodes >
                    (int.MaxValue - 8) / stride
                    ? int.MaxValue
                    : requestedNodes * stride + 8;
                if (_nodes.Length < nodeCapacity)
                    Array.Resize(ref _nodes, nodeCapacity);
                Open.EnsureCapacity(nodeCapacity);
                int minimumSlots = Math.Max(16,
                    NextPowerOfTwo(requestedNodes * 2));
                if (_labelKeys.Length < minimumSlots ||
                    _labelStride != stride)
                {
                    _labelKeys = new int[minimumSlots];
                    _labelEpochs = new int[minimumSlots];
                    _labelCounts = new byte[minimumSlots];
                    _labelNodeIndices = new int[minimumSlots * stride];
                    _labelMask = minimumSlots - 1;
                    _labelStride = stride;
                    _labelSlotCount = 0;
                    _epoch = 1;
                    return;
                }

                if (_epoch == int.MaxValue)
                {
                    Array.Clear(_labelEpochs, 0, _labelEpochs.Length);
                    _labelSlotCount = 0;
                    _epoch = 1;
                }
                else
                {
                    _epoch++;
                    _labelSlotCount = 0;
                }
            }

            public int AddStart(SearchNode pNode)
            {
                int nodeIndex = AddNode(pNode);
                int slot = FindSlot(pNode.TileId, pCreate: true);
                _labelNodeIndices[slot * _labelStride] = nodeIndex;
                _labelCounts[slot] = 1;
                return nodeIndex;
            }

            public bool TryAddLabel(SearchNode pNode, bool pRegionBounded,
                out int pNodeIndex)
            {
                pNodeIndex = -1;
                int slot = FindSlot(pNode.TileId, pCreate: true);
                int offset = slot * _labelStride;
                int count = _labelCounts[slot];
                for (int i = 0; i < count; i++)
                {
                    SearchNode existing = _nodes[_labelNodeIndices[offset + i]];
                    if (Dominates(existing, pNode, pRegionBounded)) return false;
                }

                for (int i = count - 1; i >= 0; i--)
                {
                    SearchNode existing = _nodes[_labelNodeIndices[offset + i]];
                    if (!Dominates(pNode, existing, pRegionBounded)) continue;
                    count--;
                    _labelNodeIndices[offset + i] =
                        _labelNodeIndices[offset + count];
                }

                if (count >= _labelStride)
                {
                    int worst = 0;
                    float worstF = _nodes[_labelNodeIndices[offset]].F;
                    for (int i = 1; i < count; i++)
                    {
                        float candidateF =
                            _nodes[_labelNodeIndices[offset + i]].F;
                        if (candidateF <= worstF) continue;
                        worst = i;
                        worstF = candidateF;
                    }
                    if (pNode.F > worstF) return false;
                    count--;
                    _labelNodeIndices[offset + worst] =
                        _labelNodeIndices[offset + count];
                }

                pNodeIndex = AddNode(pNode);
                _labelNodeIndices[offset + count] = pNodeIndex;
                _labelCounts[slot] = (byte)(count + 1);
                return true;
            }

            public bool IsActive(int pTileId, int pNodeIndex)
            {
                int slot = FindSlot(pTileId, pCreate: false);
                if (slot < 0) return false;
                int offset = slot * _labelStride;
                int count = _labelCounts[slot];
                for (int i = 0; i < count; i++)
                    if (_labelNodeIndices[offset + i] == pNodeIndex) return true;
                return false;
            }

            public SearchNode Node(int pIndex) => _nodes[pIndex];

            public int BuildPath(int pNodeIndex)
            {
                int count = 0;
                SearchNode node = _nodes[pNodeIndex];
                while (node.ParentIndex >= 0)
                {
                    count++;
                    node = _nodes[node.ParentIndex];
                }
                _path.SetCount(count);
                int write = count;
                node = _nodes[pNodeIndex];
                while (node.ParentIndex >= 0)
                {
                    _path[--write] = new AWPathStep(node.TileId,
                        node.Method, node.Estimate);
                    node = _nodes[node.ParentIndex];
                }
                return count;
            }

            public AWPathStep PathStep(int pIndex) => _path[pIndex];

            public IReadOnlyList<AWPathStep> PathView(int pCount)
            {
                _path.SetCount(pCount);
                return _path;
            }

            private int AddNode(SearchNode pNode)
            {
                if (_nodeCount == _nodes.Length)
                    Array.Resize(ref _nodes, Math.Max(1, _nodes.Length * 2));
                int index = _nodeCount++;
                _nodes[index] = pNode;
                return index;
            }

            private int FindSlot(int pTileId, bool pCreate)
            {
                while (true)
                {
                    int slot = Mix(pTileId) & _labelMask;
                    for (int probe = 0; probe < _labelKeys.Length; probe++)
                    {
                        if (_labelEpochs[slot] != _epoch)
                        {
                            if (!pCreate) return -1;
                            if ((_labelSlotCount + 1) * 10 >=
                                _labelKeys.Length * 7)
                            {
                                GrowLabelTable();
                                break;
                            }
                            _labelEpochs[slot] = _epoch;
                            _labelKeys[slot] = pTileId;
                            _labelCounts[slot] = 0;
                            _labelSlotCount++;
                            return slot;
                        }
                        if (_labelKeys[slot] == pTileId) return slot;
                        slot = (slot + 1) & _labelMask;
                    }

                    if (!pCreate) return -1;
                    if (_labelSlotCount < _labelKeys.Length * 7 / 10)
                        continue;
                    GrowLabelTable();
                }
            }

            private void GrowLabelTable()
            {
                int[] oldKeys = _labelKeys;
                int[] oldEpochs = _labelEpochs;
                byte[] oldCounts = _labelCounts;
                int[] oldIndices = _labelNodeIndices;
                int oldEpoch = _epoch;
                int capacity = Math.Max(16, oldKeys.Length * 2);

                _labelKeys = new int[capacity];
                _labelEpochs = new int[capacity];
                _labelCounts = new byte[capacity];
                _labelNodeIndices = new int[capacity * _labelStride];
                _labelMask = capacity - 1;
                _labelSlotCount = 0;
                _epoch = 1;

                for (int oldSlot = 0; oldSlot < oldKeys.Length; oldSlot++)
                {
                    if (oldEpochs[oldSlot] != oldEpoch) continue;
                    int slot = Mix(oldKeys[oldSlot]) & _labelMask;
                    while (_labelEpochs[slot] == _epoch)
                        slot = (slot + 1) & _labelMask;
                    _labelEpochs[slot] = _epoch;
                    _labelKeys[slot] = oldKeys[oldSlot];
                    byte count = oldCounts[oldSlot];
                    _labelCounts[slot] = count;
                    Array.Copy(oldIndices, oldSlot * _labelStride,
                        _labelNodeIndices, slot * _labelStride, count);
                    _labelSlotCount++;
                }
            }

            private static int Mix(int pValue)
            {
                unchecked
                {
                    uint value = (uint)pValue;
                    value ^= value >> 16;
                    value *= 0x7feb352dU;
                    value ^= value >> 15;
                    value *= 0x846ca68bU;
                    value ^= value >> 16;
                    return (int)value;
                }
            }

            private static int NextPowerOfTwo(int pValue)
            {
                int value = 1;
                while (value < pValue && value < 1 << 29) value <<= 1;
                return value;
            }
        }

        private readonly struct SearchResult
        {
            private SearchResult(bool pSuccess, bool pHitNodeLimit,
                int pNodeIndex, bool pReachedTarget, int pExpandedNodes)
            {
                Success = pSuccess;
                HitNodeLimit = pHitNodeLimit;
                NodeIndex = pNodeIndex;
                ReachedTarget = pReachedTarget;
                ExpandedNodes = Math.Max(0, pExpandedNodes);
            }

            public bool Success { get; }
            public bool HitNodeLimit { get; }
            public int NodeIndex { get; }
            public bool ReachedTarget { get; }
            public int ExpandedNodes { get; }

            public static SearchResult SuccessResult(int pNodeIndex,
                bool pReachedTarget, int pExpandedNodes) =>
                new SearchResult(true, false, pNodeIndex, pReachedTarget,
                    pExpandedNodes);

            public static SearchResult Failure(bool pHitNodeLimit,
                int pExpandedNodes) =>
                new SearchResult(false, pHitNodeLimit, -1, false,
                    pExpandedNodes);
        }
    }
}
