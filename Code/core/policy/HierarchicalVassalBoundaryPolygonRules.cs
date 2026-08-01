using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public readonly struct BoundaryTriangle
    {
        public BoundaryTriangle(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC)
        {
            A = pA;
            B = pB;
            C = pC;
        }

        public BoundaryFloatPoint A { get; }
        public BoundaryFloatPoint B { get; }
        public BoundaryFloatPoint C { get; }

        public BoundaryFloatPoint Centroid
        {
            get
            {
                return new BoundaryFloatPoint(
                    (A.X + B.X + C.X) / 3f,
                    (A.Y + B.Y + C.Y) / 3f);
            }
        }

        public float Area
        {
            get
            {
                return Math.Abs((B.X - A.X) * (C.Y - A.Y) -
                                (B.Y - A.Y) * (C.X - A.X)) * 0.5f;
            }
        }
    }

    public sealed class BoundaryPolygonDraft
    {
        public BoundaryPolygonDraft(
            long pOwnerId,
            BoundaryTier pTier,
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pOuterRings,
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pHoles,
            BoundaryFloatPoint[] pPositions,
            int[] pIndices,
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pSharedContours,
            bool pUsedRawFallback)
        {
            OwnerId = pOwnerId;
            Tier = pTier;
            OuterRings = pOuterRings ?? Array.Empty<IReadOnlyList<BoundaryFloatPoint>>();
            Holes = pHoles ?? Array.Empty<IReadOnlyList<BoundaryFloatPoint>>();
            Positions = pPositions ?? Array.Empty<BoundaryFloatPoint>();
            Indices = pIndices ?? Array.Empty<int>();
            SharedContours = pSharedContours ??
                             Array.Empty<IReadOnlyList<BoundaryFloatPoint>>();
            UsedRawFallback = pUsedRawFallback;
            Triangles = BuildTriangles(Positions, Indices);
            float area = 0f;
            for (int i = 0; i < Triangles.Count; i++)
                area += Triangles[i].Area;
            Area = area;
            IsValid = Indices.Length > 0 && Indices.Length % 3 == 0 &&
                      Triangles.Count * 3 == Indices.Length;
        }

        public long OwnerId { get; }
        public BoundaryTier Tier { get; }
        public IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> OuterRings { get; }
        public IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> Holes { get; }
        public BoundaryFloatPoint[] Positions { get; }
        public int[] Indices { get; }
        public IReadOnlyList<BoundaryTriangle> Triangles { get; }
        public IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> SharedContours { get; }
        public bool UsedRawFallback { get; }
        public bool IsValid { get; }
        public float Area { get; }

        private static IReadOnlyList<BoundaryTriangle> BuildTriangles(
            IReadOnlyList<BoundaryFloatPoint> pPositions,
            IReadOnlyList<int> pIndices)
        {
            var result = new List<BoundaryTriangle>(pIndices.Count / 3);
            for (int i = 0; i + 2 < pIndices.Count; i += 3)
            {
                int a = pIndices[i];
                int b = pIndices[i + 1];
                int c = pIndices[i + 2];
                if (a < 0 || b < 0 || c < 0 ||
                    a >= pPositions.Count || b >= pPositions.Count ||
                    c >= pPositions.Count)
                {
                    return Array.Empty<BoundaryTriangle>();
                }
                result.Add(new BoundaryTriangle(
                    pPositions[a], pPositions[b], pPositions[c]));
            }
            return result;
        }
    }

    public sealed class BoundaryVisualPairDraft
    {
        public BoundaryVisualPairDraft(
            BoundaryPolygonDraft pLeft,
            BoundaryPolygonDraft pRight,
            IReadOnlyList<BoundaryFloatPoint> pSharedContour,
            float pMaximumDeviation,
            bool pHasOverlapOrGap)
        {
            Left = pLeft;
            Right = pRight;
            SharedContour = pSharedContour;
            MaximumDeviation = pMaximumDeviation;
            HasOverlapOrGap = pHasOverlapOrGap;
        }

        public BoundaryPolygonDraft Left { get; }
        public BoundaryPolygonDraft Right { get; }
        public IReadOnlyList<BoundaryFloatPoint> SharedContour { get; }
        public float MaximumDeviation { get; }
        public bool HasOverlapOrGap { get; }
    }

    public static class HierarchicalVassalBoundaryPolygonRules
    {
        private const float MaximumVisualDeviation = 0.45f;
        private const float Epsilon = 0.0001f;

        public static BoundaryPolygonDraft BuildOwnerPolygon(
            BoundaryCellRaster pRaster,
            long ownerId)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            return BuildOwnerPolygon(
                pRaster, ownerId, BoundaryTier.SuzerainSystem,
                FullBounds(pRaster));
        }

        public static BoundaryPolygonDraft BuildOwnerPolygon(
            BoundaryCellRaster pRaster,
            long pOwnerId,
            BoundaryTier pTier,
            BoundaryChunkBounds pBounds)
        {
            return BuildOwnerPolygon(
                pRaster, pOwnerId, pTier, pBounds, null);
        }

        public static BoundaryPolygonDraft BuildOwnerPolygon(
            BoundaryCellRaster pRaster,
            long pOwnerId,
            BoundaryTier pTier,
            BoundaryChunkBounds pBounds,
            IReadOnlyList<BoundaryFloatPoint> pAcceptedOuter)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            bool[,] owned = BuildOwnedMask(pRaster, pOwnerId, pTier, pBounds);
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> rings =
                TraceRings(owned, pBounds.InteriorMinX, pBounds.InteriorMinY);
            var outer = new List<IReadOnlyList<BoundaryFloatPoint>>();
            var holes = new List<IReadOnlyList<BoundaryFloatPoint>>();
            for (int i = 0; i < rings.Count; i++)
            {
                IReadOnlyList<BoundaryFloatPoint> simplified =
                    SimplifyRing(rings[i]);
                if (SignedAreaClosed(simplified) >= 0f)
                    outer.Add(simplified);
                else
                    holes.Add(simplified);
            }

            bool fallback = pAcceptedOuter != null &&
                (outer.Count != 1 ||
                 SymmetricPolylineDistance(pAcceptedOuter, outer[0]) >
                    MaximumVisualDeviation + Epsilon ||
                 !AcceptedContourMatchesOwner(
                     pAcceptedOuter, owned,
                     pBounds.InteriorMinX, pBounds.InteriorMinY,
                     TotalHoleArea(holes)));
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> acceptedOuter = outer;
            if (!fallback && pAcceptedOuter != null)
            {
                IReadOnlyList<BoundaryFloatPoint> candidate =
                    SimplifyRing(pAcceptedOuter);
                acceptedOuter = new[] { EnsureWinding(candidate, positive: true) };
            }
            if (!TriangulateRings(
                    acceptedOuter, holes,
                    out BoundaryFloatPoint[] positions, out int[] indices) ||
                pAcceptedOuter != null && !fallback &&
                !TrianglesStayOnOwner(
                    positions, indices, pRaster, pOwnerId, pTier))
            {
                fallback = pAcceptedOuter != null;
                acceptedOuter = outer;
                if (!TriangulateRings(
                        outer, holes, out positions, out indices))
                {
                    positions = Array.Empty<BoundaryFloatPoint>();
                    indices = Array.Empty<int>();
                }
            }
            return new BoundaryPolygonDraft(
                pOwnerId, pTier, acceptedOuter, holes, positions, indices,
                Array.Empty<IReadOnlyList<BoundaryFloatPoint>>(), fallback);
        }

        public static BoundaryVisualPairDraft BuildVisualPair(
            BoundaryCellRaster pRaster,
            long pLeftOwnerId,
            long pRightOwnerId,
            BoundaryTier pTier,
            IReadOnlyList<BoundaryFloatPoint> pRawContour,
            IReadOnlyList<BoundaryFloatPoint> pAcceptedContour,
            BoundaryChunkBounds pBounds)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            if (pRawContour == null || pAcceptedContour == null ||
                pRawContour.Count < 2 || pAcceptedContour.Count < 2)
            {
                throw new ArgumentException("A shared contour needs two points.");
            }

            float maximumDeviation = SymmetricPolylineDistance(
                pAcceptedContour, pRawContour);
            bool useAccepted =
                maximumDeviation <= MaximumVisualDeviation + Epsilon &&
                !PolylineSelfIntersects(pAcceptedContour) &&
                SharedContourIsSafe(
                    pAcceptedContour, pRaster, pTier,
                    pLeftOwnerId, pRightOwnerId);
            IReadOnlyList<BoundaryFloatPoint> accepted = useAccepted
                ? Copy(pAcceptedContour)
                : Copy(pRawContour);
            if (!useAccepted)
                maximumDeviation = 0f;

            if (!TrySplitPair(
                    pRaster, pLeftOwnerId, pRightOwnerId, pTier,
                    pBounds, accepted,
                    out BoundaryPolygonDraft left,
                    out BoundaryPolygonDraft right))
            {
                left = BuildOwnerPolygon(pRaster, pLeftOwnerId, pTier, pBounds);
                right = BuildOwnerPolygon(pRaster, pRightOwnerId, pTier, pBounds);
                IReadOnlyList<BoundaryFloatPoint> raw = Copy(pRawContour);
                left = WithSharedContour(left, raw, pUsedRawFallback: true);
                right = WithSharedContour(right, raw, pUsedRawFallback: true);
                float pairArea = CountMask(BuildPairMask(
                    pRaster, pLeftOwnerId, pRightOwnerId, pTier, pBounds));
                return new BoundaryVisualPairDraft(
                    left, right, raw, 0f,
                    pHasOverlapOrGap: PairGeometryHasOverlapOrGap(
                        left, right, pairArea));
            }
            if (!useAccepted)
            {
                left = WithSharedContour(
                    left, accepted, pUsedRawFallback: true);
                right = WithSharedContour(
                    right, accepted, pUsedRawFallback: true);
            }
            float acceptedPairArea = CountMask(BuildPairMask(
                pRaster, pLeftOwnerId, pRightOwnerId, pTier, pBounds));
            return new BoundaryVisualPairDraft(
                left, right, accepted, maximumDeviation,
                pHasOverlapOrGap: PairGeometryHasOverlapOrGap(
                    left, right, acceptedPairArea));
        }

        private static bool TrySplitPair(
            BoundaryCellRaster pRaster,
            long pLeftOwnerId,
            long pRightOwnerId,
            BoundaryTier pTier,
            BoundaryChunkBounds pBounds,
            IReadOnlyList<BoundaryFloatPoint> pContour,
            out BoundaryPolygonDraft pLeft,
            out BoundaryPolygonDraft pRight)
        {
            pLeft = null;
            pRight = null;
            bool[,] pairMask = BuildPairMask(
                pRaster, pLeftOwnerId, pRightOwnerId, pTier, pBounds);
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> rings =
                TraceRings(pairMask, pBounds.InteriorMinX, pBounds.InteriorMinY);
            var outer = new List<IReadOnlyList<BoundaryFloatPoint>>();
            var holes = new List<IReadOnlyList<BoundaryFloatPoint>>();
            for (int i = 0; i < rings.Count; i++)
            {
                IReadOnlyList<BoundaryFloatPoint> ring = SimplifyRing(rings[i]);
                if (SignedAreaClosed(ring) > 0f) outer.Add(ring);
                else holes.Add(ring);
            }
            if (outer.Count != 1 || holes.Count != 0)
                return false;

            var boundary = new List<BoundaryFloatPoint>(outer[0]);
            int startIndex = InsertOnRing(boundary, pContour[0]);
            int endIndex = InsertOnRing(
                boundary, pContour[pContour.Count - 1]);
            if (startIndex < 0 || endIndex < 0 || startIndex == endIndex)
                return false;
            startIndex = IndexOf(boundary, pContour[0]);
            endIndex = IndexOf(boundary, pContour[pContour.Count - 1]);

            List<BoundaryFloatPoint> first = Arc(boundary, startIndex, endIndex);
            for (int i = pContour.Count - 2; i >= 1; i--)
                first.Add(pContour[i]);
            List<BoundaryFloatPoint> second = Arc(boundary, endIndex, startIndex);
            for (int i = 1; i < pContour.Count - 1; i++)
                second.Add(pContour[i]);
            first = new List<BoundaryFloatPoint>(SimplifyRing(first));
            second = new List<BoundaryFloatPoint>(SimplifyRing(second));
            if (!TrySimpleDraft(
                    pLeftOwnerId, pTier, first, pContour,
                    out BoundaryPolygonDraft firstDraft) ||
                !TrySimpleDraft(
                    pRightOwnerId, pTier, second, pContour,
                    out BoundaryPolygonDraft secondDraft))
                return false;

            BoundaryFloatPoint a = pContour[0];
            BoundaryFloatPoint b = pContour[Math.Min(1, pContour.Count - 1)];
            float length = (float)Math.Sqrt(
                (b.X - a.X) * (b.X - a.X) +
                (b.Y - a.Y) * (b.Y - a.Y));
            if (length <= Epsilon)
                return false;
            var leftProbe = new BoundaryFloatPoint(
                (a.X + b.X) * 0.5f - (b.Y - a.Y) / length * 0.05f,
                (a.Y + b.Y) * 0.5f + (b.X - a.X) / length * 0.05f);
            if (PointInPolygon(leftProbe, first))
            {
                pLeft = firstDraft;
                pRight = secondDraft;
            }
            else
            {
                pLeft = WithOwner(secondDraft, pLeftOwnerId);
                pRight = WithOwner(firstDraft, pRightOwnerId);
            }
            return !PairGeometryHasOverlapOrGap(
                       pLeft, pRight, CountMask(pairMask)) &&
                   TrianglesStayOnPair(
                       pLeft.Positions, pLeft.Indices, pRaster,
                       pLeftOwnerId, pRightOwnerId, pTier) &&
                   TrianglesStayOnPair(
                       pRight.Positions, pRight.Indices, pRaster,
                       pLeftOwnerId, pRightOwnerId, pTier);
        }

        private static BoundaryPolygonDraft WithSharedContour(
            BoundaryPolygonDraft pDraft,
            IReadOnlyList<BoundaryFloatPoint> pSharedContour,
            bool pUsedRawFallback)
        {
            return new BoundaryPolygonDraft(
                pDraft.OwnerId, pDraft.Tier,
                pDraft.OuterRings, pDraft.Holes,
                pDraft.Positions, pDraft.Indices,
                new[] { pSharedContour }, pUsedRawFallback);
        }

        private static bool[,] BuildOwnedMask(
            BoundaryCellRaster pRaster,
            long pOwnerId,
            BoundaryTier pTier,
            BoundaryChunkBounds pBounds)
        {
            int width = Math.Max(0,
                pBounds.InteriorMaxXExclusive - pBounds.InteriorMinX);
            int height = Math.Max(0,
                pBounds.InteriorMaxYExclusive - pBounds.InteriorMinY);
            var result = new bool[width, height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(
                    pBounds.InteriorMinX + x, pBounds.InteriorMinY + y);
                result[x, y] = cell.IsLand && OwnerId(cell, pTier) == pOwnerId;
            }
            return result;
        }

        private static bool[,] BuildPairMask(
            BoundaryCellRaster pRaster,
            long pLeftOwnerId,
            long pRightOwnerId,
            BoundaryTier pTier,
            BoundaryChunkBounds pBounds)
        {
            int width = Math.Max(0,
                pBounds.InteriorMaxXExclusive - pBounds.InteriorMinX);
            int height = Math.Max(0,
                pBounds.InteriorMaxYExclusive - pBounds.InteriorMinY);
            var result = new bool[width, height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(
                    pBounds.InteriorMinX + x, pBounds.InteriorMinY + y);
                long owner = OwnerId(cell, pTier);
                result[x, y] = cell.IsLand &&
                    (owner == pLeftOwnerId || owner == pRightOwnerId);
            }
            return result;
        }

        private static bool TriangulateRings(
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pOuterRings,
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pHoles,
            out BoundaryFloatPoint[] pPositions,
            out int[] pIndices)
        {
            var positions = new List<BoundaryFloatPoint>();
            var indices = new List<int>();
            var positionIndex = new Dictionary<BoundaryFloatPoint, int>();
            for (int outerIndex = 0; outerIndex < pOuterRings.Count; outerIndex++)
            {
                IReadOnlyList<BoundaryFloatPoint> outer = EnsureWinding(
                    SimplifyRing(pOuterRings[outerIndex]), positive: true);
                var containedHoles = new List<IReadOnlyList<BoundaryFloatPoint>>();
                for (int holeIndex = 0; holeIndex < pHoles.Count; holeIndex++)
                {
                    IReadOnlyList<BoundaryFloatPoint> hole = SimplifyRing(
                        pHoles[holeIndex]);
                    if (hole.Count > 0 && PointInPolygon(hole[0], outer))
                        containedHoles.Add(EnsureWinding(hole, positive: false));
                }
                List<BoundaryFloatPoint> merged = new List<BoundaryFloatPoint>(outer);
                containedHoles.Sort((first, second) =>
                    RightmostX(second).CompareTo(RightmostX(first)));
                for (int holeIndex = 0; holeIndex < containedHoles.Count; holeIndex++)
                {
                    if (!BridgeHole(merged, containedHoles[holeIndex], out merged))
                    {
                        pPositions = Array.Empty<BoundaryFloatPoint>();
                        pIndices = Array.Empty<int>();
                        return false;
                    }
                }
                if (!EarClip(merged, out IReadOnlyList<int> localIndices))
                {
                    pPositions = Array.Empty<BoundaryFloatPoint>();
                    pIndices = Array.Empty<int>();
                    return false;
                }
                var localToGlobal = new int[merged.Count];
                for (int i = 0; i < merged.Count; i++)
                {
                    if (!positionIndex.TryGetValue(merged[i], out int global))
                    {
                        global = positions.Count;
                        positions.Add(merged[i]);
                        positionIndex.Add(merged[i], global);
                    }
                    localToGlobal[i] = global;
                }
                for (int i = 0; i < localIndices.Count; i++)
                    indices.Add(localToGlobal[localIndices[i]]);
            }
            pPositions = positions.ToArray();
            pIndices = indices.ToArray();
            return pOuterRings.Count == 0 || indices.Count > 0;
        }

        private static bool BridgeHole(
            IReadOnlyList<BoundaryFloatPoint> pOuter,
            IReadOnlyList<BoundaryFloatPoint> pHole,
            out List<BoundaryFloatPoint> pMerged)
        {
            pMerged = null;
            int holeIndex = 0;
            for (int i = 1; i < pHole.Count; i++)
            {
                if (pHole[i].X > pHole[holeIndex].X ||
                    pHole[i].X == pHole[holeIndex].X &&
                    pHole[i].Y < pHole[holeIndex].Y)
                    holeIndex = i;
            }
            BoundaryFloatPoint holePoint = pHole[holeIndex];
            var outer = new List<BoundaryFloatPoint>(pOuter);
            int bridgeIndex = InsertRayIntersection(outer, holePoint);
            if (bridgeIndex < 0)
                return false;
            pMerged = new List<BoundaryFloatPoint>(outer.Count + pHole.Count + 2);
            for (int i = 0; i <= bridgeIndex; i++) pMerged.Add(outer[i]);
            pMerged.Add(holePoint);
            for (int step = 1; step < pHole.Count; step++)
                pMerged.Add(pHole[(holeIndex + step) % pHole.Count]);
            pMerged.Add(holePoint);
            pMerged.Add(outer[bridgeIndex]);
            for (int i = bridgeIndex + 1; i < outer.Count; i++) pMerged.Add(outer[i]);
            return true;
        }

        private static int InsertRayIntersection(
            List<BoundaryFloatPoint> pRing,
            BoundaryFloatPoint pHolePoint)
        {
            float bestX = float.MaxValue;
            int bestEdge = -1;
            for (int i = 0; i < pRing.Count; i++)
            {
                BoundaryFloatPoint a = pRing[i];
                BoundaryFloatPoint b = pRing[(i + 1) % pRing.Count];
                if ((a.Y > pHolePoint.Y) == (b.Y > pHolePoint.Y))
                    continue;
                float ratio = (pHolePoint.Y - a.Y) / (b.Y - a.Y);
                float x = a.X + (b.X - a.X) * ratio;
                if (x > pHolePoint.X + Epsilon && x < bestX)
                {
                    bestX = x;
                    bestEdge = i;
                }
            }
            if (bestEdge < 0)
                return -1;
            var bridge = new BoundaryFloatPoint(bestX, pHolePoint.Y);
            int next = (bestEdge + 1) % pRing.Count;
            if (pRing[bestEdge].Equals(bridge)) return bestEdge;
            if (pRing[next].Equals(bridge)) return next;
            pRing.Insert(bestEdge + 1, bridge);
            return bestEdge + 1;
        }

        private static bool EarClip(
            IReadOnlyList<BoundaryFloatPoint> pRing,
            out IReadOnlyList<int> pIndices)
        {
            var vertices = new List<int>(pRing.Count);
            for (int i = 0; i < pRing.Count; i++) vertices.Add(i);
            var indices = new List<int>((pRing.Count - 2) * 3);
            int guard = pRing.Count * pRing.Count * 2;
            while (vertices.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int cursor = 0; cursor < vertices.Count; cursor++)
                {
                    int previous = vertices[(cursor + vertices.Count - 1) % vertices.Count];
                    int current = vertices[cursor];
                    int next = vertices[(cursor + 1) % vertices.Count];
                    if (Cross(pRing[previous], pRing[current], pRing[next]) <= Epsilon)
                        continue;
                    bool contains = false;
                    for (int candidate = 0; candidate < vertices.Count; candidate++)
                    {
                        int pointIndex = vertices[candidate];
                        if (pointIndex == previous || pointIndex == current ||
                            pointIndex == next ||
                            pRing[pointIndex].Equals(pRing[previous]) ||
                            pRing[pointIndex].Equals(pRing[current]) ||
                            pRing[pointIndex].Equals(pRing[next]))
                            continue;
                        if (PointInTriangleInclusive(
                                pRing[pointIndex], pRing[previous],
                                pRing[current], pRing[next]))
                        {
                            contains = true;
                            break;
                        }
                    }
                    if (contains)
                        continue;
                    indices.Add(previous); indices.Add(current); indices.Add(next);
                    vertices.RemoveAt(cursor);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    pIndices = Array.Empty<int>();
                    return false;
                }
            }
            if (vertices.Count == 3 &&
                Cross(pRing[vertices[0]], pRing[vertices[1]], pRing[vertices[2]]) > Epsilon)
            {
                indices.Add(vertices[0]); indices.Add(vertices[1]); indices.Add(vertices[2]);
            }
            pIndices = indices;
            return vertices.Count == 3 && indices.Count >= 3;
        }

        private static bool PointInTriangleInclusive(
            BoundaryFloatPoint pPoint,
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC)
        {
            float ab = Cross(pA, pB, pPoint);
            float bc = Cross(pB, pC, pPoint);
            float ca = Cross(pC, pA, pPoint);
            return ab >= -Epsilon && bc >= -Epsilon && ca >= -Epsilon;
        }

        private static bool TrySimpleDraft(
            long pOwnerId,
            BoundaryTier pTier,
            IReadOnlyList<BoundaryFloatPoint> pRing,
            IReadOnlyList<BoundaryFloatPoint> pSharedContour,
            out BoundaryPolygonDraft pDraft)
        {
            IReadOnlyList<BoundaryFloatPoint> ring = EnsureWinding(
                SimplifyRing(pRing), positive: true);
            if (!TriangulateRings(
                    new[] { ring },
                    Array.Empty<IReadOnlyList<BoundaryFloatPoint>>(),
                    out BoundaryFloatPoint[] positions, out int[] indices))
            {
                pDraft = null;
                return false;
            }
            pDraft = new BoundaryPolygonDraft(
                pOwnerId, pTier, new[] { ring },
                Array.Empty<IReadOnlyList<BoundaryFloatPoint>>(),
                positions, indices, new[] { Copy(pSharedContour) },
                pUsedRawFallback: false);
            return pDraft.IsValid;
        }

        private static BoundaryPolygonDraft WithOwner(
            BoundaryPolygonDraft pDraft,
            long pOwnerId)
        {
            return new BoundaryPolygonDraft(
                pOwnerId, pDraft.Tier, pDraft.OuterRings, pDraft.Holes,
                pDraft.Positions, pDraft.Indices, pDraft.SharedContours,
                pDraft.UsedRawFallback);
        }

        private static int InsertOnRing(
            List<BoundaryFloatPoint> pRing,
            BoundaryFloatPoint pPoint)
        {
            int existing = IndexOf(pRing, pPoint);
            if (existing >= 0)
                return existing;
            for (int i = 0; i < pRing.Count; i++)
            {
                int next = (i + 1) % pRing.Count;
                if (!PointOnSegment(pPoint, pRing[i], pRing[next]))
                    continue;
                pRing.Insert(i + 1, pPoint);
                return i + 1;
            }
            return -1;
        }

        private static int IndexOf(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            BoundaryFloatPoint pPoint)
        {
            for (int i = 0; i < pPoints.Count; i++)
                if (pPoints[i].Equals(pPoint)) return i;
            return -1;
        }

        private static List<BoundaryFloatPoint> Arc(
            IReadOnlyList<BoundaryFloatPoint> pRing,
            int pStart,
            int pEnd)
        {
            var result = new List<BoundaryFloatPoint> { pRing[pStart] };
            int current = pStart;
            while (current != pEnd && result.Count <= pRing.Count + 1)
            {
                current = (current + 1) % pRing.Count;
                result.Add(pRing[current]);
            }
            return result;
        }

        private static IReadOnlyList<BoundaryFloatPoint> SimplifyRing(
            IReadOnlyList<BoundaryFloatPoint> pRing)
        {
            var points = new List<BoundaryFloatPoint>();
            for (int i = 0; i < pRing.Count; i++)
            {
                if (i == pRing.Count - 1 && pRing[i].Equals(pRing[0]))
                    continue;
                if (points.Count == 0 || !points[points.Count - 1].Equals(pRing[i]))
                    points.Add(pRing[i]);
            }
            bool changed = true;
            while (changed && points.Count > 3)
            {
                changed = false;
                for (int i = 0; i < points.Count; i++)
                {
                    BoundaryFloatPoint previous =
                        points[(i + points.Count - 1) % points.Count];
                    BoundaryFloatPoint current = points[i];
                    BoundaryFloatPoint next = points[(i + 1) % points.Count];
                    if (Math.Abs(Cross(previous, current, next)) > Epsilon ||
                        !PointOnSegment(current, previous, next))
                        continue;
                    points.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
            return points;
        }

        private static IReadOnlyList<BoundaryFloatPoint> EnsureWinding(
            IReadOnlyList<BoundaryFloatPoint> pRing,
            bool positive)
        {
            bool isPositive = SignedAreaClosed(pRing) >= 0f;
            if (isPositive == positive)
                return new List<BoundaryFloatPoint>(pRing);
            var result = new List<BoundaryFloatPoint>(pRing.Count);
            for (int i = pRing.Count - 1; i >= 0; i--) result.Add(pRing[i]);
            return result;
        }

        private static float SignedAreaClosed(
            IReadOnlyList<BoundaryFloatPoint> pRing)
        {
            double area = 0d;
            for (int i = 0; i < pRing.Count; i++)
            {
                BoundaryFloatPoint next = pRing[(i + 1) % pRing.Count];
                area += (double)pRing[i].X * next.Y -
                        (double)next.X * pRing[i].Y;
            }
            return (float)(area * 0.5d);
        }

        private static float RightmostX(IReadOnlyList<BoundaryFloatPoint> pRing)
        {
            float result = float.MinValue;
            for (int i = 0; i < pRing.Count; i++)
                result = Math.Max(result, pRing[i].X);
            return result;
        }

        private static bool PointOnSegment(
            BoundaryFloatPoint pPoint,
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd)
        {
            return Math.Abs(Cross(pStart, pEnd, pPoint)) <= Epsilon &&
                   pPoint.X >= Math.Min(pStart.X, pEnd.X) - Epsilon &&
                   pPoint.X <= Math.Max(pStart.X, pEnd.X) + Epsilon &&
                   pPoint.Y >= Math.Min(pStart.Y, pEnd.Y) - Epsilon &&
                   pPoint.Y <= Math.Max(pStart.Y, pEnd.Y) + Epsilon;
        }

        private static float Cross(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC)
        {
            return (pB.X - pA.X) * (pC.Y - pA.Y) -
                   (pB.Y - pA.Y) * (pC.X - pA.X);
        }

        private static bool PolylineSelfIntersects(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            for (int first = 0; first + 1 < pPoints.Count; first++)
            for (int second = first + 2; second + 1 < pPoints.Count; second++)
            {
                if (SegmentsIntersect(
                        pPoints[first], pPoints[first + 1],
                        pPoints[second], pPoints[second + 1]))
                    return true;
            }
            return false;
        }

        private static bool SegmentsIntersect(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            BoundaryFloatPoint pD)
        {
            float abC = Cross(pA, pB, pC);
            float abD = Cross(pA, pB, pD);
            float cdA = Cross(pC, pD, pA);
            float cdB = Cross(pC, pD, pB);
            if ((abC > Epsilon && abD < -Epsilon ||
                 abC < -Epsilon && abD > Epsilon) &&
                (cdA > Epsilon && cdB < -Epsilon ||
                 cdA < -Epsilon && cdB > Epsilon))
                return true;
            return Math.Abs(abC) <= Epsilon && PointOnSegment(pC, pA, pB) ||
                   Math.Abs(abD) <= Epsilon && PointOnSegment(pD, pA, pB) ||
                   Math.Abs(cdA) <= Epsilon && PointOnSegment(pA, pC, pD) ||
                   Math.Abs(cdB) <= Epsilon && PointOnSegment(pB, pC, pD);
        }

        private static bool PointInPolygon(
            BoundaryFloatPoint pPoint,
            IReadOnlyList<BoundaryFloatPoint> pRing)
        {
            bool inside = false;
            for (int i = 0, j = pRing.Count - 1; i < pRing.Count; j = i++)
            {
                if (PointOnSegment(pPoint, pRing[j], pRing[i]))
                    return true;
                bool crosses = (pRing[i].Y > pPoint.Y) !=
                               (pRing[j].Y > pPoint.Y) &&
                    pPoint.X < (pRing[j].X - pRing[i].X) *
                    (pPoint.Y - pRing[i].Y) /
                    (pRing[j].Y - pRing[i].Y) + pRing[i].X;
                if (crosses) inside = !inside;
            }
            return inside;
        }

        private static float CountMask(bool[,] pMask)
        {
            float result = 0f;
            for (int y = 0; y < pMask.GetLength(1); y++)
            for (int x = 0; x < pMask.GetLength(0); x++)
                if (pMask[x, y]) result++;
            return result;
        }

        private static bool TrianglesStayOnOwner(
            IReadOnlyList<BoundaryFloatPoint> pPositions,
            IReadOnlyList<int> pIndices,
            BoundaryCellRaster pRaster,
            long pOwnerId,
            BoundaryTier pTier)
        {
            return TrianglesStayInAllowedCells(
                pPositions, pIndices, pRaster,
                cell => cell.IsLand && OwnerId(cell, pTier) == pOwnerId);
        }

        private static bool TrianglesStayOnPair(
            IReadOnlyList<BoundaryFloatPoint> pPositions,
            IReadOnlyList<int> pIndices,
            BoundaryCellRaster pRaster,
            long pLeftOwnerId,
            long pRightOwnerId,
            BoundaryTier pTier)
        {
            return TrianglesStayInAllowedCells(
                pPositions, pIndices, pRaster,
                cell => cell.IsLand &&
                    (OwnerId(cell, pTier) == pLeftOwnerId ||
                     OwnerId(cell, pTier) == pRightOwnerId));
        }

        private static bool TrianglesStayInAllowedCells(
            IReadOnlyList<BoundaryFloatPoint> pPositions,
            IReadOnlyList<int> pIndices,
            BoundaryCellRaster pRaster,
            Func<BoundaryCellFacts, bool> pAllowed)
        {
            for (int i = 0; i + 2 < pIndices.Count; i += 3)
            {
                BoundaryFloatPoint a = pPositions[pIndices[i]];
                BoundaryFloatPoint b = pPositions[pIndices[i + 1]];
                BoundaryFloatPoint c = pPositions[pIndices[i + 2]];
                int minimumX = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)));
                int minimumY = (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)));
                int maximumX = (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)));
                int maximumY = (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
                for (int y = minimumY; y < maximumY; y++)
                for (int x = minimumX; x < maximumX; x++)
                {
                    if (TriangleCellIntersectionArea(a, b, c, x, y) <= Epsilon)
                        continue;
                    if (!pAllowed(pRaster.GetOrInvalid(x, y)))
                        return false;
                }
            }
            return true;
        }

        private static float TriangleCellIntersectionArea(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            int pCellX,
            int pCellY)
        {
            IReadOnlyList<BoundaryFloatPoint> polygon =
                new[] { pA, pB, pC };
            polygon = ClipPolygon(polygon, 0, pCellX, keepGreater: true);
            polygon = ClipPolygon(polygon, 0, pCellX + 1, keepGreater: false);
            polygon = ClipPolygon(polygon, 1, pCellY, keepGreater: true);
            polygon = ClipPolygon(polygon, 1, pCellY + 1, keepGreater: false);
            return Math.Abs(SignedAreaClosed(polygon));
        }

        private static bool PairGeometryHasOverlapOrGap(
            BoundaryPolygonDraft pLeft,
            BoundaryPolygonDraft pRight,
            float pExpectedArea)
        {
            float intersection = 0f;
            for (int left = 0; left < pLeft.Triangles.Count; left++)
            for (int right = 0; right < pRight.Triangles.Count; right++)
            {
                intersection += TriangleIntersectionArea(
                    pLeft.Triangles[left], pRight.Triangles[right]);
            }
            float union = pLeft.Area + pRight.Area - intersection;
            return intersection > 0.001f ||
                   Math.Abs(union - pExpectedArea) > 0.001f;
        }

        private static float TriangleIntersectionArea(
            BoundaryTriangle pSubject,
            BoundaryTriangle pClip)
        {
            IReadOnlyList<BoundaryFloatPoint> polygon = new[]
            {
                pSubject.A, pSubject.B, pSubject.C
            };
            BoundaryFloatPoint[] clip = { pClip.A, pClip.B, pClip.C };
            float winding = Cross(clip[0], clip[1], clip[2]);
            for (int edge = 0; edge < clip.Length && polygon.Count > 0; edge++)
            {
                polygon = ClipConvexPolygon(
                    polygon, clip[edge], clip[(edge + 1) % clip.Length],
                    winding);
            }
            return Math.Abs(SignedAreaClosed(polygon));
        }

        private static IReadOnlyList<BoundaryFloatPoint> ClipConvexPolygon(
            IReadOnlyList<BoundaryFloatPoint> pPolygon,
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            float pWinding)
        {
            if (pPolygon.Count == 0)
                return pPolygon;
            var result = new List<BoundaryFloatPoint>();
            BoundaryFloatPoint previous = pPolygon[pPolygon.Count - 1];
            bool previousInside = InsideDirectedEdge(
                previous, pStart, pEnd, pWinding);
            for (int i = 0; i < pPolygon.Count; i++)
            {
                BoundaryFloatPoint current = pPolygon[i];
                bool currentInside = InsideDirectedEdge(
                    current, pStart, pEnd, pWinding);
                if (currentInside != previousInside)
                    result.Add(LineIntersection(
                        previous, current, pStart, pEnd));
                if (currentInside)
                    result.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            return result;
        }

        private static bool InsideDirectedEdge(
            BoundaryFloatPoint pPoint,
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            float pWinding)
        {
            float side = Cross(pStart, pEnd, pPoint);
            return pWinding >= 0f
                ? side >= -Epsilon
                : side <= Epsilon;
        }

        private static BoundaryFloatPoint LineIntersection(
            BoundaryFloatPoint pFirstStart,
            BoundaryFloatPoint pFirstEnd,
            BoundaryFloatPoint pSecondStart,
            BoundaryFloatPoint pSecondEnd)
        {
            float firstX = pFirstEnd.X - pFirstStart.X;
            float firstY = pFirstEnd.Y - pFirstStart.Y;
            float secondX = pSecondEnd.X - pSecondStart.X;
            float secondY = pSecondEnd.Y - pSecondStart.Y;
            float denominator = firstX * secondY - firstY * secondX;
            if (Math.Abs(denominator) <= Epsilon)
                return pFirstStart;
            float ratio = ((pSecondStart.X - pFirstStart.X) * secondY -
                           (pSecondStart.Y - pFirstStart.Y) * secondX) /
                          denominator;
            return new BoundaryFloatPoint(
                pFirstStart.X + firstX * ratio,
                pFirstStart.Y + firstY * ratio);
        }

        private static IReadOnlyList<BoundaryFloatPoint> ClipPolygon(
            IReadOnlyList<BoundaryFloatPoint> pPolygon,
            int pAxis,
            float pBoundary,
            bool keepGreater)
        {
            if (pPolygon.Count == 0)
                return pPolygon;
            var result = new List<BoundaryFloatPoint>();
            BoundaryFloatPoint previous = pPolygon[pPolygon.Count - 1];
            bool previousInside = IsInsideClip(
                previous, pAxis, pBoundary, keepGreater);
            for (int i = 0; i < pPolygon.Count; i++)
            {
                BoundaryFloatPoint current = pPolygon[i];
                bool currentInside = IsInsideClip(
                    current, pAxis, pBoundary, keepGreater);
                if (currentInside != previousInside)
                    result.Add(ClipIntersection(
                        previous, current, pAxis, pBoundary));
                if (currentInside)
                    result.Add(current);
                previous = current;
                previousInside = currentInside;
            }
            return result;
        }

        private static bool IsInsideClip(
            BoundaryFloatPoint pPoint,
            int pAxis,
            float pBoundary,
            bool pKeepGreater)
        {
            float value = pAxis == 0 ? pPoint.X : pPoint.Y;
            return pKeepGreater
                ? value >= pBoundary - Epsilon
                : value <= pBoundary + Epsilon;
        }

        private static BoundaryFloatPoint ClipIntersection(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            int pAxis,
            float pBoundary)
        {
            float start = pAxis == 0 ? pStart.X : pStart.Y;
            float end = pAxis == 0 ? pEnd.X : pEnd.Y;
            float ratio = Math.Abs(end - start) <= Epsilon
                ? 0f
                : (pBoundary - start) / (end - start);
            return new BoundaryFloatPoint(
                pStart.X + (pEnd.X - pStart.X) * ratio,
                pStart.Y + (pEnd.Y - pStart.Y) * ratio);
        }

        private static IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> TraceRings(
            bool[,] pOwned,
            int pOriginX,
            int pOriginY)
        {
            var edges = new Dictionary<UndirectedEdge, DirectedEdge>();
            int width = pOwned.GetLength(0);
            int height = pOwned.GetLength(1);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (!pOwned[x, y])
                    continue;
                var a = new BoundaryGridPoint(pOriginX + x, pOriginY + y);
                var b = new BoundaryGridPoint(pOriginX + x + 1, pOriginY + y);
                var c = new BoundaryGridPoint(pOriginX + x + 1, pOriginY + y + 1);
                var d = new BoundaryGridPoint(pOriginX + x, pOriginY + y + 1);
                ToggleEdge(edges, a, b);
                ToggleEdge(edges, b, c);
                ToggleEdge(edges, c, d);
                ToggleEdge(edges, d, a);
            }

            var remaining = new List<DirectedEdge>(edges.Values);
            remaining.Sort((first, second) =>
            {
                int start = first.Start.CompareTo(second.Start);
                return start != 0 ? start : first.End.CompareTo(second.End);
            });
            var rings = new List<IReadOnlyList<BoundaryFloatPoint>>();
            while (remaining.Count > 0)
            {
                DirectedEdge first = remaining[0];
                remaining.RemoveAt(0);
                var points = new List<BoundaryFloatPoint>
                {
                    ToFloat(first.Start), ToFloat(first.End)
                };
                BoundaryGridPoint current = first.End;
                int guard = edges.Count + 1;
                while (!current.Equals(first.Start) && guard-- > 0)
                {
                    int nextIndex = FindNextEdge(remaining, current);
                    if (nextIndex < 0)
                        break;
                    DirectedEdge next = remaining[nextIndex];
                    remaining.RemoveAt(nextIndex);
                    current = next.End;
                    points.Add(ToFloat(current));
                }
                if (points.Count >= 4 && current.Equals(first.Start))
                    rings.Add(points);
            }
            return rings;
        }

        private static int FindNextEdge(
            IReadOnlyList<DirectedEdge> pEdges,
            BoundaryGridPoint pStart)
        {
            for (int i = 0; i < pEdges.Count; i++)
            {
                if (pEdges[i].Start.Equals(pStart))
                    return i;
            }
            return -1;
        }

        private static void ToggleEdge(
            IDictionary<UndirectedEdge, DirectedEdge> pEdges,
            BoundaryGridPoint pStart,
            BoundaryGridPoint pEnd)
        {
            var key = new UndirectedEdge(pStart, pEnd);
            if (pEdges.ContainsKey(key))
                pEdges.Remove(key);
            else
                pEdges.Add(key, new DirectedEdge(pStart, pEnd));
        }

        private static bool AcceptedContourMatchesOwner(
            IReadOnlyList<BoundaryFloatPoint> pContour,
            bool[,] pOwned,
            int pOriginX,
            int pOriginY,
            float pHoleArea)
        {
            if (pContour.Count < 4 ||
                !pContour[0].Equals(pContour[pContour.Count - 1]))
                return false;
            float ownedArea = 0f;
            for (int y = 0; y < pOwned.GetLength(1); y++)
            for (int x = 0; x < pOwned.GetLength(0); x++)
                if (pOwned[x, y]) ownedArea++;
            float contourArea = Math.Abs(SignedArea(pContour)) - pHoleArea;
            if (Math.Abs(contourArea - ownedArea) > MaximumVisualDeviation)
                return false;
            BoundaryFloatPoint centroid = PolygonCentroid(pContour);
            int localX = (int)Math.Floor(centroid.X) - pOriginX;
            int localY = (int)Math.Floor(centroid.Y) - pOriginY;
            return localX >= 0 && localY >= 0 &&
                   localX < pOwned.GetLength(0) &&
                   localY < pOwned.GetLength(1) && pOwned[localX, localY];
        }

        private static float TotalHoleArea(
            IReadOnlyList<IReadOnlyList<BoundaryFloatPoint>> pHoles)
        {
            float result = 0f;
            for (int i = 0; i < pHoles.Count; i++)
                result += Math.Abs(SignedAreaClosed(pHoles[i]));
            return result;
        }

        private static bool SharedContourIsSafe(
            IReadOnlyList<BoundaryFloatPoint> pContour,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId)
        {
            for (int i = 1; i < pContour.Count; i++)
            {
                BoundaryFloatPoint start = pContour[i - 1];
                BoundaryFloatPoint end = pContour[i];
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                int samples = Math.Max(1, (int)Math.Ceiling(length / 0.2f));
                for (int sample = 0; sample <= samples; sample++)
                {
                    float ratio = (float)sample / samples;
                    var point = new BoundaryFloatPoint(
                        start.X + dx * ratio, start.Y + dy * ratio);
                    if (!PointTouchesOnlyPair(
                            point, pRaster, pTier,
                            pLeftOwnerId, pRightOwnerId))
                        return false;
                }
            }
            return true;
        }

        private static bool PointTouchesOnlyPair(
            BoundaryFloatPoint pPoint,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId)
        {
            int floorX = (int)Math.Floor(pPoint.X);
            int floorY = (int)Math.Floor(pPoint.Y);
            bool onX = Math.Abs(pPoint.X - Math.Round(pPoint.X)) <= Epsilon;
            bool onY = Math.Abs(pPoint.Y - Math.Round(pPoint.Y)) <= Epsilon;
            int minimumX = onX ? floorX - 1 : floorX;
            int minimumY = onY ? floorY - 1 : floorY;
            bool found = false;
            for (int x = minimumX; x <= floorX; x++)
            for (int y = minimumY; y <= floorY; y++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                if (!cell.IsValid)
                    continue;
                if (!cell.IsLand)
                    return false;
                long owner = OwnerId(cell, pTier);
                if (owner != pLeftOwnerId && owner != pRightOwnerId)
                    return false;
                found = true;
            }
            return found;
        }

        private static BoundaryFloatPoint PolygonCentroid(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            float x = 0f;
            float y = 0f;
            int count = Math.Max(1, pPoints.Count - 1);
            for (int i = 0; i < count; i++)
            {
                x += pPoints[i].X;
                y += pPoints[i].Y;
            }
            return new BoundaryFloatPoint(x / count, y / count);
        }

        private static float MaximumDistanceToPolyline(
            IReadOnlyList<BoundaryFloatPoint> pCandidate,
            IReadOnlyList<BoundaryFloatPoint> pReference)
        {
            float maximum = 0f;
            for (int i = 1; i < pCandidate.Count; i++)
            {
                BoundaryFloatPoint start = pCandidate[i - 1];
                BoundaryFloatPoint end = pCandidate[i];
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                int samples = Math.Max(1, (int)Math.Ceiling(length / 0.20f));
                for (int sample = 0; sample <= samples; sample++)
                {
                    float ratio = (float)sample / samples;
                    var point = new BoundaryFloatPoint(
                        start.X + dx * ratio, start.Y + dy * ratio);
                    maximum = Math.Max(maximum,
                        DistanceToPolyline(point, pReference));
                }
            }
            return maximum;
        }

        private static float SymmetricPolylineDistance(
            IReadOnlyList<BoundaryFloatPoint> pFirst,
            IReadOnlyList<BoundaryFloatPoint> pSecond)
        {
            return Math.Max(
                MaximumDistanceToPolyline(pFirst, pSecond),
                MaximumDistanceToPolyline(pSecond, pFirst));
        }

        private static float DistanceToPolyline(
            BoundaryFloatPoint pPoint,
            IReadOnlyList<BoundaryFloatPoint> pPolyline)
        {
            float minimum = float.MaxValue;
            int segmentCount = pPolyline.Count;
            bool closed = pPolyline.Count > 2;
            if (!closed) segmentCount--;
            for (int i = 0; i < segmentCount; i++)
            {
                BoundaryFloatPoint start = pPolyline[i];
                BoundaryFloatPoint end = pPolyline[(i + 1) % pPolyline.Count];
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float lengthSquared = dx * dx + dy * dy;
                float ratio = lengthSquared <= Epsilon ? 0f :
                    ((pPoint.X - start.X) * dx +
                     (pPoint.Y - start.Y) * dy) / lengthSquared;
                ratio = Math.Max(0f, Math.Min(1f, ratio));
                float differenceX = pPoint.X - (start.X + dx * ratio);
                float differenceY = pPoint.Y - (start.Y + dy * ratio);
                minimum = Math.Min(minimum,
                    (float)Math.Sqrt(differenceX * differenceX +
                                     differenceY * differenceY));
            }
            return minimum;
        }

        private static IReadOnlyList<BoundaryFloatPoint> Copy(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            var result = new BoundaryFloatPoint[pPoints.Count];
            for (int i = 0; i < pPoints.Count; i++)
                result[i] = pPoints[i];
            return result;
        }

        private static BoundaryFloatPoint ToFloat(BoundaryGridPoint pPoint)
        {
            return new BoundaryFloatPoint(pPoint.X, pPoint.Y);
        }

        private static float SignedArea(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            double area = 0d;
            for (int i = 0; i < pPoints.Count - 1; i++)
                area += (double)pPoints[i].X * pPoints[i + 1].Y -
                        (double)pPoints[i + 1].X * pPoints[i].Y;
            return (float)(area * 0.5d);
        }

        private static long OwnerId(BoundaryCellFacts pCell, BoundaryTier pTier)
        {
            switch (pTier)
            {
                case BoundaryTier.City:
                    return pCell.CityId;
                case BoundaryTier.VassalRealm:
                    return pCell.RealmId;
                case BoundaryTier.SuzerainSystem:
                    return pCell.SystemId;
                default:
                    return -1;
            }
        }

        private static BoundaryChunkBounds FullBounds(BoundaryCellRaster pRaster)
        {
            return new BoundaryChunkBounds(
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive,
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive);
        }

        private readonly struct DirectedEdge
        {
            public DirectedEdge(BoundaryGridPoint pStart, BoundaryGridPoint pEnd)
            {
                Start = pStart;
                End = pEnd;
            }

            public BoundaryGridPoint Start { get; }
            public BoundaryGridPoint End { get; }
        }

        private readonly struct UndirectedEdge : IEquatable<UndirectedEdge>
        {
            public UndirectedEdge(BoundaryGridPoint pFirst, BoundaryGridPoint pSecond)
            {
                if (pFirst.CompareTo(pSecond) <= 0)
                {
                    First = pFirst;
                    Second = pSecond;
                }
                else
                {
                    First = pSecond;
                    Second = pFirst;
                }
            }

            public BoundaryGridPoint First { get; }
            public BoundaryGridPoint Second { get; }

            public bool Equals(UndirectedEdge pOther)
            {
                return First.Equals(pOther.First) && Second.Equals(pOther.Second);
            }

            public override bool Equals(object pValue)
            {
                return pValue is UndirectedEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked((First.GetHashCode() * 397) ^ Second.GetHashCode());
            }
        }
    }
}
