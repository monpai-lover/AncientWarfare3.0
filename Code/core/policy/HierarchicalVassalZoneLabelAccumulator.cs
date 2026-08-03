using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
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

        internal bool Add(int pZoneId, double pX, double pY,
            int pGroundTiles)
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
            return true;
        }

        internal bool TryBuild(
            out HierarchicalVassalZoneLabelMetrics pResult)
        {
            pResult = default(HierarchicalVassalZoneLabelMetrics);
            if (_weight <= 0d || _zoneIds.Count == 0) return false;

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

        internal void Reset()
        {
            _zoneIds.Clear();
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
