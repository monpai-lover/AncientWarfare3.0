using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    internal readonly struct HierarchicalVassalLabelTile :
        IEquatable<HierarchicalVassalLabelTile>
    {
        internal readonly int X;
        internal readonly int Y;

        internal HierarchicalVassalLabelTile(int pX, int pY)
        {
            X = pX;
            Y = pY;
        }

        public bool Equals(HierarchicalVassalLabelTile pOther)
        {
            return X == pOther.X && Y == pOther.Y;
        }

        public override bool Equals(object pObject)
        {
            return pObject is HierarchicalVassalLabelTile other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked { return X * 397 ^ Y; }
        }
    }

    internal readonly struct HierarchicalVassalZoneLabelMetrics
    {
        internal readonly double AnchorX;
        internal readonly double AnchorY;
        internal readonly float Angle;
        internal readonly int LandArea;
        internal readonly int SpanX;
        internal readonly int SpanY;

        internal HierarchicalVassalZoneLabelMetrics(double pAnchorX,
            double pAnchorY, float pAngle, int pLandArea, int pSpanX,
            int pSpanY)
        {
            AnchorX = pAnchorX;
            AnchorY = pAnchorY;
            Angle = pAngle;
            LandArea = pLandArea;
            SpanX = Math.Max(1, pSpanX);
            SpanY = Math.Max(1, pSpanY);
        }
    }

    internal sealed class HierarchicalVassalZoneLabelAccumulator
    {
        private readonly HashSet<int> _zoneIds = new HashSet<int>();
        private readonly HashSet<HierarchicalVassalLabelTile> _landTileSet =
            new HashSet<HierarchicalVassalLabelTile>();
        private readonly List<HierarchicalVassalLabelTile> _landTiles =
            new List<HierarchicalVassalLabelTile>();
        private bool _capitalKnown;
        private int _capitalX;
        private int _capitalY;
        private double _weight;
        private double _weightedX;
        private double _weightedY;
        private double _weightedXX;
        private double _weightedYY;
        private double _weightedXY;
        private int _landArea;
        private int _minimumX = int.MaxValue;
        private int _maximumX = int.MinValue;
        private int _minimumY = int.MaxValue;
        private int _maximumY = int.MinValue;

        internal bool HasLandTiles => _landTiles.Count > 0;

        internal void SetCapital(int pX, int pY)
        {
            _capitalKnown = true;
            _capitalX = pX;
            _capitalY = pY;
        }

        internal bool Add(int pZoneId, double pX, double pY,
            int pGroundTiles)
        {
            return Add(pZoneId, pX, pY, pGroundTiles, null);
        }

        internal bool Add(int pZoneId, double pX, double pY,
            int pGroundTiles,
            IReadOnlyList<HierarchicalVassalLabelTile> pLandTiles)
        {
            if (pZoneId < 0 || pGroundTiles <= 0 ||
                !_zoneIds.Add(pZoneId)) return false;

            double weight = pGroundTiles;
            _weight += weight;
            _weightedX += pX * weight;
            _weightedY += pY * weight;
            _weightedXX += pX * pX * weight;
            _weightedYY += pY * pY * weight;
            _weightedXY += pX * pY * weight;
            _landArea = checked(_landArea + pGroundTiles);

            int x = (int)Math.Round(pX,
                MidpointRounding.AwayFromZero);
            int y = (int)Math.Round(pY,
                MidpointRounding.AwayFromZero);
            _minimumX = Math.Min(_minimumX, x);
            _maximumX = Math.Max(_maximumX, x);
            _minimumY = Math.Min(_minimumY, y);
            _maximumY = Math.Max(_maximumY, y);
            if (pLandTiles != null)
            {
                for (int index = 0; index < pLandTiles.Count; index++)
                {
                    HierarchicalVassalLabelTile tile = pLandTiles[index];
                    if (_landTileSet.Add(tile)) _landTiles.Add(tile);
                }
            }
            return true;
        }

        internal bool TryBuild(
            out HierarchicalVassalZoneLabelMetrics pResult)
        {
            pResult = default(HierarchicalVassalZoneLabelMetrics);
            if (_weight <= 0d || _zoneIds.Count == 0) return false;
            if (_landTiles.Count > 0)
                return TryBuildConnected(out pResult);

            double centerX = _weightedX / _weight;
            double centerY = _weightedY / _weight;
            double xx = _weightedXX / _weight - centerX * centerX;
            double yy = _weightedYY / _weight - centerY * centerY;
            double xy = _weightedXY / _weight - centerX * centerY;
            double angle = 0.5d * Math.Atan2(2d * xy, xx - yy) *
                           180d / Math.PI;
            angle = Math.Max(-35d, Math.Min(35d, angle));

            pResult = new HierarchicalVassalZoneLabelMetrics(
                centerX, centerY, (float)angle, _landArea,
                _maximumX - _minimumX + 1,
                _maximumY - _minimumY + 1);
            return true;
        }

        private bool TryBuildConnected(
            out HierarchicalVassalZoneLabelMetrics pResult)
        {
            pResult = default(HierarchicalVassalZoneLabelMetrics);
            var remaining = new HashSet<HierarchicalVassalLabelTile>(
                _landTiles);
            var queue = new Queue<HierarchicalVassalLabelTile>();
            List<HierarchicalVassalLabelTile> best = null;
            while (remaining.Count > 0)
            {
                HierarchicalVassalLabelTile seed = Smallest(remaining);
                remaining.Remove(seed);
                queue.Enqueue(seed);
                var current = new List<HierarchicalVassalLabelTile>();
                while (queue.Count > 0)
                {
                    HierarchicalVassalLabelTile tile = queue.Dequeue();
                    current.Add(tile);
                    Enqueue(tile.X - 1, tile.Y, remaining, queue);
                    Enqueue(tile.X + 1, tile.Y, remaining, queue);
                    Enqueue(tile.X, tile.Y - 1, remaining, queue);
                    Enqueue(tile.X, tile.Y + 1, remaining, queue);
                }

                if (best == null || current.Count > best.Count ||
                    current.Count == best.Count &&
                    ShouldPrefer(current, best)) best = current;
            }

            if (best == null || best.Count == 0) return false;
            long sumX = 0L;
            long sumY = 0L;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            for (int index = 0; index < best.Count; index++)
            {
                HierarchicalVassalLabelTile tile = best[index];
                sumX += tile.X;
                sumY += tile.Y;
                minX = Math.Min(minX, tile.X);
                maxX = Math.Max(maxX, tile.X);
                minY = Math.Min(minY, tile.Y);
                maxY = Math.Max(maxY, tile.Y);
            }
            double centerX = (double)sumX / best.Count;
            double centerY = (double)sumY / best.Count;
            double xx = 0d;
            double yy = 0d;
            double xy = 0d;
            HierarchicalVassalLabelTile nearest = best[0];
            double nearestDistance = DistanceSquared(nearest, centerX,
                centerY);
            bool roundedVisible = false;
            int roundedX = (int)Math.Round(centerX,
                MidpointRounding.AwayFromZero);
            int roundedY = (int)Math.Round(centerY,
                MidpointRounding.AwayFromZero);
            for (int index = 0; index < best.Count; index++)
            {
                HierarchicalVassalLabelTile tile = best[index];
                if (tile.X == roundedX && tile.Y == roundedY)
                    roundedVisible = true;
                double distance = DistanceSquared(tile, centerX, centerY);
                if (distance < nearestDistance ||
                    Math.Abs(distance - nearestDistance) < 0.000001d &&
                    ComesBefore(tile, nearest))
                {
                    nearest = tile;
                    nearestDistance = distance;
                }
                double dx = tile.X - centerX;
                double dy = tile.Y - centerY;
                xx += dx * dx;
                yy += dy * dy;
                xy += dx * dy;
            }
            double visibleX = roundedVisible ? centerX : nearest.X;
            double visibleY = roundedVisible ? centerY : nearest.Y;
            double angle = 0.5d * Math.Atan2(2d * xy, xx - yy) *
                           180d / Math.PI;
            angle = Math.Max(-35d, Math.Min(35d, angle));
            pResult = new HierarchicalVassalZoneLabelMetrics(
                visibleX, visibleY, (float)angle, best.Count,
                maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        private bool ShouldPrefer(
            List<HierarchicalVassalLabelTile> pCurrent,
            List<HierarchicalVassalLabelTile> pBest)
        {
            bool currentCapital = _capitalKnown && Contains(pCurrent,
                _capitalX, _capitalY);
            bool bestCapital = _capitalKnown && Contains(pBest,
                _capitalX, _capitalY);
            if (currentCapital != bestCapital) return currentCapital;
            return ComesBefore(Smallest(pCurrent), Smallest(pBest));
        }

        private static bool Contains(
            List<HierarchicalVassalLabelTile> pTiles, int pX, int pY)
        {
            for (int index = 0; index < pTiles.Count; index++)
                if (pTiles[index].X == pX && pTiles[index].Y == pY)
                    return true;
            return false;
        }

        private static HierarchicalVassalLabelTile Smallest(
            IEnumerable<HierarchicalVassalLabelTile> pTiles)
        {
            HierarchicalVassalLabelTile result = default(
                HierarchicalVassalLabelTile);
            bool initialized = false;
            foreach (HierarchicalVassalLabelTile tile in pTiles)
            {
                if (!initialized || ComesBefore(tile, result))
                {
                    result = tile;
                    initialized = true;
                }
            }
            return result;
        }

        private static void Enqueue(int pX, int pY,
            HashSet<HierarchicalVassalLabelTile> pRemaining,
            Queue<HierarchicalVassalLabelTile> pQueue)
        {
            var tile = new HierarchicalVassalLabelTile(pX, pY);
            if (pRemaining.Remove(tile)) pQueue.Enqueue(tile);
        }

        private static bool ComesBefore(HierarchicalVassalLabelTile pLeft,
            HierarchicalVassalLabelTile pRight)
        {
            return pLeft.X < pRight.X ||
                   pLeft.X == pRight.X && pLeft.Y < pRight.Y;
        }

        private static double DistanceSquared(
            HierarchicalVassalLabelTile pTile, double pX, double pY)
        {
            double dx = pTile.X - pX;
            double dy = pTile.Y - pY;
            return dx * dx + dy * dy;
        }

        internal void Reset()
        {
            _zoneIds.Clear();
            _landTileSet.Clear();
            _landTiles.Clear();
            _capitalKnown = false;
            _capitalX = 0;
            _capitalY = 0;
            _weight = 0d;
            _weightedX = 0d;
            _weightedY = 0d;
            _weightedXX = 0d;
            _weightedYY = 0d;
            _weightedXY = 0d;
            _landArea = 0;
            _minimumX = int.MaxValue;
            _maximumX = int.MinValue;
            _minimumY = int.MaxValue;
            _maximumY = int.MinValue;
        }
    }
}
