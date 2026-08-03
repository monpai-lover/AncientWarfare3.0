using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal struct HierarchicalVassalMapModeGeometryMetrics
    {
        public int Area;
        public Vector2 Centroid;
        public float Angle;
        public int SpanX;
        public int SpanY;
    }

    internal struct HierarchicalVassalMapModeLabelPlacement
    {
        public Vector2 Centroid;
        public float Angle;
        public float Size;
    }

    internal static class HierarchicalVassalMapModeGeometry
    {
        private const double GlyphWidthFactor = 1.0d;
        private const double GlyphHeightFactor = 1.16d;
        private const double EnvelopeSampleSpacing = 0.45d;
        private const double MinimumEnvelopeLandCoverage = 0.76d;
        private const int ReadableCountryLabelArea = 96;
        private const float ReadableCountryLabelSize = 1.25f;
        private const int MaximumAnchorCandidates = 24;
        private const int FitSearchIterations = 9;

        // Snapshot construction already has to walk every land tile. Keep
        // the area, anchor and orientation calculation in one pass so a
        // large map switch does not allocate several duplicate tile sets.
        internal static HierarchicalVassalMapModeGeometryMetrics
            CalculateMetrics(IReadOnlyList<Vector2Int> pLandTiles)
        {
            return CalculateMetrics(pLandTiles, default(CancellationToken));
        }

        internal static HierarchicalVassalMapModeGeometryMetrics
            CalculateMetrics(IReadOnlyList<Vector2Int> pLandTiles,
                CancellationToken pCancellationToken)
        {
            List<Vector2Int> tiles = UniqueTiles(pLandTiles,
                pCancellationToken);
            if (tiles.Count == 0)
                return new HierarchicalVassalMapModeGeometryMetrics
                {
                    Area = 0,
                    Centroid = new Vector2(0f, 0f),
                    Angle = 0f,
                    SpanX = 0,
                    SpanY = 0
                };

            long sumX = 0L;
            long sumY = 0L;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            for (int index = 0; index < tiles.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int tile = tiles[index];
                sumX += tile.x;
                sumY += tile.y;
                if (tile.x < minX) minX = tile.x;
                if (tile.x > maxX) maxX = tile.x;
                if (tile.y < minY) minY = tile.y;
                if (tile.y > maxY) maxY = tile.y;
            }

            Vector2 centroid = new Vector2(
                (float)sumX / tiles.Count,
                (float)sumY / tiles.Count);
            Vector2Int rounded = new Vector2Int(
                RoundToTile(centroid.x), RoundToTile(centroid.y));
            bool roundedVisible = false;
            Vector2Int nearest = tiles[0];
            double nearestDistance = DistanceSquared(nearest, centroid);
            double xx = 0d;
            double yy = 0d;
            double xy = 0d;
            for (int index = 0; index < tiles.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int candidate = tiles[index];
                if (candidate.x == rounded.x && candidate.y == rounded.y)
                    roundedVisible = true;
                double distance = DistanceSquared(candidate, centroid);
                if (distance < nearestDistance ||
                    (Math.Abs(distance - nearestDistance) < 0.000001d &&
                     ComesBefore(candidate, nearest)))
                {
                    nearest = candidate;
                    nearestDistance = distance;
                }

                double dx = candidate.x - centroid.x;
                double dy = candidate.y - centroid.y;
                xx += dx * dx;
                yy += dy * dy;
                xy += dx * dy;
            }

            Vector2 visibleCentroid = roundedVisible
                ? centroid
                : new Vector2(nearest.x, nearest.y);
            float angle = 0f;
            if (tiles.Count >= 3)
            {
                double rawAngle = 0.5d * Math.Atan2(2d * xy, xx - yy) *
                                  180d / Math.PI;
                if (rawAngle > 90d) rawAngle -= 180d;
                if (rawAngle < -90d) rawAngle += 180d;
                angle = (float)Math.Max(-35d, Math.Min(35d, rawAngle));
            }

            return new HierarchicalVassalMapModeGeometryMetrics
            {
                Area = tiles.Count,
                Centroid = visibleCentroid,
                Angle = angle,
                SpanX = maxX - minX + 1,
                SpanY = maxY - minY + 1
            };
        }

        public static int CountArea(IReadOnlyList<Vector2Int> pLandTiles)
        {
            return UniqueTiles(pLandTiles).Count;
        }

        public static Vector2 CalculateArithmeticCentroid(
            IReadOnlyList<Vector2Int> pLandTiles)
        {
            List<Vector2Int> tiles = UniqueTiles(pLandTiles);
            if (tiles.Count == 0) return new Vector2(0f, 0f);
            long sumX = 0L;
            long sumY = 0L;
            for (int index = 0; index < tiles.Count; index++)
            {
                sumX += tiles[index].x;
                sumY += tiles[index].y;
            }
            return new Vector2((float)sumX / tiles.Count,
                (float)sumY / tiles.Count);
        }

        public static Vector2 ResolveVisibleCentroid(
            IReadOnlyList<Vector2Int> pLandTiles)
        {
            List<Vector2Int> tiles = UniqueTiles(pLandTiles);
            if (tiles.Count == 0) return new Vector2(0f, 0f);
            Vector2 centroid = CalculateArithmeticCentroid(tiles);
            var rounded = new Vector2Int(RoundToTile(centroid.x),
                RoundToTile(centroid.y));
            var visible = new HashSet<Vector2Int>(tiles);
            if (visible.Contains(rounded)) return centroid;

            Vector2Int best = tiles[0];
            double bestDistance = DistanceSquared(best, centroid);
            for (int index = 1; index < tiles.Count; index++)
            {
                Vector2Int candidate = tiles[index];
                double distance = DistanceSquared(candidate, centroid);
                if (distance < bestDistance ||
                    (Math.Abs(distance - bestDistance) < 0.000001d &&
                     ComesBefore(candidate, best)))
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return new Vector2(best.x, best.y);
        }

        public static float CalculateLabelSize(int pArea)
        {
            return CalculateLabelSize(pArea, string.Empty);
        }

        public static float CalculateLabelSize(int pArea, string pDisplayName)
        {
            double scaled = HierarchicalVassalMapModeRules.LabelSizeBase +
                            Math.Sqrt(Math.Max(0, pArea)) *
                            HierarchicalVassalMapModeRules.LabelSizeScale;
            int nameLength = string.IsNullOrWhiteSpace(pDisplayName)
                ? 0
                : pDisplayName.Trim().Length;
            if (nameLength > 4)
                scaled *= Math.Sqrt(4d / nameLength);
            return (float)Math.Max(
                HierarchicalVassalMapModeRules.MinimumLabelSize,
                Math.Min(HierarchicalVassalMapModeRules.MaximumLabelSize,
                    scaled));
        }

        internal static float CalculateRenderedCharacterSize(
            float pEnvelopeSize, string pDisplayName,
            float pMeasuredWidthAtProbe, float pMeasuredHeightAtProbe,
            float pProbeCharacterSize)
        {
            if (!IsFinitePositive(pEnvelopeSize)) return 0f;
            if (!IsFinitePositive(pMeasuredWidthAtProbe) ||
                !IsFinitePositive(pMeasuredHeightAtProbe) ||
                !IsFinitePositive(pProbeCharacterSize))
                return pEnvelopeSize;

            int nameLength = string.IsNullOrWhiteSpace(pDisplayName)
                ? 1
                : Math.Max(1, pDisplayName.Trim().Length);
            float targetWidth = nameLength * pEnvelopeSize;
            float targetHeight = (float)GlyphHeightFactor * pEnvelopeSize;
            float widthScale = targetWidth / pMeasuredWidthAtProbe;
            float heightScale = targetHeight / pMeasuredHeightAtProbe;
            float scale = Math.Min(widthScale, heightScale);
            return IsFinitePositive(scale)
                ? pProbeCharacterSize * scale
                : pEnvelopeSize;
        }

        internal static float CalculateLabelSize(
            HierarchicalVassalMapModeGeometryMetrics pMetrics,
            string pDisplayName)
        {
            return CalculateLabelSize(pMetrics, pDisplayName, 0);
        }

        internal static float CalculateLabelSize(
            HierarchicalVassalMapModeGeometryMetrics pMetrics,
            string pDisplayName, int pCountryLabelGap)
        {
            if (pMetrics.Area <= 0) return
                HierarchicalVassalMapModeRules.MinimumLabelSize;

            double spanArea = Math.Max(1d,
                (double)Math.Max(1, pMetrics.SpanX) *
                Math.Max(1, pMetrics.SpanY));
            double compactness = Math.Max(0.2d,
                Math.Min(1d, pMetrics.Area / spanArea));
            double shortSpan = Math.Max(1d,
                Math.Min(Math.Max(1, pMetrics.SpanX),
                    Math.Max(1, pMetrics.SpanY)));
            double areaDiameter = Math.Sqrt(Math.Max(1, pMetrics.Area));
            double spanFactor = Math.Max(0.25d,
                Math.Min(1d, shortSpan / areaDiameter));
            double shapeFactor = 0.72d + 0.28d *
                Math.Sqrt(compactness) * (0.75d + 0.25d * spanFactor);
            double scaled = HierarchicalVassalMapModeRules.LabelSizeBase +
                            areaDiameter *
                            HierarchicalVassalMapModeRules.LabelSizeScale *
                            shapeFactor;
            double labelWidth = CalculateLabelWidthInGlyphs(
                pDisplayName, pCountryLabelGap);
            if (labelWidth > 4d)
                scaled *= Math.Sqrt(4d / labelWidth);
            double longSpan = Math.Max(1d,
                Math.Max(pMetrics.SpanX, pMetrics.SpanY));
            double widthLimit = longSpan * 0.78d /
                Math.Max(1d, labelWidth * GlyphWidthFactor);
            double heightLimit = shortSpan * 0.5d /
                GlyphHeightFactor;
            double territoryLimit = Math.Max(
                HierarchicalVassalMapModeRules.
                    SmallTerritoryMinimumLabelSize,
                Math.Min(widthLimit, heightLimit));
            double dynamicFloor = Math.Min(
                HierarchicalVassalMapModeRules.MinimumLabelSize,
                territoryLimit);
            return (float)Math.Max(dynamicFloor,
                Math.Min(HierarchicalVassalMapModeRules.MaximumLabelSize,
                    Math.Min(scaled, territoryLimit)));
        }

        internal static HierarchicalVassalMapModeLabelPlacement
            CalculateLabelPlacement(IReadOnlyList<Vector2Int> pLandTiles,
                string pDisplayName)
        {
            return CalculateLabelPlacement(pLandTiles, pDisplayName, 0);
        }

        internal static HierarchicalVassalMapModeLabelPlacement
            CalculateLabelPlacement(IReadOnlyList<Vector2Int> pLandTiles,
                string pDisplayName, int pCountryLabelGap)
        {
            return CalculateLabelPlacement(pLandTiles, pDisplayName,
                pCountryLabelGap, default(CancellationToken));
        }

        internal static HierarchicalVassalMapModeLabelPlacement
            CalculateLabelPlacement(IReadOnlyList<Vector2Int> pLandTiles,
                string pDisplayName, int pCountryLabelGap,
                CancellationToken pCancellationToken)
        {
            List<Vector2Int> allTiles = UniqueTiles(pLandTiles,
                pCancellationToken);
            if (allTiles.Count == 0)
                return new HierarchicalVassalMapModeLabelPlacement
                {
                    Centroid = new Vector2(0f, 0f),
                    Angle = 0f,
                    Size = HierarchicalVassalMapModeRules.
                        SmallTerritoryMinimumLabelSize
                };

            List<Vector2Int> component = LargestConnectedComponent(allTiles,
                pCancellationToken);
            HierarchicalVassalMapModeGeometryMetrics metrics =
                CalculateMetrics(component, pCancellationToken);
            var mask = new HashSet<Vector2Int>(component);
            Vector2 bestAnchor = metrics.Centroid;
            float bestCapacity = 0f;
            Vector2Int roundedCentroid = new Vector2Int(
                RoundToTile(metrics.Centroid.x),
                RoundToTile(metrics.Centroid.y));
            if (mask.Contains(roundedCentroid))
            {
                bestCapacity = FindMaximumFittedSize(mask,
                    pDisplayName, pCountryLabelGap, bestAnchor,
                    metrics.Angle, metrics, pCancellationToken);
            }

            List<Vector2Int> anchorCandidates = BuildAnchorCandidates(
                component, metrics.Centroid, pCancellationToken);
            for (int index = 0; index < anchorCandidates.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int candidateTile = anchorCandidates[index];
                var candidateAnchor = new Vector2(candidateTile.x,
                    candidateTile.y);
                float candidateCapacity = FindMaximumFittedSize(mask,
                    pDisplayName, pCountryLabelGap, candidateAnchor,
                    metrics.Angle, metrics, pCancellationToken);
                if (!IsBetterAnchor(candidateCapacity, candidateTile,
                        bestCapacity, bestAnchor, metrics.Centroid))
                    continue;
                bestCapacity = candidateCapacity;
                bestAnchor = candidateAnchor;
            }

            float areaScaled = CalculateLabelSize(metrics, pDisplayName,
                pCountryLabelGap);
            double fillProgress = Math.Max(0d, Math.Min(1d,
                (component.Count - 24d) / 500d));
            float fillRatio = (float)(0.62d + 0.28d * fillProgress);
            float fittedTarget = bestCapacity * fillRatio;
            float readabilityFloor = metrics.Area >=
                                     ReadableCountryLabelArea
                ? Math.Min(ReadableCountryLabelSize, bestCapacity)
                : HierarchicalVassalMapModeRules.
                    SmallTerritoryMinimumLabelSize;
            float size = Math.Min(
                HierarchicalVassalMapModeRules.MaximumLabelSize,
                Math.Min(bestCapacity, Math.Max(readabilityFloor,
                    Math.Max(areaScaled, fittedTarget))));
            if (size <= 0f)
                size = HierarchicalVassalMapModeRules.
                    SmallTerritoryMinimumLabelSize;

            return new HierarchicalVassalMapModeLabelPlacement
            {
                Centroid = bestAnchor,
                Angle = metrics.Angle,
                Size = size
            };
        }

        internal static bool LabelEnvelopeFitsTerritory(
            IReadOnlyList<Vector2Int> pLandTiles, string pDisplayName,
            HierarchicalVassalMapModeLabelPlacement pPlacement)
        {
            return LabelEnvelopeFitsTerritory(pLandTiles, pDisplayName,
                pPlacement, 0);
        }

        internal static bool LabelEnvelopeFitsTerritory(
            IReadOnlyList<Vector2Int> pLandTiles, string pDisplayName,
            HierarchicalVassalMapModeLabelPlacement pPlacement,
            int pCountryLabelGap)
        {
            List<Vector2Int> tiles = UniqueTiles(pLandTiles);
            return tiles.Count > 0 && EnvelopeFits(
                new HashSet<Vector2Int>(tiles), pDisplayName,
                pCountryLabelGap, pPlacement.Centroid, pPlacement.Angle,
                pPlacement.Size);
        }

        internal static float CalculateCountryGlyphCenterOffset(float pSize,
            int pCountryLabelGap)
        {
            if (!IsFinitePositive(pSize) || pCountryLabelGap <= 0) return 0f;
            int gap = Math.Max(1, Math.Min(4, pCountryLabelGap));
            double centerDistanceInGlyphs = 1d + gap * 0.25d;
            return (float)(pSize * GlyphWidthFactor *
                           centerDistanceInGlyphs * 0.5d);
        }

        public static float CalculateLabelAngle(
            IReadOnlyList<Vector2Int> pLandTiles)
        {
            List<Vector2Int> tiles = UniqueTiles(pLandTiles);
            if (tiles.Count < 3) return 0f;

            Vector2 centroid = CalculateArithmeticCentroid(tiles);
            double xx = 0d;
            double yy = 0d;
            double xy = 0d;
            for (int index = 0; index < tiles.Count; index++)
            {
                double dx = tiles[index].x - centroid.x;
                double dy = tiles[index].y - centroid.y;
                xx += dx * dx;
                yy += dy * dy;
                xy += dx * dy;
            }

            double angle = 0.5d * Math.Atan2(2d * xy, xx - yy) *
                           180d / Math.PI;
            if (angle > 90d) angle -= 180d;
            if (angle < -90d) angle += 180d;
            return (float)Math.Max(-35d, Math.Min(35d, angle));
        }

        public static float CalculateCityLabelSize(int pArea)
        {
            // City labels use a larger independent scale than country labels;
            // the old 0.065 factor made names disappear on ordinary maps.
            double scaled = Math.Sqrt(Math.Max(1, pArea)) * 0.325d;
            return (float)Math.Max(
                HierarchicalVassalMapModeRules.CityLabelMinimumSize,
                Math.Min(
                    HierarchicalVassalMapModeRules.CityLabelMaximumSize,
                    scaled));
        }

        private static List<Vector2Int> UniqueTiles(
            IReadOnlyList<Vector2Int> pLandTiles,
            CancellationToken pCancellationToken = default(CancellationToken))
        {
            var result = new List<Vector2Int>();
            if (pLandTiles == null) return result;
            var seen = new HashSet<Vector2Int>();
            for (int index = 0; index < pLandTiles.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int tile = pLandTiles[index];
                if (seen.Add(tile)) result.Add(tile);
            }
            return result;
        }

        private static List<Vector2Int> LargestConnectedComponent(
            IReadOnlyList<Vector2Int> pTiles,
            CancellationToken pCancellationToken = default(CancellationToken))
        {
            var remaining = new HashSet<Vector2Int>(pTiles);
            var largest = new List<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            while (remaining.Count > 0)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int seed = default(Vector2Int);
                foreach (Vector2Int tile in remaining)
                {
                    seed = tile;
                    break;
                }
                remaining.Remove(seed);
                queue.Enqueue(seed);
                var current = new List<Vector2Int>();
                while (queue.Count > 0)
                {
                    pCancellationToken.ThrowIfCancellationRequested();
                    Vector2Int tile = queue.Dequeue();
                    current.Add(tile);
                    EnqueueNeighbour(tile.x - 1, tile.y, remaining, queue);
                    EnqueueNeighbour(tile.x + 1, tile.y, remaining, queue);
                    EnqueueNeighbour(tile.x, tile.y - 1, remaining, queue);
                    EnqueueNeighbour(tile.x, tile.y + 1, remaining, queue);
                }
                if (current.Count > largest.Count) largest = current;
            }
            return largest;
        }

        private static void EnqueueNeighbour(int pX, int pY,
            HashSet<Vector2Int> pRemaining, Queue<Vector2Int> pQueue)
        {
            var candidate = new Vector2Int(pX, pY);
            if (!pRemaining.Remove(candidate)) return;
            pQueue.Enqueue(candidate);
        }

        private static List<Vector2Int> BuildAnchorCandidates(
            IReadOnlyList<Vector2Int> pTiles, Vector2 pCentroid,
            CancellationToken pCancellationToken = default(CancellationToken))
        {
            var candidates = new List<Vector2Int>();
            if (pTiles == null || pTiles.Count == 0) return candidates;

            var nearest = new List<Vector2Int>();
            for (int index = 0; index < pTiles.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                Vector2Int tile = pTiles[index];
                int insert = nearest.Count;
                double distance = DistanceSquared(tile, pCentroid);
                while (insert > 0 && DistanceSquared(
                           nearest[insert - 1], pCentroid) > distance)
                    insert--;
                if (insert >= MaximumAnchorCandidates / 2) continue;
                nearest.Insert(insert, tile);
                if (nearest.Count > MaximumAnchorCandidates / 2)
                    nearest.RemoveAt(nearest.Count - 1);
            }
            for (int index = 0; index < nearest.Count; index++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                AddUniqueCandidate(candidates, nearest[index]);
            }

            int remainingSlots = MaximumAnchorCandidates - candidates.Count;
            int stride = Math.Max(1, pTiles.Count / Math.Max(1,
                remainingSlots));
            for (int index = 0; index < pTiles.Count &&
                                candidates.Count < MaximumAnchorCandidates;
                 index += stride)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                AddUniqueCandidate(candidates, pTiles[index]);
            }
            return candidates;
        }

        private static void AddUniqueCandidate(List<Vector2Int> pCandidates,
            Vector2Int pCandidate)
        {
            for (int index = 0; index < pCandidates.Count; index++)
                if (pCandidates[index].Equals(pCandidate)) return;
            pCandidates.Add(pCandidate);
        }

        private static float FindMaximumFittedSize(
            HashSet<Vector2Int> pMask, string pDisplayName,
            int pCountryLabelGap, Vector2 pAnchor, float pAngle,
            HierarchicalVassalMapModeGeometryMetrics pMetrics,
            CancellationToken pCancellationToken = default(CancellationToken))
        {
            float low = 0f;
            float high = Math.Min(
                HierarchicalVassalMapModeRules.MaximumLabelSize / 0.75f,
                Math.Max(pMetrics.SpanX, pMetrics.SpanY));
            for (int iteration = 0; iteration < FitSearchIterations;
                 iteration++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                float candidate = (low + high) * 0.5f;
                if (EnvelopeFits(pMask, pDisplayName, pCountryLabelGap,
                        pAnchor, pAngle, candidate, pCancellationToken))
                    low = candidate;
                else
                    high = candidate;
            }
            return low;
        }

        private static bool IsBetterAnchor(float pCandidateCapacity,
            Vector2Int pCandidate, float pBestCapacity, Vector2 pBestAnchor,
            Vector2 pTerritoryCentroid)
        {
            const float capacityEpsilon = 0.0001f;
            if (pCandidateCapacity > pBestCapacity + capacityEpsilon)
                return true;
            if (Math.Abs(pCandidateCapacity - pBestCapacity) >
                capacityEpsilon)
                return false;

            double candidateDistance = DistanceSquared(pCandidate,
                pTerritoryCentroid);
            var bestTile = new Vector2Int(RoundToTile(pBestAnchor.x),
                RoundToTile(pBestAnchor.y));
            double bestDistance = DistanceSquared(bestTile,
                pTerritoryCentroid);
            return candidateDistance < bestDistance - 0.000001d ||
                   Math.Abs(candidateDistance - bestDistance) < 0.000001d &&
                   ComesBefore(pCandidate, bestTile);
        }

        private static bool EnvelopeFits(HashSet<Vector2Int> pMask,
            string pDisplayName, int pCountryLabelGap, Vector2 pAnchor,
            float pAngle, float pSize,
            CancellationToken pCancellationToken = default(CancellationToken))
        {
            if (pMask == null || pMask.Count == 0 || pSize <= 0f)
                return false;
            var anchorTile = new Vector2Int(RoundToTile(pAnchor.x),
                RoundToTile(pAnchor.y));
            if (!pMask.Contains(anchorTile)) return false;
            double labelWidth = CalculateLabelWidthInGlyphs(
                pDisplayName, pCountryLabelGap);
            double outlineMargin = Math.Max(0.08d, pSize * 0.1d);
            double halfWidth = labelWidth * GlyphWidthFactor * pSize * 0.5d +
                               outlineMargin;
            double halfHeight = GlyphHeightFactor * pSize * 0.5d +
                                outlineMargin;
            double radians = pAngle * Math.PI / 180d;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            int xSteps = Math.Max(1, (int)Math.Ceiling(
                halfWidth * 2d / EnvelopeSampleSpacing));
            int ySteps = Math.Max(1, (int)Math.Ceiling(
                halfHeight * 2d / EnvelopeSampleSpacing));
            int coveredSamples = 0;
            int totalSamples = (xSteps + 1) * (ySteps + 1);
            for (int yIndex = 0; yIndex <= ySteps; yIndex++)
            {
                pCancellationToken.ThrowIfCancellationRequested();
                double localY = -halfHeight + halfHeight * 2d * yIndex /
                    ySteps;
                for (int xIndex = 0; xIndex <= xSteps; xIndex++)
                {
                    double localX = -halfWidth + halfWidth * 2d * xIndex /
                        xSteps;
                    double worldX = pAnchor.x + localX * cos - localY * sin;
                    double worldY = pAnchor.y + localX * sin + localY * cos;
                    var tile = new Vector2Int(RoundToTile((float)worldX),
                        RoundToTile((float)worldY));
                    if (pMask.Contains(tile)) coveredSamples++;
                }
            }
            double requiredCoverage = pMask.Count >=
                                      ReadableCountryLabelArea
                ? MinimumEnvelopeLandCoverage
                : 1d;
            return totalSamples > 0 &&
                   (double)coveredSamples / totalSamples >=
                   requiredCoverage;
        }

        private static double CalculateLabelWidthInGlyphs(
            string pDisplayName, int pCountryLabelGap)
        {
            string value = pDisplayName?.Trim() ?? string.Empty;
            int length = Math.Max(1, value.Length);
            if (length != 2 || pCountryLabelGap <= 0) return length;
            int gap = Math.Max(1, Math.Min(4, pCountryLabelGap));
            return 2d + gap * 0.25d;
        }

        private static int RoundToTile(float pValue)
        {
            return (int)Math.Round(pValue, MidpointRounding.AwayFromZero);
        }

        private static bool IsFinitePositive(float pValue)
        {
            return pValue > 0f && !float.IsNaN(pValue) &&
                   !float.IsInfinity(pValue);
        }

        private static double DistanceSquared(Vector2Int pTile,
            Vector2 pPoint)
        {
            double dx = pTile.x - pPoint.x;
            double dy = pTile.y - pPoint.y;
            return dx * dx + dy * dy;
        }

        private static bool ComesBefore(Vector2Int pLeft, Vector2Int pRight)
        {
            return pLeft.x < pRight.x ||
                   pLeft.x == pRight.x && pLeft.y < pRight.y;
        }
    }
}
