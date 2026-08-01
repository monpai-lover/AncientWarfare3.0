using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalBoundaryCurveRules
    {
        public const float MaximumSampleSpacing = 0.35f;

        private const float SampleStep = 0.2f;

        private const float GridEpsilon = 0.0001f;

        public static IReadOnlyList<BoundaryGridPoint> Simplify(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            ISet<BoundaryGridPoint> pProtectedPoints,
            float tolerance)
        {
            if (pPoints == null)
                throw new ArgumentNullException(nameof(pPoints));
            if (pPoints.Count <= 2)
                return CopyDistinct(pPoints);

            List<BoundaryGridPoint> points = CopyDistinct(pPoints);
            if (points.Count <= 2)
                return points;
            var anchors = new List<int> { 0 };
            if (pProtectedPoints != null)
            {
                for (int i = 1; i < points.Count - 1; i++)
                {
                    if (pProtectedPoints.Contains(points[i]))
                        anchors.Add(i);
                }
            }
            anchors.Add(points.Count - 1);

            var keep = new bool[points.Count];
            for (int i = 0; i < anchors.Count; i++)
                keep[anchors[i]] = true;
            float toleranceSquared = Math.Max(0f, tolerance) *
                                     Math.Max(0f, tolerance);
            for (int i = 1; i < anchors.Count; i++)
            {
                SimplifyRange(
                    points, anchors[i - 1], anchors[i],
                    toleranceSquared, keep);
            }

            var result = new List<BoundaryGridPoint>();
            for (int i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    result.Add(points[i]);
            }
            return result;
        }

        public static BoundaryCurveDraft Fit(
            IReadOnlyList<BoundaryGridPoint> pRawPoints,
            ISet<BoundaryGridPoint> pProtectedPoints,
            BoundaryCellRaster pRaster,
            BoundaryCurveOptions pOptions)
        {
            if (pRawPoints == null)
                throw new ArgumentNullException(nameof(pRawPoints));
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));

            List<BoundaryGridPoint> raw = CopyDistinct(pRawPoints);
            if (pOptions.Closed && raw.Count > 0 &&
                !raw[0].Equals(raw[raw.Count - 1]))
            {
                raw.Add(raw[0]);
            }
            if (raw.Count < 3)
                return RawFallback(raw, pOptions.Closed);

            ISet<BoundaryGridPoint> safetyAnchors = DeriveProtectedAnchors(
                raw, pProtectedPoints, pRaster, pOptions);
            ISet<BoundaryGridPoint> simplificationAnchors =
                ExpandAnchorNeighbors(raw, safetyAnchors);
            IReadOnlyList<BoundaryGridPoint> simplified = Simplify(
                raw, simplificationAnchors, 0.45f);
            if (pOptions.Closed && simplified.Count > 0 &&
                !simplified[0].Equals(simplified[simplified.Count - 1]))
            {
                var closed = new List<BoundaryGridPoint>(simplified);
                closed.Add(closed[0]);
                simplified = closed;
            }

            float[] tangentScales = { 1f, 0.5f, 0.25f };
            for (int i = 0; i < tangentScales.Length; i++)
            {
                IReadOnlyList<BoundaryFloatPoint> candidate = SampleCurve(
                    simplified, pOptions.Closed, tangentScales[i]);
                var draft = new BoundaryCurveDraft(
                    candidate, pOptions.Closed,
                    pUsedRawFallback: false, tangentScales[i]);
                if (IsSafeCurveWithAnchors(
                        draft, pRaster, raw, pOptions, safetyAnchors))
                    return draft;
            }
            return RawFallback(raw, pOptions.Closed);
        }

        public static ISet<BoundaryGridPoint> DeriveProtectedAnchors(
            IReadOnlyList<BoundaryGridPoint> pRawPoints,
            ISet<BoundaryGridPoint> pExplicitPoints,
            BoundaryCellRaster pRaster,
            BoundaryCurveOptions pOptions)
        {
            if (pRawPoints == null)
                throw new ArgumentNullException(nameof(pRawPoints));
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            var result = pExplicitPoints == null
                ? new HashSet<BoundaryGridPoint>()
                : new HashSet<BoundaryGridPoint>(pExplicitPoints);
            if (pRawPoints.Count == 0)
                return result;
            result.Add(pRawPoints[0]);
            result.Add(pRawPoints[pRawPoints.Count - 1]);

            var counts = new Dictionary<BoundaryGridPoint, int>();
            for (int i = 0; i < pRawPoints.Count; i++)
            {
                BoundaryGridPoint point = pRawPoints[i];
                counts[point] = counts.TryGetValue(point, out int count)
                    ? count + 1 : 1;
            }
            for (int i = 0; i < pRawPoints.Count; i++)
            {
                BoundaryGridPoint point = pRawPoints[i];
                if (counts[point] > 1 ||
                    IsNarrowAnchor(point, pRaster, pOptions) ||
                    TouchesSmallOwnerComponent(point, pRaster, pOptions.Tier,
                        pOptions.LeftOwnerId) ||
                    TouchesSmallOwnerComponent(point, pRaster, pOptions.Tier,
                        pOptions.RightOwnerId))
                {
                    result.Add(point);
                }
            }
            return result;
        }

        public static float LocalMaximumDeviationAt(
            BoundaryGridPoint pPoint,
            ISet<BoundaryGridPoint> pProtectedPoints,
            BoundaryCellRaster pRaster,
            BoundaryCurveOptions pOptions)
        {
            bool protectedPoint = pProtectedPoints != null &&
                                  pProtectedPoints.Contains(pPoint);
            return Math.Min(pOptions.MaximumDeviation,
                protectedPoint || IsNarrowAnchor(pPoint, pRaster, pOptions)
                    ? 0.20f : 0.45f);
        }

        public static BoundaryCurveDraft FitCanonicalWindow(
            IReadOnlyList<BoundaryGridPoint> pCanonicalPoints,
            int pStartIndex,
            int pEndIndex,
            ISet<BoundaryGridPoint> pProtectedPoints,
            BoundaryCellRaster pRaster,
            BoundaryCurveOptions pOptions)
        {
            if (pCanonicalPoints == null)
                throw new ArgumentNullException(nameof(pCanonicalPoints));
            if (pStartIndex < 0 || pEndIndex <= pStartIndex ||
                pEndIndex >= pCanonicalPoints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pStartIndex));
            }

            BoundaryCurveDraft canonical = Fit(
                pCanonicalPoints, pProtectedPoints, pRaster, pOptions);
            int sampleStart = FindControlSample(
                canonical.Points, pCanonicalPoints[pStartIndex], 0);
            int sampleEnd = FindControlSample(
                canonical.Points, pCanonicalPoints[pEndIndex], sampleStart + 1);
            if (sampleStart < 0 || sampleEnd < sampleStart)
                return RawFallback(CopyWindow(
                    pCanonicalPoints, pStartIndex, pEndIndex), false);

            var points = new List<BoundaryFloatPoint>(sampleEnd - sampleStart + 1);
            for (int i = sampleStart; i <= sampleEnd; i++)
                points.Add(canonical.Points[i]);
            return new BoundaryCurveDraft(
                points, pClosed: false, canonical.UsedRawFallback,
                canonical.TangentScale,
                CanonicalTangentAt(pCanonicalPoints, pStartIndex, pOptions.Closed),
                CanonicalTangentAt(pCanonicalPoints, pEndIndex, pOptions.Closed));
        }

        public static bool IsSafeSegment(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            IReadOnlyList<BoundaryGridPoint> pRawChain,
            float maximumDeviation,
            bool allowRiverWater)
        {
            if (!IsFinite(pStart) || !IsFinite(pEnd) || pRaster == null ||
                pRawChain == null || pRawChain.Count == 0)
            {
                return false;
            }

            if (!IsSupercoverSafe(
                    pStart, pEnd, pRaster, pTier,
                    pLeftOwnerId, pRightOwnerId, allowRiverWater,
                    pRawChain))
            {
                return false;
            }

            float dx = pEnd.X - pStart.X;
            float dy = pEnd.Y - pStart.Y;
            float length = SquareRoot(dx * dx + dy * dy);
            int steps = Math.Max(1, (int)Math.Ceiling(length / SampleStep));
            for (int i = 0; i <= steps; i++)
            {
                float ratio = (float)i / steps;
                var point = new BoundaryFloatPoint(
                    pStart.X + dx * ratio,
                    pStart.Y + dy * ratio);
                if (DistanceToPolyline(point, pRawChain) >
                    maximumDeviation + GridEpsilon)
                {
                    return false;
                }
                if (!IsPointAllowed(
                        point, pRaster, pTier,
                        pLeftOwnerId, pRightOwnerId, allowRiverWater))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsSafeCurve(
            BoundaryCurveDraft pCurve,
            BoundaryCellRaster pRaster,
            IReadOnlyList<BoundaryGridPoint> pRawChain,
            BoundaryCurveOptions pOptions)
        {
            if (pCurve == null || !pCurve.IsValid ||
                pCurve.Points == null || pCurve.Points.Count < 2)
            {
                return false;
            }
            for (int i = 0; i < pCurve.Points.Count; i++)
            {
                if (!IsFinite(pCurve.Points[i]))
                    return false;
            }
            for (int i = 1; i < pCurve.Points.Count; i++)
            {
                if (!pCurve.UsedRawFallback && !IsSafeSegment(
                        pCurve.Points[i - 1], pCurve.Points[i], pRaster,
                        pOptions.Tier, pOptions.LeftOwnerId,
                        pOptions.RightOwnerId, pRawChain,
                        pOptions.MaximumDeviation,
                        pOptions.AllowRiverWater))
                {
                    return false;
                }
            }
            if (!pCurve.UsedRawFallback &&
                HasSelfIntersection(pCurve.Points, pCurve.Closed))
                return false;
            if (pCurve.Closed)
            {
                int rawSign = Math.Sign(SignedArea(pRawChain));
                int curveSign = Math.Sign(SignedArea(pCurve.Points));
                if (rawSign != 0 && curveSign != rawSign)
                    return false;
            }
            return true;
        }

        private static bool IsSafeCurveWithAnchors(
            BoundaryCurveDraft pCurve,
            BoundaryCellRaster pRaster,
            IReadOnlyList<BoundaryGridPoint> pRawChain,
            BoundaryCurveOptions pOptions,
            ISet<BoundaryGridPoint> pSafetyAnchors)
        {
            if (pCurve == null || pCurve.Points == null ||
                pCurve.Points.Count < 2)
            {
                return false;
            }
            for (int i = 1; i < pCurve.Points.Count; i++)
            {
                float maximumDeviation = pOptions.MaximumDeviation;
                foreach (BoundaryGridPoint anchor in pSafetyAnchors)
                {
                    if (DistanceToSegment(ToFloat(anchor),
                            pCurve.Points[i - 1], pCurve.Points[i]) <= 1f)
                    {
                        maximumDeviation = Math.Min(maximumDeviation, 0.20f);
                        break;
                    }
                }
                if (!IsSafeSegment(
                        pCurve.Points[i - 1], pCurve.Points[i], pRaster,
                        pOptions.Tier, pOptions.LeftOwnerId,
                        pOptions.RightOwnerId, pRawChain,
                        maximumDeviation, pOptions.AllowRiverWater))
                {
                    return false;
                }
            }
            if (HasSelfIntersection(pCurve.Points, pCurve.Closed))
                return false;
            if (pCurve.Closed)
            {
                int rawSign = Math.Sign(SignedArea(pRawChain));
                int curveSign = Math.Sign(SignedArea(pCurve.Points));
                if (rawSign != 0 && curveSign != rawSign)
                    return false;
            }
            return true;
        }

        public static BoundaryFloatPoint CanonicalTangentAt(
            IReadOnlyList<BoundaryGridPoint> pContext,
            int pIndex)
        {
            if (pContext == null)
                throw new ArgumentNullException(nameof(pContext));
            if (pIndex < 0 || pIndex >= pContext.Count)
                throw new ArgumentOutOfRangeException(nameof(pIndex));
            int previous = Math.Max(0, pIndex - 1);
            int next = Math.Min(pContext.Count - 1, pIndex + 1);
            return new BoundaryFloatPoint(
                (pContext[next].X - pContext[previous].X) * 0.5f,
                (pContext[next].Y - pContext[previous].Y) * 0.5f);
        }

        private static BoundaryFloatPoint CanonicalTangentAt(
            IReadOnlyList<BoundaryGridPoint> pContext,
            int pIndex,
            bool pClosed)
        {
            if (!pClosed)
                return CanonicalTangentAt(pContext, pIndex);
            int uniqueCount = pContext.Count - 1;
            int index = pIndex == uniqueCount ? 0 : pIndex;
            int previous = (index + uniqueCount - 1) % uniqueCount;
            int next = (index + 1) % uniqueCount;
            return new BoundaryFloatPoint(
                (pContext[next].X - pContext[previous].X) * 0.5f,
                (pContext[next].Y - pContext[previous].Y) * 0.5f);
        }

        public static float SignedArea(IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            if (pPoints == null || pPoints.Count < 3)
                return 0f;
            double area = 0d;
            for (int i = 0; i < pPoints.Count; i++)
            {
                BoundaryGridPoint current = pPoints[i];
                BoundaryGridPoint next = pPoints[(i + 1) % pPoints.Count];
                area += (double)current.X * next.Y -
                        (double)next.X * current.Y;
            }
            return (float)(area * 0.5d);
        }

        public static float SignedArea(IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            if (pPoints == null || pPoints.Count < 3)
                return 0f;
            double area = 0d;
            for (int i = 0; i < pPoints.Count; i++)
            {
                BoundaryFloatPoint current = pPoints[i];
                BoundaryFloatPoint next = pPoints[(i + 1) % pPoints.Count];
                area += (double)current.X * next.Y -
                        (double)next.X * current.Y;
            }
            return (float)(area * 0.5d);
        }

        public static BoundaryFloatPoint EvaluateCentripetal(
            BoundaryFloatPoint p0,
            BoundaryFloatPoint p1,
            BoundaryFloatPoint p2,
            BoundaryFloatPoint p3,
            float pT)
        {
            return CatmullRom(p0, p1, p2, p3,
                Math.Max(0f, Math.Min(1f, pT)));
        }

        public static IReadOnlyList<BoundaryFloatPoint> SampleCentripetalSpan(
            BoundaryFloatPoint p0,
            BoundaryFloatPoint p1,
            BoundaryFloatPoint p2,
            BoundaryFloatPoint p3,
            float pTangentScale)
        {
            var result = new List<BoundaryFloatPoint> { p1 };
            BoundaryFloatPoint end = p2;
            SubdivideCentripetalSpan(
                p0, p1, p2, p3, pTangentScale,
                0f, 1f, p1, end, 0, result);
            return result;
        }

        private static IReadOnlyList<BoundaryFloatPoint> SampleCurve(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            bool pClosed,
            float pTangentScale)
        {
            int uniqueCount = pClosed ? pPoints.Count - 1 : pPoints.Count;
            if (uniqueCount < 2)
                return ToFloatPoints(pPoints);

            int segmentCount = pClosed ? uniqueCount : uniqueCount - 1;
            var result = new List<BoundaryFloatPoint>();
            for (int segment = 0; segment < segmentCount; segment++)
            {
                BoundaryFloatPoint p1 = At(pPoints, segment, uniqueCount, pClosed);
                BoundaryFloatPoint p2 = At(pPoints, segment + 1, uniqueCount, pClosed);
                BoundaryFloatPoint p0 = At(pPoints, segment - 1, uniqueCount, pClosed);
                BoundaryFloatPoint p3 = At(pPoints, segment + 2, uniqueCount, pClosed);
                IReadOnlyList<BoundaryFloatPoint> span =
                    SampleCentripetalSpan(p0, p1, p2, p3, pTangentScale);
                int start = segment == 0 ? 0 : 1;
                for (int i = start; i < span.Count; i++)
                    result.Add(span[i]);
            }
            if (pClosed && result.Count > 0 &&
                !result[0].Equals(result[result.Count - 1]))
            {
                result.Add(result[0]);
            }
            return result;
        }

        private static void SubdivideCentripetalSpan(
            BoundaryFloatPoint p0,
            BoundaryFloatPoint p1,
            BoundaryFloatPoint p2,
            BoundaryFloatPoint p3,
            float pTangentScale,
            float pStartTime,
            float pEndTime,
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            int pDepth,
            List<BoundaryFloatPoint> pResult)
        {
            float middleTime = (pStartTime + pEndTime) * 0.5f;
            BoundaryFloatPoint middle = EvaluateScaledCentripetal(
                p0, p1, p2, p3, middleTime, pTangentScale);
            float leftLength = Distance(pStart, middle);
            float rightLength = Distance(middle, pEnd);
            float chordLength = Distance(pStart, pEnd);
            bool spacingSafe = chordLength <= 0.30f &&
                               leftLength <= 0.30f && rightLength <= 0.30f;
            bool flatEnough = leftLength + rightLength - chordLength <= 0.005f;
            if (pDepth >= 20 || spacingSafe && flatEnough)
            {
                pResult.Add(pEnd);
                return;
            }
            SubdivideCentripetalSpan(
                p0, p1, p2, p3, pTangentScale,
                pStartTime, middleTime, pStart, middle,
                pDepth + 1, pResult);
            SubdivideCentripetalSpan(
                p0, p1, p2, p3, pTangentScale,
                middleTime, pEndTime, middle, pEnd,
                pDepth + 1, pResult);
        }

        private static BoundaryFloatPoint EvaluateScaledCentripetal(
            BoundaryFloatPoint p0,
            BoundaryFloatPoint p1,
            BoundaryFloatPoint p2,
            BoundaryFloatPoint p3,
            float pTime,
            float pTangentScale)
        {
            BoundaryFloatPoint curved = CatmullRom(p0, p1, p2, p3, pTime);
            BoundaryFloatPoint linear = new BoundaryFloatPoint(
                p1.X + (p2.X - p1.X) * pTime,
                p1.Y + (p2.Y - p1.Y) * pTime);
            return new BoundaryFloatPoint(
                linear.X + (curved.X - linear.X) * pTangentScale,
                linear.Y + (curved.Y - linear.Y) * pTangentScale);
        }

        private static BoundaryFloatPoint CatmullRom(
            BoundaryFloatPoint p0,
            BoundaryFloatPoint p1,
            BoundaryFloatPoint p2,
            BoundaryFloatPoint p3,
            float pT)
        {
            float t0 = 0f;
            float t1 = t0 + KnotDelta(p0, p1);
            float t2 = t1 + KnotDelta(p1, p2);
            float t3 = t2 + KnotDelta(p2, p3);
            float t = t1 + (t2 - t1) * pT;

            BoundaryFloatPoint a1 = Interpolate(p0, p1, t0, t1, t);
            BoundaryFloatPoint a2 = Interpolate(p1, p2, t1, t2, t);
            BoundaryFloatPoint a3 = Interpolate(p2, p3, t2, t3, t);
            BoundaryFloatPoint b1 = Interpolate(a1, a2, t0, t2, t);
            BoundaryFloatPoint b2 = Interpolate(a2, a3, t1, t3, t);
            return Interpolate(b1, b2, t1, t2, t);
        }

        private static float KnotDelta(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd)
        {
            float dx = pEnd.X - pStart.X;
            float dy = pEnd.Y - pStart.Y;
            return Math.Max(GridEpsilon,
                SquareRoot(SquareRoot(dx * dx + dy * dy)));
        }

        private static BoundaryFloatPoint Interpolate(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            float pStartTime,
            float pEndTime,
            float pTime)
        {
            float ratio = (pTime - pStartTime) / (pEndTime - pStartTime);
            return new BoundaryFloatPoint(
                pStart.X + (pEnd.X - pStart.X) * ratio,
                pStart.Y + (pEnd.Y - pStart.Y) * ratio);
        }

        private static BoundaryFloatPoint At(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            int pIndex,
            int pUniqueCount,
            bool pClosed)
        {
            if (pClosed)
            {
                int wrapped = pIndex % pUniqueCount;
                if (wrapped < 0)
                    wrapped += pUniqueCount;
                return ToFloat(pPoints[wrapped]);
            }
            int clamped = Math.Max(0, Math.Min(pUniqueCount - 1, pIndex));
            return ToFloat(pPoints[clamped]);
        }

        private static bool IsPointAllowed(
            BoundaryFloatPoint pPoint,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            bool pAllowRiverWater)
        {
            int floorX = (int)Math.Floor(pPoint.X);
            int floorY = (int)Math.Floor(pPoint.Y);
            bool onX = Math.Abs(pPoint.X - Math.Round(pPoint.X)) <= GridEpsilon;
            bool onY = Math.Abs(pPoint.Y - Math.Round(pPoint.Y)) <= GridEpsilon;
            int xCount = onX ? 2 : 1;
            int yCount = onY ? 2 : 1;
            for (int ix = 0; ix < xCount; ix++)
            {
                int x = floorX - (onX && ix == 1 ? 1 : 0);
                for (int iy = 0; iy < yCount; iy++)
                {
                    int y = floorY - (onY && iy == 1 ? 1 : 0);
                    BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                    if (pAllowRiverWater && cell.IsValid &&
                        cell.Water == BoundaryWaterKind.InlandWater)
                    {
                        return true;
                    }
                    if (!cell.IsLand)
                        continue;
                    long owner = OwnerId(cell, pTier);
                    if (owner == pLeftOwnerId || owner == pRightOwnerId)
                        return true;
                }
            }
            return false;
        }

        private static bool IsSupercoverSafe(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            bool pAllowRiverWater,
            IReadOnlyList<BoundaryGridPoint> pRawChain)
        {
            float dx = pEnd.X - pStart.X;
            float dy = pEnd.Y - pStart.Y;
            if (dx * dx + dy * dy <= GridEpsilon * GridEpsilon)
                return false;

            var times = new List<float> { 0f, 1f };
            AddGridCrossingTimes(pStart.X, dx, times);
            AddGridCrossingTimes(pStart.Y, dy, times);
            times.Sort();

            var cells = new HashSet<BoundaryGridPoint>();
            float previous = times[0];
            for (int i = 1; i < times.Count; i++)
            {
                float current = times[i];
                if (current - previous > GridEpsilon)
                {
                    AddTouchedCells(PointAt(
                        pStart, dx, dy, (previous + current) * 0.5f), cells);
                }
                if (current > GridEpsilon && current < 1f - GridEpsilon)
                {
                    BoundaryFloatPoint crossing =
                        PointAt(pStart, dx, dy, current);
                    if (IsGridCorner(crossing) &&
                        !RawContains(pRawChain,
                            new BoundaryGridPoint(
                                (int)Math.Round(crossing.X),
                                (int)Math.Round(crossing.Y))))
                    {
                        return false;
                    }
                    AddTouchedCells(crossing, cells);
                }
                previous = current;
            }
            AddTouchedCells(PointAt(pStart, dx, dy, 0.5f), cells);

            foreach (BoundaryGridPoint key in cells)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(key.X, key.Y);
                if (IsForbiddenSupercoverCell(
                        cell, pTier, pLeftOwnerId,
                        pRightOwnerId, pAllowRiverWater))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsGridCorner(BoundaryFloatPoint pPoint)
        {
            return Math.Abs(pPoint.X - Math.Round(pPoint.X)) <= GridEpsilon &&
                   Math.Abs(pPoint.Y - Math.Round(pPoint.Y)) <= GridEpsilon;
        }

        private static bool RawContains(
            IReadOnlyList<BoundaryGridPoint> pRaw,
            BoundaryGridPoint pPoint)
        {
            for (int i = 0; i < pRaw.Count; i++)
            {
                if (pRaw[i].Equals(pPoint))
                    return true;
            }
            return false;
        }

        private static void AddGridCrossingTimes(
            float pStart,
            float pDelta,
            List<float> pTimes)
        {
            if (Math.Abs(pDelta) <= GridEpsilon)
                return;
            float end = pStart + pDelta;
            int first = (int)Math.Ceiling(Math.Min(pStart, end));
            int last = (int)Math.Floor(Math.Max(pStart, end));
            for (int grid = first; grid <= last; grid++)
            {
                float ratio = (grid - pStart) / pDelta;
                if (ratio <= GridEpsilon || ratio >= 1f - GridEpsilon)
                    continue;
                bool duplicate = false;
                for (int i = 0; i < pTimes.Count; i++)
                {
                    if (Math.Abs(pTimes[i] - ratio) <= GridEpsilon)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    pTimes.Add(ratio);
            }
        }

        private static BoundaryFloatPoint PointAt(
            BoundaryFloatPoint pStart,
            float pDx,
            float pDy,
            float pTime)
        {
            return new BoundaryFloatPoint(
                pStart.X + pDx * pTime,
                pStart.Y + pDy * pTime);
        }

        private static void AddTouchedCells(
            BoundaryFloatPoint pPoint,
            ISet<BoundaryGridPoint> pCells)
        {
            int floorX = (int)Math.Floor(pPoint.X);
            int floorY = (int)Math.Floor(pPoint.Y);
            bool onX = Math.Abs(pPoint.X - Math.Round(pPoint.X)) <= GridEpsilon;
            bool onY = Math.Abs(pPoint.Y - Math.Round(pPoint.Y)) <= GridEpsilon;
            int minimumX = onX ? floorX - 1 : floorX;
            int minimumY = onY ? floorY - 1 : floorY;
            for (int x = minimumX; x <= floorX; x++)
            for (int y = minimumY; y <= floorY; y++)
                pCells.Add(new BoundaryGridPoint(x, y));
        }

        private static bool IsForbiddenSupercoverCell(
            BoundaryCellFacts pCell,
            BoundaryTier pTier,
            long pLeftOwnerId,
            long pRightOwnerId,
            bool pAllowRiverWater)
        {
            if (!pCell.IsValid)
                return true;
            if (pAllowRiverWater &&
                pCell.Water == BoundaryWaterKind.InlandWater)
            {
                return false;
            }
            if (!pCell.IsLand)
                return true;
            long owner = OwnerId(pCell, pTier);
            return owner != pLeftOwnerId && owner != pRightOwnerId;
        }

        private static float DistanceToPolyline(
            BoundaryFloatPoint pPoint,
            IReadOnlyList<BoundaryGridPoint> pPolyline)
        {
            if (pPolyline.Count == 1)
                return Distance(pPoint, ToFloat(pPolyline[0]));
            float minimum = float.MaxValue;
            for (int i = 1; i < pPolyline.Count; i++)
            {
                minimum = Math.Min(minimum, DistanceToSegment(
                    pPoint, ToFloat(pPolyline[i - 1]), ToFloat(pPolyline[i])));
            }
            return minimum;
        }

        private static float DistanceToSegment(
            BoundaryFloatPoint pPoint,
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd)
        {
            float dx = pEnd.X - pStart.X;
            float dy = pEnd.Y - pStart.Y;
            float lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= GridEpsilon)
                return Distance(pPoint, pStart);
            float t = ((pPoint.X - pStart.X) * dx +
                       (pPoint.Y - pStart.Y) * dy) / lengthSquared;
            t = Math.Max(0f, Math.Min(1f, t));
            return Distance(pPoint, new BoundaryFloatPoint(
                pStart.X + dx * t, pStart.Y + dy * t));
        }

        private static bool HasSelfIntersection(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            bool pClosed)
        {
            int segmentCount = pPoints.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                for (int j = i + 2; j < segmentCount; j++)
                {
                    if (pClosed && i == 0 && j == segmentCount - 1)
                        continue;
                    if (SegmentsIntersect(
                            pPoints[i], pPoints[i + 1],
                            pPoints[j], pPoints[j + 1]))
                    {
                        return true;
                    }
                }
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
            if (abC * abD < -GridEpsilon && cdA * cdB < -GridEpsilon)
                return true;
            return Math.Abs(abC) <= GridEpsilon && OnSegment(pA, pB, pC) ||
                   Math.Abs(abD) <= GridEpsilon && OnSegment(pA, pB, pD) ||
                   Math.Abs(cdA) <= GridEpsilon && OnSegment(pC, pD, pA) ||
                   Math.Abs(cdB) <= GridEpsilon && OnSegment(pC, pD, pB);
        }

        private static bool OnSegment(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            BoundaryFloatPoint pPoint)
        {
            return pPoint.X >= Math.Min(pStart.X, pEnd.X) - GridEpsilon &&
                   pPoint.X <= Math.Max(pStart.X, pEnd.X) + GridEpsilon &&
                   pPoint.Y >= Math.Min(pStart.Y, pEnd.Y) - GridEpsilon &&
                   pPoint.Y <= Math.Max(pStart.Y, pEnd.Y) + GridEpsilon;
        }

        private static float Cross(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC)
        {
            return (pB.X - pA.X) * (pC.Y - pA.Y) -
                   (pB.Y - pA.Y) * (pC.X - pA.X);
        }

        private static void SimplifyRange(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            int pStart,
            int pEnd,
            float pToleranceSquared,
            bool[] pKeep)
        {
            if (pEnd <= pStart + 1)
                return;
            float maximum = -1f;
            int maximumIndex = -1;
            BoundaryFloatPoint start = ToFloat(pPoints[pStart]);
            BoundaryFloatPoint end = ToFloat(pPoints[pEnd]);
            for (int i = pStart + 1; i < pEnd; i++)
            {
                float distance = DistanceToSegment(
                    ToFloat(pPoints[i]), start, end);
                float squared = distance * distance;
                if (squared > maximum)
                {
                    maximum = squared;
                    maximumIndex = i;
                }
            }
            if (maximum <= pToleranceSquared)
                return;
            pKeep[maximumIndex] = true;
            SimplifyRange(
                pPoints, pStart, maximumIndex, pToleranceSquared, pKeep);
            SimplifyRange(
                pPoints, maximumIndex, pEnd, pToleranceSquared, pKeep);
        }

        private static BoundaryCurveDraft RawFallback(
            IReadOnlyList<BoundaryGridPoint> pRaw,
            bool pClosed)
        {
            return new BoundaryCurveDraft(
                ToFloatPoints(pRaw), pClosed,
                pUsedRawFallback: true, pTangentScale: 0f);
        }

        private static List<BoundaryGridPoint> CopyDistinct(
            IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            var result = new List<BoundaryGridPoint>(pPoints.Count);
            for (int i = 0; i < pPoints.Count; i++)
            {
                if (result.Count == 0 ||
                    !result[result.Count - 1].Equals(pPoints[i]))
                {
                    result.Add(pPoints[i]);
                }
            }
            return result;
        }

        private static IReadOnlyList<BoundaryFloatPoint> ToFloatPoints(
            IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            var result = new List<BoundaryFloatPoint>(pPoints.Count);
            for (int i = 0; i < pPoints.Count; i++)
                result.Add(ToFloat(pPoints[i]));
            return result;
        }

        private static ISet<BoundaryGridPoint> ExpandAnchorNeighbors(
            IReadOnlyList<BoundaryGridPoint> pRaw,
            ISet<BoundaryGridPoint> pAnchors)
        {
            var result = new HashSet<BoundaryGridPoint>(pAnchors);
            for (int i = 0; i < pRaw.Count; i++)
            {
                if (!pAnchors.Contains(pRaw[i]))
                    continue;
                if (i > 0)
                    result.Add(pRaw[i - 1]);
                if (i + 1 < pRaw.Count)
                    result.Add(pRaw[i + 1]);
            }
            return result;
        }

        private static bool IsNarrowAnchor(
            BoundaryGridPoint pPoint,
            BoundaryCellRaster pRaster,
            BoundaryCurveOptions pOptions)
        {
            int allowed = 0;
            for (int x = pPoint.X - 1; x <= pPoint.X; x++)
            for (int y = pPoint.Y - 1; y <= pPoint.Y; y++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                if (!cell.IsLand)
                    continue;
                long owner = OwnerId(cell, pOptions.Tier);
                if (owner == pOptions.LeftOwnerId ||
                    owner == pOptions.RightOwnerId)
                {
                    allowed++;
                }
            }
            return allowed < 4;
        }

        private static bool TouchesSmallOwnerComponent(
            BoundaryGridPoint pPoint,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier,
            long pOwnerId)
        {
            var pending = new Queue<BoundaryGridPoint>();
            var visited = new HashSet<BoundaryGridPoint>();
            for (int x = pPoint.X - 1; x <= pPoint.X; x++)
            for (int y = pPoint.Y - 1; y <= pPoint.Y; y++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                var key = new BoundaryGridPoint(x, y);
                if (cell.IsLand && OwnerId(cell, pTier) == pOwnerId &&
                    visited.Add(key))
                {
                    pending.Enqueue(key);
                }
            }
            int count = 0;
            int[] offsetsX = { -1, 1, 0, 0 };
            int[] offsetsY = { 0, 0, -1, 1 };
            while (pending.Count > 0)
            {
                BoundaryGridPoint current = pending.Dequeue();
                count++;
                if (count > 4)
                    return false;
                for (int i = 0; i < offsetsX.Length; i++)
                {
                    var next = new BoundaryGridPoint(
                        current.X + offsetsX[i], current.Y + offsetsY[i]);
                    if (visited.Contains(next))
                        continue;
                    BoundaryCellFacts cell =
                        pRaster.GetOrInvalid(next.X, next.Y);
                    if (!cell.IsLand || OwnerId(cell, pTier) != pOwnerId)
                        continue;
                    visited.Add(next);
                    pending.Enqueue(next);
                }
            }
            return count > 0;
        }

        private static int FindControlSample(
            IReadOnlyList<BoundaryFloatPoint> pSamples,
            BoundaryGridPoint pControl,
            int pStart)
        {
            for (int i = Math.Max(0, pStart); i < pSamples.Count; i++)
            {
                if (pSamples[i].X == pControl.X &&
                    pSamples[i].Y == pControl.Y)
                {
                    return i;
                }
            }
            return -1;
        }

        private static IReadOnlyList<BoundaryGridPoint> CopyWindow(
            IReadOnlyList<BoundaryGridPoint> pPoints,
            int pStart,
            int pEnd)
        {
            var result = new List<BoundaryGridPoint>(pEnd - pStart + 1);
            for (int i = pStart; i <= pEnd; i++)
                result.Add(pPoints[i]);
            return result;
        }

        private static BoundaryFloatPoint ToFloat(BoundaryGridPoint pPoint)
        {
            return new BoundaryFloatPoint(pPoint.X, pPoint.Y);
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

        private static bool IsFinite(BoundaryFloatPoint pPoint)
        {
            return !float.IsNaN(pPoint.X) && !float.IsInfinity(pPoint.X) &&
                   !float.IsNaN(pPoint.Y) && !float.IsInfinity(pPoint.Y);
        }

        private static float Distance(
            BoundaryFloatPoint pFirst,
            BoundaryFloatPoint pSecond)
        {
            float dx = pFirst.X - pSecond.X;
            float dy = pFirst.Y - pSecond.Y;
            return SquareRoot(dx * dx + dy * dy);
        }

        private static float SquareRoot(float pValue)
        {
            return (float)Math.Sqrt(pValue);
        }
    }
}
