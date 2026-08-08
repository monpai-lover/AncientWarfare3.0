// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWStreamingPathGenerator : IAWPathSegmentGenerator
    {
        private const float Epsilon = 0.001f;
        [ThreadStatic] private static SearchWorkspace _threadWorkspace;
        [ThreadStatic] private static AWRegionRouteCache _threadRegionRouteCache;
        private readonly AWPathfindingConfig _config;
        private readonly Func<AWPathRequest, bool> _isRequestCurrent;

        public AWStreamingPathGenerator(AWPathfindingConfig pConfig = null,
            Func<AWPathRequest, bool> pIsRequestCurrent = null)
        {
            _config = pConfig ?? AWPathfindingConfig.Default;
            _isRequestCurrent = pIsRequestCurrent;
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
                if (_isRequestCurrent != null &&
                    !_isRequestCurrent(pRequest))
                    return AWPathGenerationResult.Failure(
                        AWPathFailureReason.StaleTraversal);
                if (pRequest.TryTakeCachedSegment(pMaximumSteps,
                        out AWPathStep[] cachedSteps, out bool cachedComplete))
                {
                    int cachedEnd = cachedSteps.Length > 0
                        ? cachedSteps[cachedSteps.Length - 1].TileId
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

                float direct = AWTraversalRules.Distance(start.X, start.Y, target.X, target.Y);
                bool longRange = direct > _config.ShortRangeTiles;
                if (!longRange && pRequest.Options.LimitPathfindingRegions <= 0 &&
                    !pRequest.Options.BoundedMilitaryWater &&
                    direct <= Math.Max(1, _config.SegmentTargetSteps) &&
                    TryBuildDirectSegment(pRequest, start, target,
                        pCancellation, out AWPathStep[] directSteps))
                {
                    int maximum = Math.Max(1, pMaximumSteps);
                    int output = Math.Min(maximum, directSteps.Length);
                    var directOutput = new AWPathStep[output];
                    Array.Copy(directSteps, directOutput, output);
                    if (output < directSteps.Length)
                        pRequest.CacheRoute(directSteps, output, true);
                    int end = output > 0 ? directOutput[output - 1].TileId :
                        pRequest.StartTileId;
                    return AWPathGenerationResult.Success(end,
                        output == directSteps.Length, directOutput);
                }
                int primaryLimit = longRange ? _config.MaxNodesLong : _config.MaxNodesShort;
                AWTileTraversalSnapshot objective = target;
                AWRegionCorridor regionCorridor = null;
                float heuristicWeight = longRange
                    ? Math.Max(1f, _config.LongRangeHeuristicWeight)
                    : 1f;
                if (longRange)
                {
                    AWRegionRouteCache regionCache = _threadRegionRouteCache;
                    int capacity = Math.Max(1, _config.RegionRouteCacheSize);
                    if (regionCache == null || regionCache.Capacity != capacity)
                    {
                        regionCache = new AWRegionRouteCache(capacity);
                        _threadRegionRouteCache = regionCache;
                    }
                    int[] route = regionCache.GetOrBuild(pRequest.Generation,
                        start.Id, target.Id, TraversalClass(pRequest));
                    if (route != null && route.Length > 1)
                    {
                        regionCorridor = AWRegionCorridor.Create(
                            pRequest.Generation.RegionTopology, route);
                        int waypoint = ResolveRegionLookahead(pRequest.Generation,
                            route, start, _config.RegionCorridorLookaheadTiles);
                        if (waypoint >= 0 && pRequest.Generation.TryGet(waypoint,
                                out AWTileTraversalSnapshot resolvedWaypoint))
                            objective = resolvedWaypoint;
                    }
                }
                SearchResult result = Search(pRequest, start, objective,
                    Math.Max(1, primaryLimit), float.PositiveInfinity,
                    regionCorridor, heuristicWeight, pCancellation, workspace);
#if !AW3_RULES_TESTS
                AWPathfindingBootstrap.PathDiagnostics.AddExpandedNodes(result.ExpandedNodes);
#endif
                if (!result.Success && result.HitNodeLimit && longRange)
                {
#if !AW3_RULES_TESTS
                    AWPathfindingBootstrap.PathDiagnostics.OnFallback();
#endif
                    float detour = Math.Max(_config.FallbackCorridorMinDetour,
                        direct * _config.FallbackCorridorDetourScale);
                    result = Search(pRequest, start, objective,
                        Math.Max(_config.MaxNodesLongFallback, _config.MaxNodesLong),
                        direct + detour, regionCorridor?.Expand(),
                        heuristicWeight, pCancellation, workspace);
#if !AW3_RULES_TESTS
                    AWPathfindingBootstrap.PathDiagnostics.AddExpandedNodes(result.ExpandedNodes);
#endif
                }

                if (result.Success && pRequest.PhysicalTransportAvailable &&
                    CanUseVanillaTransport(start, target, pRequest.Profile) &&
                    !AWNarrowWaterRecoveryRules
                        .ShouldTryBoundedCrossingBeforeTransport(
                            pRequest.Profile.IsMilitary,
                            pRequest.Options.BoundedMilitaryWater) &&
                    AWDockTransportRules.ShouldPreferTransport(
                        workspace.PathLength(result.NodeIndex),
                        pRequest.PhysicalTransportRouteTiles))
                    return TransportResult(target.Id);

                if (!result.Success)
                {
                    if (pRequest.PhysicalTransportAvailable &&
                        CanUseVanillaTransport(start, target,
                            pRequest.Profile) &&
                        !AWNarrowWaterRecoveryRules
                            .ShouldTryBoundedCrossingBeforeTransport(
                                pRequest.Profile.IsMilitary,
                                pRequest.Options.BoundedMilitaryWater))
                    {
                        return TransportResult(target.Id);
                    }
                    return AWPathGenerationResult.Failure(result.HitNodeLimit
                        ? AWPathFailureReason.SearchLimitExceeded
                        : AWPathFailureReason.Unreachable);
                }

                int stepCount = workspace.BuildPath(result.NodeIndex);
                int maximumSteps = Math.Max(1, pMaximumSteps);
                int outputCount = Math.Min(stepCount, maximumSteps);
                var fullRoute = new AWPathStep[stepCount];
                for (int i = 0; i < stepCount; i++)
                    fullRoute[i] = workspace.PathStep(i);
                var steps = new AWPathStep[outputCount];
                for (int i = 0; i < outputCount; i++)
                {
                    pCancellation.ThrowIfCancellationRequested();
                    steps[i] = fullRoute[i];
                }
                bool objectiveIsFinalTarget = objective.Id == target.Id;
                if (outputCount < stepCount)
                    pRequest.CacheRoute(fullRoute, outputCount,
                        result.ReachedTarget && objectiveIsFinalTarget);
                int endTileId = outputCount > 0
                    ? steps[outputCount - 1].TileId
                    : pRequest.StartTileId;
                bool reachedTarget = result.ReachedTarget &&
                    objectiveIsFinalTarget && outputCount == stepCount;
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
                   pStart.IslandId >= 0 && pTarget.IslandId >= 0 &&
                   pStart.IslandId != pTarget.IslandId;
        }

        private static bool TryBuildDirectSegment(AWPathRequest pRequest,
            AWTileTraversalSnapshot pStart, AWTileTraversalSnapshot pTarget,
            CancellationToken pCancellation, out AWPathStep[] pSteps)
        {
            pSteps = Array.Empty<AWPathStep>();
            int x = pStart.X, y = pStart.Y;
            int targetX = pTarget.X, targetY = pTarget.Y;
            int dx = Math.Abs(targetX - x), dy = Math.Abs(targetY - y);
            int sx = x < targetX ? 1 : -1, sy = y < targetY ? 1 : -1;
            int error = dx - dy, currentId = pStart.Id;
            var result = new List<AWPathStep>(Math.Max(dx, dy));
            while (x != targetX || y != targetY)
            {
                pCancellation.ThrowIfCancellationRequested();
                int previousX = x, previousY = y, doubled = error * 2;
                if (doubled > -dy) { error -= dy; x += sx; }
                if (doubled < dx) { error += dx; y += sy; }
                int nextId = x + y * pRequest.Generation.Width;
                if (!pRequest.Generation.TryGet(nextId,
                        out AWTileTraversalSnapshot next) ||
                    !AWTraversalRules.CanEnter(next, pRequest.Profile,
                        pRequest.Options)) return false;
                if (previousX != x && previousY != y)
                {
                    int sideX = x + previousY * pRequest.Generation.Width;
                    int sideY = previousX + y * pRequest.Generation.Width;
                    if (!pRequest.Generation.TryGet(sideX, out AWTileTraversalSnapshot sxTile) ||
                        !pRequest.Generation.TryGet(sideY, out AWTileTraversalSnapshot syTile) ||
                        !AWTraversalRules.CanEnter(sxTile, pRequest.Profile, pRequest.Options) ||
                        !AWTraversalRules.CanEnter(syTile, pRequest.Profile, pRequest.Options)) return false;
                }
                if (!pRequest.Generation.TryGet(currentId, out AWTileTraversalSnapshot current)) return false;
                AWTraversalEstimate estimate = AWTraversalRules.Estimate(current, next,
                    pRequest.Profile, pRequest.Options);
                if (float.IsInfinity(estimate.RiskCost)) return false;
                estimate = new AWTraversalEstimate(estimate.TimeSeconds,
                    estimate.StaminaCost, estimate.HealthCost, estimate.RiskCost,
                    estimate.Hazards | AWHazardFlags.Direct);
                result.Add(new AWPathStep(nextId,
                    next.Liquid || next.Ocean ? AWMovementMethod.Swim : AWMovementMethod.Walk,
                    estimate, -1L,
                    AWPathTileFlagsExtensions.FromSnapshot(next)));
                currentId = nextId;
            }
            pSteps = result.ToArray();
            return true;
        }

        private static AWPathGenerationResult TransportResult(int pTargetTileId)
        {
            var estimate = new AWTraversalEstimate(0f, 0f, 0f, 0f,
                AWHazardFlags.Transport);
            return AWPathGenerationResult.Success(pTargetTileId, true,
                new[] { new AWPathStep(pTargetTileId,
                    AWMovementMethod.Transport, estimate) });
        }

        private static int TraversalClass(AWPathRequest pRequest)
        {
            int value = pRequest.Profile.IsBoat ? 1 : 0;
            if (pRequest.Profile.IsWaterCreature) value |= 2;
            if (pRequest.Options.PathOnWater) value |= 4;
            if (pRequest.Options.BoundedMilitaryWater) value |= 8;
            return value;
        }

        private static int ResolveRegionLookahead(AWTraversalGeneration pGeneration,
            int[] pRoute, AWTileTraversalSnapshot pStart, int pLookaheadTiles)
        {
            if (pGeneration?.RegionTopology == null || pRoute == null)
                return -1;
            int selected = -1;
            for (int index = 1; index < pRoute.Length; index++)
            {
                if (!pGeneration.RegionTopology.TryGetRegion(pRoute[index],
                        out AWRegionNode region) || region.CenterTileId < 0)
                    continue;
                selected = region.CenterTileId;
                if (pGeneration.TryGet(selected,
                        out AWTileTraversalSnapshot candidate) &&
                    AWTraversalRules.Distance(pStart.X, pStart.Y,
                        candidate.X, candidate.Y) >= Math.Max(1,
                        pLookaheadTiles)) break;
            }
            return selected;
        }

        private SearchResult Search(AWPathRequest pRequest, AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, int pMaxNodes, float pCorridorLimit,
            AWRegionCorridor pRegionCorridor, float pHeuristicWeight,
            CancellationToken pCancellation, SearchWorkspace pWorkspace)
        {
            pWorkspace.Reset(pMaxNodes, _config.MaxLabelsPerTile);
            float startHeuristic = Heuristic(pStart, pTarget, pRequest.Profile) *
                                   pHeuristicWeight;
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
                if (current.TileId == pTarget.Id)
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
                    if (pRegionCorridor != null && neighbor.RegionId >= 0 &&
                        !pRegionCorridor.Contains(neighbor.RegionId) &&
                        neighbor.Id != pTarget.Id) continue;
                    if (!float.IsPositiveInfinity(pCorridorLimit) &&
                        !AWTraversalRules.IsInsideFallbackCorridor(neighbor.X, neighbor.Y,
                            pStart.X, pStart.Y, pTarget.X, pTarget.Y, pCorridorLimit -
                            AWTraversalRules.Distance(pStart.X, pStart.Y, pTarget.X, pTarget.Y)))
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
                    float h = Heuristic(neighbor, pTarget, pRequest.Profile) *
                              pHeuristicWeight;
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
                        regionTransitions,
                        AWPathTileFlagsExtensions.FromSnapshot(neighbor));
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

            internal static AWRegionCorridor Create(
                AWRegionTopologySnapshot pTopology, int[] pRoute)
            {
                if (pTopology == null || pRoute == null || pRoute.Length == 0)
                    return null;
                var ids = new HashSet<int>(pRoute);
                return new AWRegionCorridor(ids, pTopology, 0).Expand();
            }

            internal bool Contains(int pRegionId) => _regionIds.Contains(pRegionId);

            internal AWRegionCorridor Expand()
            {
                var expanded = new HashSet<int>(_regionIds);
                foreach (int regionId in _regionIds)
                {
                    if (!_topology.TryGetRegion(regionId,
                            out AWRegionNode region)) continue;
                    for (int index = 0; index < region.Neighbours.Length; index++)
                        expanded.Add(region.Neighbours[index]);
                }
                return new AWRegionCorridor(expanded, _topology,
                    _expansionDepth + 1);
            }
        }

        private readonly struct SearchNode
        {
            public SearchNode(int pTileId, int pParentIndex, AWMovementMethod pMethod,
                AWTraversalEstimate pEstimate, float pTime, float pStaminaCost,
                float pHealthCost, float pRisk, float pG, float pH,
                int pWaterRun, int pRegionTransitions,
                AWPathTileFlags pPlannedTileFlags = AWPathTileFlags.None)
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
                PlannedTileFlags = pPlannedTileFlags;
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
            public AWPathTileFlags PlannedTileFlags { get; }
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

        private sealed class SearchWorkspace
        {
            private const int InitialNodeCapacity = 256;
            private const int InitialPathCapacity = 64;
            private SearchNode[] _nodes = new SearchNode[InitialNodeCapacity];
            private AWPathStep[] _path = new AWPathStep[InitialPathCapacity];
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
                int minimumSlots = Math.Max(16,
                    NextPowerOfTwo(Math.Max(1, pMaxNodes) * 2));
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
                EnsurePathCapacity(count);
                int write = count;
                node = _nodes[pNodeIndex];
                while (node.ParentIndex >= 0)
                {
                    _path[--write] = new AWPathStep(node.TileId,
                        node.Method, node.Estimate, -1L,
                        node.PlannedTileFlags);
                    node = _nodes[node.ParentIndex];
                }
                return count;
            }

            public int PathLength(int pNodeIndex)
            {
                int count = 0;
                SearchNode node = _nodes[pNodeIndex];
                while (node.ParentIndex >= 0)
                {
                    count++;
                    node = _nodes[node.ParentIndex];
                }
                return count;
            }

            public AWPathStep PathStep(int pIndex) => _path[pIndex];

            private int AddNode(SearchNode pNode)
            {
                if (_nodeCount == _nodes.Length)
                    Array.Resize(ref _nodes, _nodes.Length * 2);
                int index = _nodeCount++;
                _nodes[index] = pNode;
                return index;
            }

            private void EnsurePathCapacity(int pCount)
            {
                if (pCount <= _path.Length) return;
                int capacity = _path.Length;
                while (capacity < pCount) capacity *= 2;
                Array.Resize(ref _path, capacity);
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
