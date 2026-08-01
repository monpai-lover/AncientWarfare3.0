using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalBoundaryRiverRules
    {
        private const int MinimumRiverCells = 6;

        public static bool IsRiverCandidate(
            int liquidCells,
            int maximumLocalWidth,
            int boundingWidth,
            int boundingHeight,
            bool touchesOcean)
        {
            if (touchesOcean || liquidCells < MinimumRiverCells)
                return false;
            if (maximumLocalWidth <= 0 || maximumLocalWidth > 2)
                return false;
            if (boundingWidth <= 0 || boundingHeight <= 0)
                return false;
            int shorter = Math.Min(boundingWidth, boundingHeight);
            int longer = Math.Max(boundingWidth, boundingHeight);
            return longer >= shorter * 2;
        }

        public static bool ShouldEmitPoliticalRiver(
            long leftRealmId,
            long rightRealmId)
        {
            return leftRealmId >= 0 && rightRealmId >= 0 &&
                   leftRealmId != rightRealmId;
        }

        public static BoundaryRiverDraft Analyze(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));

            var visited = new HashSet<BoundaryGridPoint>();
            var chains = new List<BoundaryPoliticalRiverChain>();
            var suppressed = new HashSet<BoundaryGridEdgeKey>();
            for (int y = pRaster.OriginY; y < pRaster.MaxYExclusive; y++)
            {
                for (int x = pRaster.OriginX; x < pRaster.MaxXExclusive; x++)
                {
                    var point = new BoundaryGridPoint(x, y);
                    if (visited.Contains(point) ||
                        !IsInlandWater(pRaster.GetOrInvalid(x, y)))
                    {
                        continue;
                    }

                    RiverComponent component = FloodComponent(
                        pRaster, point, visited);
                    int maximumLocalWidth = MaximumLocalWidth(component);
                    if (!IsRiverCandidate(
                            component.Cells.Count,
                            maximumLocalWidth,
                            component.Width,
                            component.Height,
                            component.TouchesOcean))
                    {
                        continue;
                    }

                    TryBuildAxisAlignedChain(
                        pRaster, pLayer, component, chains, suppressed);
                }
            }
            return new BoundaryRiverDraft(chains, suppressed);
        }

        private static RiverComponent FloodComponent(
            BoundaryCellRaster pRaster,
            BoundaryGridPoint pStart,
            HashSet<BoundaryGridPoint> pVisited)
        {
            var queue = new Queue<BoundaryGridPoint>();
            var cells = new List<BoundaryGridPoint>();
            queue.Enqueue(pStart);
            pVisited.Add(pStart);
            int minX = pStart.X;
            int maxX = pStart.X;
            int minY = pStart.Y;
            int maxY = pStart.Y;
            bool touchesOcean = false;
            while (queue.Count > 0)
            {
                BoundaryGridPoint current = queue.Dequeue();
                cells.Add(current);
                minX = Math.Min(minX, current.X);
                maxX = Math.Max(maxX, current.X);
                minY = Math.Min(minY, current.Y);
                maxY = Math.Max(maxY, current.Y);
                VisitNeighbor(pRaster, current.X - 1, current.Y,
                    queue, pVisited, ref touchesOcean);
                VisitNeighbor(pRaster, current.X + 1, current.Y,
                    queue, pVisited, ref touchesOcean);
                VisitNeighbor(pRaster, current.X, current.Y - 1,
                    queue, pVisited, ref touchesOcean);
                VisitNeighbor(pRaster, current.X, current.Y + 1,
                    queue, pVisited, ref touchesOcean);
            }
            return new RiverComponent(
                cells, minX, minY, maxX, maxY, touchesOcean);
        }

        private static void VisitNeighbor(
            BoundaryCellRaster pRaster,
            int pX,
            int pY,
            Queue<BoundaryGridPoint> pQueue,
            HashSet<BoundaryGridPoint> pVisited,
            ref bool pTouchesOcean)
        {
            BoundaryCellFacts cell = pRaster.GetOrInvalid(pX, pY);
            if (cell.IsValid && cell.Water == BoundaryWaterKind.Ocean)
            {
                pTouchesOcean = true;
                return;
            }
            if (!IsInlandWater(cell))
                return;
            var point = new BoundaryGridPoint(pX, pY);
            if (pVisited.Add(point))
                pQueue.Enqueue(point);
        }

        private static int MaximumLocalWidth(RiverComponent pComponent)
        {
            var set = new HashSet<BoundaryGridPoint>(pComponent.Cells);
            int maximumHorizontalRun = 0;
            for (int y = pComponent.MinY; y <= pComponent.MaxY; y++)
            {
                int run = 0;
                for (int x = pComponent.MinX; x <= pComponent.MaxX; x++)
                {
                    if (set.Contains(new BoundaryGridPoint(x, y)))
                    {
                        run++;
                        maximumHorizontalRun = Math.Max(maximumHorizontalRun, run);
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }

            int maximumVerticalRun = 0;
            for (int x = pComponent.MinX; x <= pComponent.MaxX; x++)
            {
                int run = 0;
                for (int y = pComponent.MinY; y <= pComponent.MaxY; y++)
                {
                    if (set.Contains(new BoundaryGridPoint(x, y)))
                    {
                        run++;
                        maximumVerticalRun = Math.Max(maximumVerticalRun, run);
                    }
                    else
                    {
                        run = 0;
                    }
                }
            }
            return Math.Min(maximumHorizontalRun, maximumVerticalRun);
        }

        private static bool TryBuildAxisAlignedChain(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            RiverComponent pComponent,
            List<BoundaryPoliticalRiverChain> pChains,
            HashSet<BoundaryGridEdgeKey> pSuppressed)
        {
            bool vertical = pComponent.Height >= pComponent.Width * 2;
            bool horizontal = pComponent.Width >= pComponent.Height * 2;
            if (!vertical && !horizontal)
                return false;

            var componentSet = new HashSet<BoundaryGridPoint>(pComponent.Cells);
            var points = new List<BoundaryFloatPoint>();
            var pendingSuppressed = new List<BoundaryGridEdgeKey>();
            long stableLeft = -1;
            long stableRight = -1;
            BoundaryTier stableTier = BoundaryTier.None;

            int firstSlice = vertical ? pComponent.MinY : pComponent.MinX;
            int lastSlice = vertical ? pComponent.MaxY : pComponent.MaxX;
            for (int slice = firstSlice; slice <= lastSlice; slice++)
            {
                int minorMin;
                int minorMax;
                if (!TrySliceBounds(
                        componentSet, pComponent, vertical, slice,
                        out minorMin, out minorMax))
                {
                    return false;
                }

                BoundaryCellFacts left;
                BoundaryCellFacts right;
                if (vertical)
                {
                    left = pRaster.GetOrInvalid(minorMin - 1, slice);
                    right = pRaster.GetOrInvalid(minorMax + 1, slice);
                }
                else
                {
                    left = pRaster.GetOrInvalid(slice, minorMax + 1);
                    right = pRaster.GetOrInvalid(slice, minorMin - 1);
                }

                BoundaryTier tier =
                    HierarchicalVassalBoundaryTopologyRules.Classify(
                        left, right, pLayer);
                long leftOwner = OwnerId(left, tier);
                long rightOwner = OwnerId(right, tier);
                if (tier == BoundaryTier.None ||
                    !ShouldEmitPoliticalRiver(leftOwner, rightOwner))
                {
                    return false;
                }
                if (points.Count == 0)
                {
                    stableLeft = leftOwner;
                    stableRight = rightOwner;
                    stableTier = tier;
                }
                else if (stableLeft != leftOwner ||
                         stableRight != rightOwner || stableTier != tier)
                {
                    return false;
                }

                float center = (minorMin + minorMax + 1) * 0.5f;
                points.Add(vertical
                    ? new BoundaryFloatPoint(center, slice + 0.5f)
                    : new BoundaryFloatPoint(slice + 0.5f, center));
                AddShoreEdges(
                    vertical, slice, minorMin, minorMax, pendingSuppressed);
            }

            pChains.Add(new BoundaryPoliticalRiverChain(
                points, stableTier, stableLeft, stableRight));
            for (int i = 0; i < pendingSuppressed.Count; i++)
                pSuppressed.Add(pendingSuppressed[i]);
            return true;
        }

        private static bool TrySliceBounds(
            HashSet<BoundaryGridPoint> pCells,
            RiverComponent pComponent,
            bool pVertical,
            int pSlice,
            out int pMinorMin,
            out int pMinorMax)
        {
            pMinorMin = int.MaxValue;
            pMinorMax = int.MinValue;
            int first = pVertical ? pComponent.MinX : pComponent.MinY;
            int last = pVertical ? pComponent.MaxX : pComponent.MaxY;
            for (int minor = first; minor <= last; minor++)
            {
                var point = pVertical
                    ? new BoundaryGridPoint(minor, pSlice)
                    : new BoundaryGridPoint(pSlice, minor);
                if (!pCells.Contains(point))
                    continue;
                pMinorMin = Math.Min(pMinorMin, minor);
                pMinorMax = Math.Max(pMinorMax, minor);
            }
            if (pMinorMin == int.MaxValue)
                return false;
            for (int minor = pMinorMin; minor <= pMinorMax; minor++)
            {
                var point = pVertical
                    ? new BoundaryGridPoint(minor, pSlice)
                    : new BoundaryGridPoint(pSlice, minor);
                if (!pCells.Contains(point))
                    return false;
            }
            return pMinorMax - pMinorMin + 1 <= 2;
        }

        private static void AddShoreEdges(
            bool pVertical,
            int pSlice,
            int pMinorMin,
            int pMinorMax,
            List<BoundaryGridEdgeKey> pEdges)
        {
            if (pVertical)
            {
                pEdges.Add(new BoundaryGridEdgeKey(
                    new BoundaryGridPoint(pMinorMin, pSlice),
                    new BoundaryGridPoint(pMinorMin, pSlice + 1)));
                pEdges.Add(new BoundaryGridEdgeKey(
                    new BoundaryGridPoint(pMinorMax + 1, pSlice),
                    new BoundaryGridPoint(pMinorMax + 1, pSlice + 1)));
            }
            else
            {
                pEdges.Add(new BoundaryGridEdgeKey(
                    new BoundaryGridPoint(pSlice, pMinorMin),
                    new BoundaryGridPoint(pSlice + 1, pMinorMin)));
                pEdges.Add(new BoundaryGridEdgeKey(
                    new BoundaryGridPoint(pSlice, pMinorMax + 1),
                    new BoundaryGridPoint(pSlice + 1, pMinorMax + 1)));
            }
        }

        private static bool IsInlandWater(BoundaryCellFacts pCell)
        {
            return pCell.IsValid &&
                   pCell.Water == BoundaryWaterKind.InlandWater;
        }

        private static long OwnerId(BoundaryCellFacts pCell, BoundaryTier pTier)
        {
            if (!pCell.IsLand || pCell.SystemId < 0)
                return -1;
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

        private sealed class RiverComponent
        {
            public RiverComponent(
                List<BoundaryGridPoint> pCells,
                int pMinX,
                int pMinY,
                int pMaxX,
                int pMaxY,
                bool pTouchesOcean)
            {
                Cells = pCells;
                MinX = pMinX;
                MinY = pMinY;
                MaxX = pMaxX;
                MaxY = pMaxY;
                TouchesOcean = pTouchesOcean;
            }

            public List<BoundaryGridPoint> Cells { get; }
            public int MinX { get; }
            public int MinY { get; }
            public int MaxX { get; }
            public int MaxY { get; }
            public bool TouchesOcean { get; }
            public int Width { get { return MaxX - MinX + 1; } }
            public int Height { get { return MaxY - MinY + 1; } }
        }
    }
}
