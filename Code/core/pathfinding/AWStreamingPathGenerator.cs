// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWStreamingPathGenerator : IAWPathGenerator
    {
        private const float Epsilon = 0.001f;
        [ThreadStatic] private static SearchWorkspace _threadWorkspace;
        private readonly AWPathfindingConfig _config;

        public AWStreamingPathGenerator(AWPathfindingConfig pConfig = null)
        {
            _config = pConfig ?? AWPathfindingConfig.Default;
        }

        public void Generate(AWPathRequest pRequest, CancellationToken pCancellation)
        {
            if (pRequest == null) return;
            SearchWorkspace workspace = _threadWorkspace ??=
                new SearchWorkspace(OpenEntryComparer.Instance);
            try
            {
                pCancellation.ThrowIfCancellationRequested();
                if (!pRequest.Generation.TryGet(pRequest.StartTileId, out AWTileTraversalSnapshot start))
                {
                    pRequest.Stream.Fail(AWPathFailureReason.InvalidStart, null);
                    return;
                }
                if (!pRequest.Generation.TryGet(pRequest.TargetTileId, out AWTileTraversalSnapshot target))
                {
                    pRequest.Stream.Fail(AWPathFailureReason.InvalidTarget, null);
                    return;
                }
                if (pRequest.StartTileId == pRequest.TargetTileId)
                {
                    pRequest.Stream.Complete();
                    return;
                }
                if (pRequest.Options.BoundedMilitaryWater &&
                    (target.Liquid || target.Ocean))
                {
                    pRequest.Stream.Fail(AWPathFailureReason.Unreachable, null);
                    return;
                }
                if (!AWTraversalRules.CanEnter(target, pRequest.Profile, pRequest.Options))
                {
                    pRequest.Stream.Fail(AWPathFailureReason.Unreachable, null);
                    return;
                }

                float direct = AWTraversalRules.Distance(start.X, start.Y, target.X, target.Y);
                bool longRange = direct > _config.ShortRangeTiles;
                int primaryLimit = longRange ? _config.MaxNodesLong : _config.MaxNodesShort;
                SearchResult result = Search(pRequest, start, target,
                    Math.Max(1, primaryLimit), float.PositiveInfinity,
                    pCancellation, workspace);
                if (!result.Success && result.HitNodeLimit && longRange)
                {
                    float detour = Math.Max(_config.FallbackCorridorMinDetour,
                        direct * _config.FallbackCorridorDetourScale);
                    result = Search(pRequest, start, target,
                        Math.Max(_config.MaxNodesLongFallback, _config.MaxNodesLong),
                        direct + detour, pCancellation, workspace);
                }

                if (!result.Success)
                {
                    if (CanUseVanillaTransport(start, target,
                            pRequest.Profile) &&
                        !AWNarrowWaterRecoveryRules
                            .ShouldTryBoundedCrossingBeforeTransport(
                                pRequest.Profile.IsMilitary,
                                pRequest.Options.BoundedMilitaryWater))
                    {
                        var transportEstimate = new AWTraversalEstimate(0f, 0f, 0f, 0f,
                            AWHazardFlags.Transport);
                        pRequest.Stream.AddStep(new AWPathStep(target.Id,
                            AWMovementMethod.Transport, transportEstimate));
                        pRequest.Stream.Complete();
                        return;
                    }
                    pRequest.Stream.Fail(result.HitNodeLimit
                        ? AWPathFailureReason.SearchLimitExceeded
                        : AWPathFailureReason.Unreachable, null);
                    return;
                }

                int stepCount = workspace.BuildPath(result.NodeIndex);
                for (int i = 0; i < stepCount; i++)
                {
                    pCancellation.ThrowIfCancellationRequested();
                    if (!pRequest.Stream.AddStep(workspace.PathStep(i))) return;
                }
                pRequest.Stream.Complete();
            }
            catch (OperationCanceledException)
            {
                pRequest.Stream.Cancel(AWPathFailureReason.CancelledByNewRequest);
            }
            catch (Exception error)
            {
                pRequest.Stream.Fail(AWPathFailureReason.GeneratorException, error);
            }
        }

        private static bool CanUseVanillaTransport(AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, AWActorTraversalProfile pProfile)
        {
            return !pProfile.CanFly && !pProfile.IsBoat && !pProfile.IsWaterCreature &&
                   pStart.IslandId >= 0 && pTarget.IslandId >= 0 &&
                   pStart.IslandId != pTarget.IslandId;
        }

        private SearchResult Search(AWPathRequest pRequest, AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, int pMaxNodes, float pCorridorLimit,
            CancellationToken pCancellation, SearchWorkspace pWorkspace)
        {
            pWorkspace.Reset(pMaxNodes, _config.MaxLabelsPerTile);
            float startHeuristic = Heuristic(pStart, pTarget, pRequest.Profile);
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
                    return SearchResult.SuccessResult(entry.NodeIndex);
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
                    float h = Heuristic(neighbor, pTarget, pRequest.Profile);
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
                return SearchResult.SuccessResult(segmentIndex);
            return SearchResult.Failure(pWorkspace.Open.Count > 0 &&
                                        expanded >= pMaxNodes);
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
                        node.Method, node.Estimate);
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
                int pNodeIndex)
            {
                Success = pSuccess;
                HitNodeLimit = pHitNodeLimit;
                NodeIndex = pNodeIndex;
            }

            public bool Success { get; }
            public bool HitNodeLimit { get; }
            public int NodeIndex { get; }

            public static SearchResult SuccessResult(int pNodeIndex) =>
                new SearchResult(true, false, pNodeIndex);

            public static SearchResult Failure(bool pHitNodeLimit) =>
                new SearchResult(false, pHitNodeLimit, -1);
        }
    }
}
