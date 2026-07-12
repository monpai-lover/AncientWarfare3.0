// Derived from Cultiway-Reborn pathfinding (MIT, Copyright (c) 2025 Inmny).
using System;
using System.Collections.Generic;
using System.Threading;

namespace AncientWarfare3.core.pathfinding
{
    public sealed class AWStreamingPathGenerator
    {
        private const float Epsilon = 0.001f;
        private readonly AWPathfindingConfig _config;

        public AWStreamingPathGenerator(AWPathfindingConfig pConfig = null)
        {
            _config = pConfig ?? AWPathfindingConfig.Default;
        }

        public void Generate(AWPathRequest pRequest, CancellationToken pCancellation)
        {
            if (pRequest == null) return;
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
                if (!AWTraversalRules.CanEnter(target, pRequest.Profile, pRequest.Options))
                {
                    pRequest.Stream.Fail(AWPathFailureReason.Unreachable, null);
                    return;
                }

                float direct = AWTraversalRules.Distance(start.X, start.Y, target.X, target.Y);
                bool longRange = direct > _config.ShortRangeTiles;
                int primaryLimit = longRange ? _config.MaxNodesLong : _config.MaxNodesShort;
                SearchResult result = Search(pRequest, start, target, Math.Max(1, primaryLimit),
                    float.PositiveInfinity, pCancellation);
                if (!result.Success && result.HitNodeLimit && longRange)
                {
                    float detour = Math.Max(_config.FallbackCorridorMinDetour,
                        direct * _config.FallbackCorridorDetourScale);
                    result = Search(pRequest, start, target,
                        Math.Max(_config.MaxNodesLongFallback, _config.MaxNodesLong),
                        direct + detour, pCancellation);
                }

                if (!result.Success)
                {
                    pRequest.Stream.Fail(result.HitNodeLimit
                        ? AWPathFailureReason.SearchLimitExceeded
                        : AWPathFailureReason.Unreachable, null);
                    return;
                }

                foreach (AWPathStep step in result.Steps)
                {
                    pCancellation.ThrowIfCancellationRequested();
                    if (!pRequest.Stream.AddStep(step)) return;
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

        private SearchResult Search(AWPathRequest pRequest, AWTileTraversalSnapshot pStart,
            AWTileTraversalSnapshot pTarget, int pMaxNodes, float pCorridorLimit,
            CancellationToken pCancellation)
        {
            var nodes = new List<SearchNode>(Math.Min(pMaxNodes * 2, 4096));
            var labelsByTile = new Dictionary<int, List<int>>(Math.Min(pMaxNodes, 1024));
            var open = new AWBinaryHeap<OpenEntry>(128, OpenEntryComparer.Instance);
            float startHeuristic = Heuristic(pStart, pTarget, pRequest.Profile);
            nodes.Add(SearchNode.Start(pStart.Id, startHeuristic));
            labelsByTile[pStart.Id] = new List<int>(_config.MaxLabelsPerTile) { 0 };
            open.Enqueue(new OpenEntry(0, startHeuristic, startHeuristic));

            int expanded = 0;
            while (open.Count > 0 && expanded < pMaxNodes)
            {
                if ((expanded & 63) == 0) pCancellation.ThrowIfCancellationRequested();
                OpenEntry entry = open.Dequeue();
                SearchNode current = nodes[entry.NodeIndex];
                if (!IsActive(labelsByTile, current.TileId, entry.NodeIndex)) continue;
                expanded++;
                if (current.TileId == pTarget.Id) return BuildResult(nodes, entry.NodeIndex);
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
                    int nodeIndex = nodes.Count;
                    var node = new SearchNode(neighbor.Id, entry.NodeIndex, method, estimate,
                        time, stamina, health, risk, g, h);
                    nodes.Add(node);
                    if (!TryAddLabel(labelsByTile, nodes, nodeIndex)) continue;
                    open.Enqueue(new OpenEntry(nodeIndex, node.F, h));
                }
            }
            return SearchResult.Failure(open.Count > 0 && expanded >= pMaxNodes);
        }

        private bool TryAddLabel(Dictionary<int, List<int>> pLabelsByTile,
            List<SearchNode> pNodes, int pNodeIndex)
        {
            SearchNode node = pNodes[pNodeIndex];
            if (!pLabelsByTile.TryGetValue(node.TileId, out List<int> labels))
            {
                labels = new List<int>(Math.Max(1, _config.MaxLabelsPerTile));
                pLabelsByTile[node.TileId] = labels;
            }
            for (int i = 0; i < labels.Count; i++)
                if (Dominates(pNodes[labels[i]], node)) return false;
            for (int i = labels.Count - 1; i >= 0; i--)
                if (Dominates(node, pNodes[labels[i]])) labels.RemoveAt(i);

            labels.Add(pNodeIndex);
            int cap = Math.Max(1, _config.MaxLabelsPerTile);
            if (labels.Count <= cap) return true;
            int worst = 0;
            for (int i = 1; i < labels.Count; i++)
                if (pNodes[labels[i]].F > pNodes[labels[worst]].F) worst = i;
            bool rejected = labels[worst] == pNodeIndex;
            labels.RemoveAt(worst);
            return !rejected;
        }

        private static bool Dominates(SearchNode pLeft, SearchNode pRight)
        {
            return pLeft.G <= pRight.G + Epsilon &&
                   pLeft.StaminaCost <= pRight.StaminaCost + Epsilon &&
                   pLeft.HealthCost <= pRight.HealthCost + Epsilon &&
                   pLeft.Risk <= pRight.Risk + Epsilon;
        }

        private static bool IsActive(Dictionary<int, List<int>> pLabelsByTile, int pTileId,
            int pNodeIndex)
        {
            return pLabelsByTile.TryGetValue(pTileId, out List<int> labels) &&
                   labels.Contains(pNodeIndex);
        }

        private static float Heuristic(AWTileTraversalSnapshot pFrom,
            AWTileTraversalSnapshot pTarget, AWActorTraversalProfile pProfile)
        {
            return AWTraversalRules.Distance(pFrom.X, pFrom.Y, pTarget.X, pTarget.Y) /
                   Math.Max(0.01f, pProfile.MovementSpeed);
        }

        private static SearchResult BuildResult(List<SearchNode> pNodes, int pIndex)
        {
            var reverse = new List<AWPathStep>();
            SearchNode node = pNodes[pIndex];
            while (node.ParentIndex >= 0)
            {
                reverse.Add(new AWPathStep(node.TileId, node.Method, node.Estimate));
                node = pNodes[node.ParentIndex];
            }
            reverse.Reverse();
            return SearchResult.SuccessResult(reverse.ToArray());
        }

        private readonly struct SearchNode
        {
            public SearchNode(int pTileId, int pParentIndex, AWMovementMethod pMethod,
                AWTraversalEstimate pEstimate, float pTime, float pStaminaCost,
                float pHealthCost, float pRisk, float pG, float pH)
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
            public float F => G + H;

            public static SearchNode Start(int pTileId, float pH)
            {
                return new SearchNode(pTileId, -1, AWMovementMethod.Walk, default,
                    0f, 0f, 0f, 0f, 0f, pH);
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

        private readonly struct SearchResult
        {
            private SearchResult(bool pSuccess, bool pHitNodeLimit, AWPathStep[] pSteps)
            {
                Success = pSuccess;
                HitNodeLimit = pHitNodeLimit;
                Steps = pSteps ?? Array.Empty<AWPathStep>();
            }

            public bool Success { get; }
            public bool HitNodeLimit { get; }
            public AWPathStep[] Steps { get; }

            public static SearchResult SuccessResult(AWPathStep[] pSteps) =>
                new SearchResult(true, false, pSteps);

            public static SearchResult Failure(bool pHitNodeLimit) =>
                new SearchResult(false, pHitNodeLimit, Array.Empty<AWPathStep>());
        }
    }
}
