using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    [Flags]
    public enum BoundaryRibbonFlags : byte
    {
        None = 0,
        River = 1,
        Coast = 2,
        Transparent = 4,
        RawFallback = 8
    }

    public enum BoundaryRibbonCoastSide : byte
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public sealed class BoundaryRibbonInput
    {
        public BoundaryRibbonInput(
            BoundaryCurveDraft curve,
            IReadOnlyList<BoundaryGridPoint> rawPoints,
            BoundaryTier tier,
            long leftOwnerId,
            long rightOwnerId,
            uint leftRgba,
            uint rightRgba,
            bool isRiver,
            BoundaryRibbonCoastSide coastSide)
        {
            Curve = curve;
            RawPoints = rawPoints;
            Tier = tier;
            LeftOwnerId = leftOwnerId;
            RightOwnerId = rightOwnerId;
            LeftRgba = leftRgba;
            RightRgba = rightRgba;
            IsRiver = isRiver;
            CoastSide = coastSide;
        }

        public BoundaryCurveDraft Curve { get; }
        public IReadOnlyList<BoundaryGridPoint> RawPoints { get; }
        public BoundaryTier Tier { get; }
        public long LeftOwnerId { get; }
        public long RightOwnerId { get; }
        public uint LeftRgba { get; }
        public uint RightRgba { get; }
        public bool IsRiver { get; }
        public BoundaryRibbonCoastSide CoastSide { get; }
    }

    public sealed class BoundaryMeshDraft
    {
        public BoundaryMeshDraft(
            float[] pPositionX,
            float[] pPositionY,
            float[] pNormalX,
            float[] pNormalY,
            float[] pSignedDistance,
            byte[] pTiers,
            long[] pLeftOwnerIds,
            long[] pRightOwnerIds,
            uint[] pLeftRgba,
            uint[] pRightRgba,
            float[] pLocalHalfWidths,
            byte[] pFlags,
            byte[] pPoliticalAlpha,
            byte[] pSegmentFallbacks,
            int[] pCityIndices,
            int[] pVassalRealmIndices,
            int[] pSuzerainSystemIndices,
            int[] pCenterLineIndices,
            int pFailureCount = 0)
        {
            PositionX = pPositionX ?? Array.Empty<float>();
            PositionY = pPositionY ?? Array.Empty<float>();
            NormalX = pNormalX ?? Array.Empty<float>();
            NormalY = pNormalY ?? Array.Empty<float>();
            SignedDistance = pSignedDistance ?? Array.Empty<float>();
            Tiers = pTiers ?? Array.Empty<byte>();
            LeftOwnerIds = pLeftOwnerIds ?? Array.Empty<long>();
            RightOwnerIds = pRightOwnerIds ?? Array.Empty<long>();
            LeftRgba = pLeftRgba ?? Array.Empty<uint>();
            RightRgba = pRightRgba ?? Array.Empty<uint>();
            LocalHalfWidths = pLocalHalfWidths ?? Array.Empty<float>();
            Flags = pFlags ?? Array.Empty<byte>();
            PoliticalAlpha = pPoliticalAlpha ?? Array.Empty<byte>();
            SegmentFallbacks = pSegmentFallbacks ?? Array.Empty<byte>();
            CityIndices = pCityIndices ?? Array.Empty<int>();
            VassalRealmIndices = pVassalRealmIndices ?? Array.Empty<int>();
            SuzerainSystemIndices = pSuzerainSystemIndices ?? Array.Empty<int>();
            CenterLineIndices = pCenterLineIndices ?? Array.Empty<int>();
            FailureCount = pFailureCount;
        }

        public int VertexCount { get { return PositionX.Length; } }
        public float[] PositionX { get; }
        public float[] PositionY { get; }
        public float[] NormalX { get; }
        public float[] NormalY { get; }
        public float[] SignedDistance { get; }
        public byte[] Tiers { get; }
        public long[] LeftOwnerIds { get; }
        public long[] RightOwnerIds { get; }
        public uint[] LeftRgba { get; }
        public uint[] RightRgba { get; }
        public float[] LocalHalfWidths { get; }
        public byte[] Flags { get; }
        public byte[] PoliticalAlpha { get; }
        public byte[] SegmentFallbacks { get; }
        public int[] CityIndices { get; }
        public int[] VassalRealmIndices { get; }
        public int[] SuzerainSystemIndices { get; }
        public int[] CenterLineIndices { get; }
        public int FailureCount { get; }
    }

    public static class HierarchicalVassalBoundaryMeshDraftRules
    {
        private const float Epsilon = 0.0001f;
        private const float MaximumCorridor = 0.45f;
        private const int MaximumRibbonPointCount = 65536;
        private const int MaximumTriangleFootprintCells = 262144;

        public static float TargetWidth(BoundaryTier pTier)
        {
            switch (pTier)
            {
                case BoundaryTier.City:
                    return 0.12f;
                case BoundaryTier.VassalRealm:
                    return 0.20f;
                case BoundaryTier.SuzerainSystem:
                    return 0.32f;
                default:
                    return 0f;
            }
        }

        internal static BoundaryMeshDraft BuildFillNonAuthoritativeForTests(
            BoundaryCellRaster pRaster)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            var bounds = new BoundaryChunkBounds(
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive,
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive);
            return BuildFillNonAuthoritativeForTests(
                pRaster, BoundaryDisplayLayer.Countries, bounds);
        }

        internal static BoundaryMeshDraft BuildFillNonAuthoritativeForTests(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            BoundaryChunkBounds pBounds)
        {
            // Compatibility-only; authoritative rendering supplies an assignment.
            return BuildFillCore(pRaster, pLayer, pBounds, null);
        }

        public static BoundaryMeshDraft BuildFillAuthoritative(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            if (pAssignment == null)
                return EmptyFailureDraft(1);
            return BuildFillCore(pRaster, pLayer, pBounds, pAssignment);
        }

        private static BoundaryMeshDraft BuildFillCore(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            BoundaryChunkBounds pBounds,
            HierarchyColorAssignment pAssignment)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            if (pAssignment != null && !pAssignment.IsValid)
                return EmptyFailureDraft(1);
            BoundarySafeBoundsStatus boundsStatus =
                BoundarySafeBoundsGate.Evaluate(pBounds);
            if (boundsStatus == BoundarySafeBoundsStatus.Invalid)
                return EmptyFailureDraft(1);
            if (boundsStatus == BoundarySafeBoundsStatus.Empty)
                return EmptyFailureDraft(0);
            BoundaryTier fillTier = pLayer == BoundaryDisplayLayer.Cities
                ? BoundaryTier.City
                : BoundaryTier.VassalRealm;
            var owners = new SortedSet<long>();
            for (int y = pBounds.InteriorMinY;
                 y < pBounds.InteriorMaxYExclusive; y++)
            for (int x = pBounds.InteriorMinX;
                 x < pBounds.InteriorMaxXExclusive; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                long owner = OwnerId(cell, fillTier);
                if (cell.IsLand && owner >= 0)
                    owners.Add(owner);
            }

            var positions = new List<BoundaryFloatPoint>();
            var ownerIds = new List<long>();
            var colors = new List<uint>();
            var cityIndices = new List<int>();
            var vassalIndices = new List<int>();
            var systemIndices = new List<int>();
            var ownerColors = new Dictionary<long, uint>();
            foreach (long owner in owners)
            {
                uint color;
                if (pAssignment != null)
                {
                    if (!pAssignment.TryGetColor(fillTier, owner, out color))
                        return EmptyFailureDraft(1);
                }
                else
                {
                    BoundaryCellFacts facts = FindOwnerFacts(
                        pRaster, owner, fillTier);
                    color = HierarchicalVassalBoundaryColorRules.CandidateColor(
                        new HierarchyColorIdentity(
                            fillTier, owner,
                            facts.SystemId, facts.SystemId,
                            facts.RealmId, facts.CityId, facts.Rgba), 0);
                }
                ownerColors.Add(owner, color);
            }
            foreach (long owner in owners)
            {
                BoundaryPolygonDraft polygon =
                    HierarchicalVassalBoundaryPolygonRules.BuildOwnerPolygon(
                        pRaster, owner, fillTier, pBounds);
                if (!polygon.IsValid)
                    return EmptyFailureDraft(1);
                int offset = positions.Count;
                uint color = ownerColors[owner];
                for (int i = 0; i < polygon.Positions.Length; i++)
                {
                    positions.Add(polygon.Positions[i]);
                    ownerIds.Add(owner);
                    colors.Add(color);
                }
                List<int> target = IndicesFor(
                    fillTier, cityIndices, vassalIndices, systemIndices);
                for (int i = 0; i < polygon.Indices.Length; i++)
                {
                    int index = offset + polygon.Indices[i];
                    target.Add(index);
                }
            }

            int count = positions.Count;
            var positionX = new float[count];
            var positionY = new float[count];
            for (int i = 0; i < count; i++)
            {
                positionX[i] = positions[i].X;
                positionY[i] = positions[i].Y;
            }
            return new BoundaryMeshDraft(
                positionX, positionY, new float[count], new float[count],
                new float[count], Fill(count, (byte)fillTier), ownerIds.ToArray(),
                Fill(count, -1L), colors.ToArray(), new uint[count],
                new float[count], new byte[count], Fill(count, (byte)255),
                Array.Empty<byte>(), cityIndices.ToArray(),
                vassalIndices.ToArray(), systemIndices.ToArray(),
                Array.Empty<int>());
        }

        public static BoundaryMeshDraft BuildRibbons(
            IReadOnlyList<BoundaryRibbonInput> pInputs,
            BoundaryCellRaster pRaster)
        {
            if (pInputs == null)
                throw new ArgumentNullException(nameof(pInputs));
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));

            var positionX = new List<float>();
            var positionY = new List<float>();
            var normalX = new List<float>();
            var normalY = new List<float>();
            var signedDistance = new List<float>();
            var tiers = new List<byte>();
            var leftOwners = new List<long>();
            var rightOwners = new List<long>();
            var leftColors = new List<uint>();
            var rightColors = new List<uint>();
            var halfWidths = new List<float>();
            var flags = new List<byte>();
            var alpha = new List<byte>();
            var fallbacks = new List<byte>();
            var city = new List<int>();
            var vassal = new List<int>();
            var system = new List<int>();
            var centerLine = new List<int>();
            int failureCount = 0;

            for (int inputIndex = 0; inputIndex < pInputs.Count; inputIndex++)
            {
                BoundaryRibbonInput input = pInputs[inputIndex];
                if (!RibbonInputIsValid(input))
                {
                    failureCount++;
                    continue;
                }
                IReadOnlyList<BoundaryFloatPoint> samples =
                    Resample(input.Curve.Points, 0.20f);
                BoundaryCurveDraft effectiveCurve = input.Curve;
                bool rawFallback = false;
                float targetHalfWidth = TargetWidth(input.Tier) * 0.5f;
                float[] widths = SafeWidths(
                    samples, input.Curve, targetHalfWidth, pRaster, input);
                ConstrainSegmentFootprints(
                    samples, input.Curve, widths, pRaster, input);
                if (HasZeroWidth(widths))
                {
                    effectiveCurve = BuildRawCurve(input.RawPoints);
                    samples = Resample(effectiveCurve.Points, 0.20f);
                    widths = SafeWidths(
                        samples, effectiveCurve, targetHalfWidth, pRaster, input);
                    ConstrainSegmentFootprints(
                        samples, effectiveCurve, widths, pRaster, input);
                    rawFallback = true;
                }
                if (HasZeroWidth(widths))
                {
                    failureCount++;
                    continue;
                }
                int firstVertex = positionX.Count;
                for (int pointIndex = 0;
                     pointIndex < samples.Count; pointIndex++)
                {
                    BoundaryFloatPoint center = samples[pointIndex];
                    BoundaryFloatPoint tangent = TangentAt(
                        effectiveCurve, samples, pointIndex);
                    Normalize(tangent, out float tangentX, out float tangentY);
                    float nx = -tangentY;
                    float ny = tangentX;
                    float safeWidth = widths[pointIndex];
                    byte fallback = rawFallback ? (byte)1 : (byte)0;
                    bool riverTransitionTransparent = input.IsRiver &&
                        (IsRiverWater(center, pRaster) ||
                         pointIndex > 0 && IsRiverWater(samples[pointIndex - 1], pRaster) ||
                         pointIndex + 1 < samples.Count &&
                         IsRiverWater(samples[pointIndex + 1], pRaster));
                    fallbacks.Add(fallback);
                    AddCrossSection(
                        center, nx, ny, safeWidth, input, pRaster,
                        riverTransitionTransparent,
                        fallback != 0,
                        positionX, positionY, normalX, normalY,
                        signedDistance, tiers, leftOwners, rightOwners,
                        leftColors, rightColors, halfWidths, flags, alpha);
                }

                List<int> tierIndices = IndicesFor(
                    input.Tier, city, vassal, system);
                for (int pointIndex = 1;
                     pointIndex < samples.Count; pointIndex++)
                {
                    int previous = firstVertex + (pointIndex - 1) * 4;
                    int current = firstVertex + pointIndex * 4;
                    AddQuad(tierIndices, previous, previous + 1,
                        current, current + 1);
                    AddQuad(tierIndices, previous + 2, previous + 3,
                        current + 2, current + 3);
                    centerLine.Add(previous + 1);
                    centerLine.Add(current + 1);
                }
            }

            return new BoundaryMeshDraft(
                positionX.ToArray(), positionY.ToArray(),
                normalX.ToArray(), normalY.ToArray(),
                signedDistance.ToArray(), tiers.ToArray(),
                leftOwners.ToArray(), rightOwners.ToArray(),
                leftColors.ToArray(), rightColors.ToArray(),
                halfWidths.ToArray(), flags.ToArray(), alpha.ToArray(),
                fallbacks.ToArray(), city.ToArray(), vassal.ToArray(),
                system.ToArray(), centerLine.ToArray(), failureCount);
        }

        private static bool RibbonInputIsValid(BoundaryRibbonInput pInput)
        {
            if (pInput == null || pInput.Curve == null ||
                pInput.RawPoints == null ||
                pInput.Tier < BoundaryTier.City ||
                pInput.Tier > BoundaryTier.SuzerainSystem)
                return false;
            BoundaryCurveDraft curve = pInput.Curve;
            if (!curve.IsValid || curve.Closed || curve.Points == null ||
                curve.Points.Count < 2 ||
                curve.Points.Count > MaximumRibbonPointCount ||
                !ResampleCountWithinBudget(curve.Points) ||
                !IsFinite(curve.StartTangent) || !IsFinite(curve.EndTangent))
                return false;
            for (int i = 0; i < curve.Points.Count; i++)
                if (!IsFinite(curve.Points[i])) return false;
            if (pInput.RawPoints.Count < 2 ||
                pInput.RawPoints.Count > MaximumRibbonPointCount ||
                !ResampleCountWithinBudget(pInput.RawPoints) ||
                pInput.RawPoints[0].Equals(
                    pInput.RawPoints[pInput.RawPoints.Count - 1]))
                return false;
            bool hasDistinctRawPoint = false;
            BoundaryGridPoint first = pInput.RawPoints[0];
            for (int i = 1; i < pInput.RawPoints.Count; i++)
            {
                if (!pInput.RawPoints[i].Equals(first))
                {
                    hasDistinctRawPoint = true;
                    break;
                }
            }
            return hasDistinctRawPoint;
        }

        private static bool IsFinite(BoundaryFloatPoint pPoint)
        {
            return !float.IsNaN(pPoint.X) && !float.IsInfinity(pPoint.X) &&
                   !float.IsNaN(pPoint.Y) && !float.IsInfinity(pPoint.Y);
        }

        public static float ComputeSafeHalfWidth(
            BoundaryFloatPoint pCenter,
            BoundaryFloatPoint pNormal,
            float pTargetHalfWidth,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            Normalize(pNormal, out float nx, out float ny);
            return ComputeSafeHalfWidth(
                pCenter, nx, ny, pTargetHalfWidth, pRaster, pInput);
        }

        public static uint HierarchyColor(
            uint rootRgba,
            long rootSystemId,
            long displayedOwnerId,
            BoundaryTier tier,
            uint adjacentRgba)
        {
            HierarchyColorIdentity identity = CompatibilityColorIdentity(
                rootRgba, rootSystemId, displayedOwnerId, tier);
            uint candidate =
                HierarchicalVassalBoundaryColorRules.CandidateColor(identity, 0);
            if (adjacentRgba == 0 || candidate != adjacentRgba)
                return candidate;
            for (int candidateIndex = 1; candidateIndex < 32; candidateIndex++)
            {
                candidate = HierarchicalVassalBoundaryColorRules.CandidateColor(
                    identity, candidateIndex);
                if (candidate != adjacentRgba)
                    return candidate;
            }
            return candidate;
        }

        private static HierarchyColorIdentity CompatibilityColorIdentity(
            uint rootRgba,
            long rootSystemId,
            long displayedOwnerId,
            BoundaryTier tier)
        {
            return new HierarchyColorIdentity(
                tier, displayedOwnerId,
                rootSystemId, rootSystemId,
                tier == BoundaryTier.VassalRealm ? displayedOwnerId : -1,
                tier == BoundaryTier.City ? displayedOwnerId : -1,
                rootRgba);
        }

        private static float ComputeSafeHalfWidth(
            BoundaryFloatPoint pCenter,
            float pNormalX,
            float pNormalY,
            float pTargetHalfWidth,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            float[] scales = { 1f, 0.75f, 0.5f, 0.25f, 0.125f, 0f };
            for (int scaleIndex = 0; scaleIndex < scales.Length; scaleIndex++)
            {
                float width = pTargetHalfWidth * scales[scaleIndex];
                bool safe = true;
                const int samples = 8;
                for (int sample = -samples; sample <= samples; sample++)
                {
                    float distance = width * sample / samples;
                    var point = new BoundaryFloatPoint(
                        pCenter.X + pNormalX * distance,
                        pCenter.Y + pNormalY * distance);
                    if (!pInput.IsRiver &&
                        DistanceToRaw(point, pInput.RawPoints) >
                        MaximumCorridor + Epsilon)
                    {
                        safe = false;
                        break;
                    }
                    if (!IsFootprintPointAllowed(
                            point, distance, pRaster, pInput))
                    {
                        safe = false;
                        break;
                    }
                }
                if (safe)
                    return width;
            }
            return 0f;
        }

        private static float[] SafeWidths(
            IReadOnlyList<BoundaryFloatPoint> pSamples,
            BoundaryCurveDraft pCurve,
            float pTargetHalfWidth,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            var result = new float[pSamples.Count];
            for (int i = 0; i < pSamples.Count; i++)
            {
                BoundaryFloatPoint tangent = TangentAt(pCurve, pSamples, i);
                Normalize(tangent, out float tangentX, out float tangentY);
                result[i] = ComputeSafeHalfWidth(
                    pSamples[i], -tangentY, tangentX,
                    pTargetHalfWidth, pRaster, pInput);
            }
            return result;
        }

        private static void ConstrainSegmentFootprints(
            IReadOnlyList<BoundaryFloatPoint> pSamples,
            BoundaryCurveDraft pCurve,
            float[] pWidths,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            for (int segment = 1; segment < pSamples.Count; segment++)
            {
                int previous = segment - 1;
                bool lockPrevious = previous == 0;
                bool lockCurrent = segment == pSamples.Count - 1;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (SegmentFootprintIsSafe(
                            pSamples, pCurve, previous, segment,
                            pWidths[previous], pWidths[segment],
                            pRaster, pInput))
                        break;
                    if (!lockPrevious)
                        pWidths[previous] *= 0.5f;
                    if (!lockCurrent)
                        pWidths[segment] *= 0.5f;
                }
                if (!SegmentFootprintIsSafe(
                        pSamples, pCurve, previous, segment,
                        pWidths[previous], pWidths[segment],
                        pRaster, pInput))
                {
                    if (lockPrevious && lockCurrent)
                    {
                        pWidths[previous] = 0f;
                        pWidths[segment] = 0f;
                    }
                    else if (!lockPrevious)
                        pWidths[previous] = 0f;
                    else if (!lockCurrent)
                        pWidths[segment] = 0f;
                }
            }
        }

        private static bool SegmentFootprintIsSafe(
            IReadOnlyList<BoundaryFloatPoint> pSamples,
            BoundaryCurveDraft pCurve,
            int pPrevious,
            int pCurrent,
            float pPreviousWidth,
            float pCurrentWidth,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            var scratch = new TriangleClipScratch();
            BoundaryFloatPoint previousTangent =
                TangentAt(pCurve, pSamples, pPrevious);
            BoundaryFloatPoint currentTangent =
                TangentAt(pCurve, pSamples, pCurrent);
            Normalize(previousTangent, out float previousTx, out float previousTy);
            Normalize(currentTangent, out float currentTx, out float currentTy);
            var previousNormal = new BoundaryFloatPoint(-previousTy, previousTx);
            var currentNormal = new BoundaryFloatPoint(-currentTy, currentTx);
            BoundaryFloatPoint previousLeft = Offset(
                pSamples[pPrevious], previousNormal, pPreviousWidth);
            BoundaryFloatPoint currentLeft = Offset(
                pSamples[pCurrent], currentNormal, pCurrentWidth);
            BoundaryFloatPoint previousRight = Offset(
                pSamples[pPrevious], previousNormal, -pPreviousWidth);
            BoundaryFloatPoint currentRight = Offset(
                pSamples[pCurrent], currentNormal, -pCurrentWidth);
            return TriangleFootprintIsSafe(
                       previousLeft, currentLeft, pSamples[pCurrent],
                       pPreviousWidth, pCurrentWidth, 0f,
                       pRaster, pInput, scratch) &&
                   TriangleFootprintIsSafe(
                       previousLeft, pSamples[pCurrent], pSamples[pPrevious],
                       pPreviousWidth, 0f, 0f,
                       pRaster, pInput, scratch) &&
                   TriangleFootprintIsSafe(
                       pSamples[pPrevious], pSamples[pCurrent], currentRight,
                       0f, 0f, -pCurrentWidth,
                       pRaster, pInput, scratch) &&
                   TriangleFootprintIsSafe(
                       pSamples[pPrevious], currentRight, previousRight,
                       0f, -pCurrentWidth, -pPreviousWidth,
                       pRaster, pInput, scratch);
        }

        internal static bool TriangleFootprintIsSafeForTests(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            return TriangleFootprintIsSafe(
                pA, pB, pC, 0f, 0f, 0f,
                pRaster, pInput, new TriangleClipScratch());
        }

        private static bool TriangleFootprintIsSafe(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            float pDistanceA,
            float pDistanceB,
            float pDistanceC,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput,
            TriangleClipScratch pScratch)
        {
            if (!pInput.IsRiver &&
                (DistanceToRaw(pA, pInput.RawPoints) > MaximumCorridor + Epsilon ||
                 DistanceToRaw(pB, pInput.RawPoints) > MaximumCorridor + Epsilon ||
                 DistanceToRaw(pC, pInput.RawPoints) > MaximumCorridor + Epsilon))
                return false;
            float minimumX = Math.Min(pA.X, Math.Min(pB.X, pC.X));
            float maximumX = Math.Max(pA.X, Math.Max(pB.X, pC.X));
            float minimumY = Math.Min(pA.Y, Math.Min(pB.Y, pC.Y));
            float maximumY = Math.Max(pA.Y, Math.Max(pB.Y, pC.Y));
            double firstX = Math.Floor(minimumX);
            double lastX = Math.Floor(maximumX);
            double firstY = Math.Floor(minimumY);
            double lastY = Math.Floor(maximumY);
            if (firstX < int.MinValue || lastX > int.MaxValue ||
                firstY < int.MinValue || lastY > int.MaxValue)
                return false;
            long cellWidth = (long)(lastX - firstX) + 1L;
            long cellHeight = (long)(lastY - firstY) + 1L;
            if (cellWidth <= 0L || cellHeight <= 0L ||
                cellWidth > MaximumTriangleFootprintCells ||
                cellHeight > MaximumTriangleFootprintCells ||
                cellWidth * cellHeight > MaximumTriangleFootprintCells)
                return false;
            for (long y = (long)firstY; y <= (long)lastY; y++)
            for (long x = (long)firstX; x <= (long)lastX; x++)
            {
                if (!CellIsForbidden(
                        (int)x, (int)y,
                        pDistanceA, pDistanceB, pDistanceC,
                        pRaster, pInput))
                    continue;
                double area = TriangleCellIntersectionArea(
                    pA, pB, pC, (int)x, (int)y, pScratch);
                if (area > 0f ||
                    SegmentCrossesCellInterior(pA, pB, (int)x, (int)y) ||
                    SegmentCrossesCellInterior(pB, pC, (int)x, (int)y) ||
                    SegmentCrossesCellInterior(pC, pA, (int)x, (int)y))
                    return false;
            }
            return true;
        }

        private static bool CellIsForbidden(
            int pX,
            int pY,
            float pDistanceA,
            float pDistanceB,
            float pDistanceC,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            BoundaryCellFacts cell = pRaster.GetOrInvalid(pX, pY);
            if (!cell.IsValid)
            {
                bool outside = pX < pRaster.OriginX || pY < pRaster.OriginY ||
                    pX >= pRaster.MaxXExclusive ||
                    pY >= pRaster.MaxYExclusive;
                return !outside || !CoastSideAllows(
                    pDistanceA, pDistanceB, pDistanceC, pInput.CoastSide);
            }
            if (!cell.IsLand)
            {
                if (pInput.IsRiver &&
                    cell.Water == BoundaryWaterKind.InlandWater)
                    return false;
                return !CoastSideAllows(
                    pDistanceA, pDistanceB, pDistanceC, pInput.CoastSide);
            }
            long owner = OwnerId(cell, pInput.Tier);
            return owner != pInput.LeftOwnerId &&
                   owner != pInput.RightOwnerId;
        }

        private static bool CoastSideAllows(
            float pDistanceA,
            float pDistanceB,
            float pDistanceC,
            BoundaryRibbonCoastSide pCoastSide)
        {
            return pCoastSide == BoundaryRibbonCoastSide.Left &&
                   pDistanceA >= -Epsilon && pDistanceB >= -Epsilon &&
                   pDistanceC >= -Epsilon ||
                   pCoastSide == BoundaryRibbonCoastSide.Right &&
                   pDistanceA <= Epsilon && pDistanceB <= Epsilon &&
                   pDistanceC <= Epsilon;
        }

        private static double TriangleCellIntersectionArea(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            int pCellX,
            int pCellY,
            TriangleClipScratch pScratch)
        {
            BoundaryFloatPoint[] input = pScratch.First;
            BoundaryFloatPoint[] output = pScratch.Second;
            input[0] = pA; input[1] = pB; input[2] = pC;
            int count = 3;
            count = ClipCellAxis(input, count, output, 0, pCellX, true);
            Swap(ref input, ref output);
            count = ClipCellAxis(input, count, output, 0, pCellX + 1, false);
            Swap(ref input, ref output);
            count = ClipCellAxis(input, count, output, 1, pCellY, true);
            Swap(ref input, ref output);
            count = ClipCellAxis(input, count, output, 1, pCellY + 1, false);
            if (count < 3) return 0d;
            double twiceArea = 0d;
            for (int i = 0; i < count; i++)
            {
                BoundaryFloatPoint next = output[(i + 1) % count];
                twiceArea += (double)output[i].X * next.Y -
                             (double)output[i].Y * next.X;
            }
            return Math.Abs(twiceArea) * 0.5d;
        }

        private static int ClipCellAxis(
            BoundaryFloatPoint[] pInput,
            int pCount,
            BoundaryFloatPoint[] pOutput,
            int pAxis,
            float pBoundary,
            bool pKeepGreater)
        {
            if (pCount == 0) return 0;
            int outputCount = 0;
            BoundaryFloatPoint previous = pInput[pCount - 1];
            bool previousInside = CellClipInside(
                previous, pAxis, pBoundary, pKeepGreater);
            for (int i = 0; i < pCount; i++)
            {
                BoundaryFloatPoint current = pInput[i];
                bool currentInside = CellClipInside(
                    current, pAxis, pBoundary, pKeepGreater);
                if (currentInside != previousInside)
                    pOutput[outputCount++] = CellClipIntersection(
                        previous, current, pAxis, pBoundary);
                if (currentInside)
                    pOutput[outputCount++] = current;
                previous = current;
                previousInside = currentInside;
            }
            return outputCount;
        }

        private static bool CellClipInside(
            BoundaryFloatPoint pPoint,
            int pAxis,
            float pBoundary,
            bool pKeepGreater)
        {
            float value = pAxis == 0 ? pPoint.X : pPoint.Y;
            return pKeepGreater ? value >= pBoundary : value <= pBoundary;
        }

        private static BoundaryFloatPoint CellClipIntersection(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            int pAxis,
            float pBoundary)
        {
            double start = pAxis == 0 ? pStart.X : pStart.Y;
            double end = pAxis == 0 ? pEnd.X : pEnd.Y;
            double denominator = end - start;
            double ratio = denominator == 0d
                ? 0d : (pBoundary - start) / denominator;
            return new BoundaryFloatPoint(
                (float)(pStart.X + (pEnd.X - pStart.X) * ratio),
                (float)(pStart.Y + (pEnd.Y - pStart.Y) * ratio));
        }

        private static bool SegmentCrossesCellInterior(
            BoundaryFloatPoint pStart,
            BoundaryFloatPoint pEnd,
            int pCellX,
            int pCellY)
        {
            float minimum = 0f;
            float maximum = 1f;
            float dx = pEnd.X - pStart.X;
            float dy = pEnd.Y - pStart.Y;
            if (!ClipSegmentRange(-dx, pStart.X - pCellX, ref minimum, ref maximum) ||
                !ClipSegmentRange(dx, pCellX + 1 - pStart.X, ref minimum, ref maximum) ||
                !ClipSegmentRange(-dy, pStart.Y - pCellY, ref minimum, ref maximum) ||
                !ClipSegmentRange(dy, pCellY + 1 - pStart.Y, ref minimum, ref maximum) ||
                maximum - minimum <= Epsilon)
                return false;
            float ratio = (minimum + maximum) * 0.5f;
            float x = pStart.X + dx * ratio;
            float y = pStart.Y + dy * ratio;
            return x > pCellX && x < pCellX + 1 &&
                   y > pCellY && y < pCellY + 1;
        }

        private static bool ClipSegmentRange(
            float pDirection,
            float pDistance,
            ref float pMinimum,
            ref float pMaximum)
        {
            if (Math.Abs(pDirection) <= Epsilon)
                return pDistance >= 0f;
            float ratio = pDistance / pDirection;
            if (pDirection < 0f)
            {
                if (ratio > pMaximum) return false;
                if (ratio > pMinimum) pMinimum = ratio;
            }
            else
            {
                if (ratio < pMinimum) return false;
                if (ratio < pMaximum) pMaximum = ratio;
            }
            return true;
        }

        private static void Swap(
            ref BoundaryFloatPoint[] pFirst,
            ref BoundaryFloatPoint[] pSecond)
        {
            BoundaryFloatPoint[] temporary = pFirst;
            pFirst = pSecond;
            pSecond = temporary;
        }

        private sealed class TriangleClipScratch
        {
            public readonly BoundaryFloatPoint[] First =
                new BoundaryFloatPoint[12];
            public readonly BoundaryFloatPoint[] Second =
                new BoundaryFloatPoint[12];
        }

        private static BoundaryFloatPoint Offset(
            BoundaryFloatPoint pPoint,
            BoundaryFloatPoint pNormal,
            float pDistance)
        {
            return new BoundaryFloatPoint(
                pPoint.X + pNormal.X * pDistance,
                pPoint.Y + pNormal.Y * pDistance);
        }

        private static bool HasZeroWidth(IReadOnlyList<float> pWidths)
        {
            if (pWidths.Count == 0)
                return true;
            for (int i = 0; i < pWidths.Count; i++)
                if (pWidths[i] <= Epsilon) return true;
            return false;
        }

        private static IReadOnlyList<BoundaryFloatPoint> ToFloatPoints(
            IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            var result = new BoundaryFloatPoint[pPoints.Count];
            for (int i = 0; i < pPoints.Count; i++)
                result[i] = new BoundaryFloatPoint(pPoints[i].X, pPoints[i].Y);
            return result;
        }

        private static BoundaryCurveDraft BuildRawCurve(
            IReadOnlyList<BoundaryGridPoint> pRawPoints)
        {
            IReadOnlyList<BoundaryFloatPoint> points = ToFloatPoints(pRawPoints);
            BoundaryFloatPoint startTangent = RawTangent(points, fromStart: true);
            BoundaryFloatPoint endTangent = RawTangent(points, fromStart: false);
            return new BoundaryCurveDraft(
                points, false, true, 0f, startTangent, endTangent);
        }

        private static BoundaryFloatPoint RawTangent(
            IReadOnlyList<BoundaryFloatPoint> pPoints, bool fromStart)
        {
            int first = fromStart ? 0 : pPoints.Count - 1;
            int step = fromStart ? 1 : -1;
            for (int index = first + step;
                 index >= 0 && index < pPoints.Count; index += step)
            {
                BoundaryFloatPoint tangent = fromStart
                    ? new BoundaryFloatPoint(
                        pPoints[index].X - pPoints[first].X,
                        pPoints[index].Y - pPoints[first].Y)
                    : new BoundaryFloatPoint(
                        pPoints[first].X - pPoints[index].X,
                        pPoints[first].Y - pPoints[index].Y);
                if (Math.Abs(tangent.X) > Epsilon ||
                    Math.Abs(tangent.Y) > Epsilon)
                    return tangent;
            }
            return default(BoundaryFloatPoint);
        }

        private static bool IsFootprintPointAllowed(
            BoundaryFloatPoint pPoint,
            float pSignedDistance,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            if (pInput.IsRiver && IsRiverWater(pPoint, pRaster))
                return true;
            int floorX = (int)Math.Floor(pPoint.X);
            int floorY = (int)Math.Floor(pPoint.Y);
            bool onX = Math.Abs(pPoint.X - Math.Round(pPoint.X)) <= Epsilon;
            bool onY = Math.Abs(pPoint.Y - Math.Round(pPoint.Y)) <= Epsilon;
            int minimumX = onX ? floorX - 1 : floorX;
            int minimumY = onY ? floorY - 1 : floorY;
            bool foundAllowed = false;
            for (int x = minimumX; x <= floorX; x++)
            for (int y = minimumY; y <= floorY; y++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                if (!cell.IsValid)
                {
                    if (x < pRaster.OriginX || y < pRaster.OriginY ||
                        x >= pRaster.MaxXExclusive ||
                        y >= pRaster.MaxYExclusive)
                        continue;
                    return false;
                }
                if (!cell.IsLand)
                {
                    bool allowedCoastSide =
                        pInput.CoastSide == BoundaryRibbonCoastSide.Left &&
                        pSignedDistance >= -Epsilon ||
                        pInput.CoastSide == BoundaryRibbonCoastSide.Right &&
                        pSignedDistance <= Epsilon;
                    if (allowedCoastSide)
                        continue;
                    return false;
                }
                long owner = OwnerId(cell, pInput.Tier);
                if (owner != pInput.LeftOwnerId && owner != pInput.RightOwnerId)
                    return false;
                foundAllowed = true;
            }
            return foundAllowed || pInput.CoastSide != BoundaryRibbonCoastSide.None;
        }

        private static bool IsRiverWater(
            BoundaryFloatPoint pPoint,
            BoundaryCellRaster pRaster)
        {
            BoundaryCellFacts cell = pRaster.GetOrInvalid(
                (int)Math.Floor(pPoint.X), (int)Math.Floor(pPoint.Y));
            return cell.IsValid &&
                   cell.Water == BoundaryWaterKind.InlandWater;
        }

        private static float DistanceToRaw(
            BoundaryFloatPoint pPoint,
            IReadOnlyList<BoundaryGridPoint> pRaw)
        {
            float minimum = float.MaxValue;
            for (int i = 1; i < pRaw.Count; i++)
            {
                var start = new BoundaryFloatPoint(pRaw[i - 1].X, pRaw[i - 1].Y);
                var end = new BoundaryFloatPoint(pRaw[i].X, pRaw[i].Y);
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float lengthSquared = dx * dx + dy * dy;
                float ratio = lengthSquared <= Epsilon
                    ? 0f
                    : ((pPoint.X - start.X) * dx +
                       (pPoint.Y - start.Y) * dy) / lengthSquared;
                ratio = Math.Max(0f, Math.Min(1f, ratio));
                float sampleX = start.X + dx * ratio;
                float sampleY = start.Y + dy * ratio;
                float differenceX = pPoint.X - sampleX;
                float differenceY = pPoint.Y - sampleY;
                minimum = Math.Min(minimum,
                    (float)Math.Sqrt(differenceX * differenceX +
                                     differenceY * differenceY));
            }
            return minimum;
        }

        private static BoundaryFloatPoint TangentAt(
            BoundaryCurveDraft pCurve,
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            int pIndex)
        {
            if (pIndex == 0 && !IsZero(pCurve.StartTangent))
                return pCurve.StartTangent;
            if (pIndex == pPoints.Count - 1 &&
                !IsZero(pCurve.EndTangent))
                return pCurve.EndTangent;
            int previous = Math.Max(0, pIndex - 1);
            int next = Math.Min(pPoints.Count - 1, pIndex + 1);
            return new BoundaryFloatPoint(
                pPoints[next].X - pPoints[previous].X,
                pPoints[next].Y - pPoints[previous].Y);
        }

        private static IReadOnlyList<BoundaryFloatPoint> Resample(
            IReadOnlyList<BoundaryFloatPoint> pPoints,
            float pMaximumSpacing)
        {
            var result = new List<BoundaryFloatPoint> { pPoints[0] };
            for (int i = 1; i < pPoints.Count; i++)
            {
                BoundaryFloatPoint start = pPoints[i - 1];
                BoundaryFloatPoint end = pPoints[i];
                float dx = end.X - start.X;
                float dy = end.Y - start.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1,
                    (int)Math.Ceiling(length / pMaximumSpacing));
                for (int step = 1; step <= steps; step++)
                {
                    float ratio = (float)step / steps;
                    result.Add(new BoundaryFloatPoint(
                        start.X + dx * ratio,
                        start.Y + dy * ratio));
                }
            }
            return result;
        }

        private static bool ResampleCountWithinBudget(
            IReadOnlyList<BoundaryFloatPoint> pPoints)
        {
            long count = 1L;
            for (int i = 1; i < pPoints.Count; i++)
            {
                double dx = (double)pPoints[i].X - pPoints[i - 1].X;
                double dy = (double)pPoints[i].Y - pPoints[i - 1].Y;
                double steps = Math.Max(1d,
                    Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / 0.20d));
                if (steps > MaximumRibbonPointCount ||
                    count + steps > MaximumRibbonPointCount)
                    return false;
                count += (long)steps;
            }
            return true;
        }

        private static bool ResampleCountWithinBudget(
            IReadOnlyList<BoundaryGridPoint> pPoints)
        {
            long count = 1L;
            for (int i = 1; i < pPoints.Count; i++)
            {
                double dx = (double)pPoints[i].X - pPoints[i - 1].X;
                double dy = (double)pPoints[i].Y - pPoints[i - 1].Y;
                double steps = Math.Max(1d,
                    Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / 0.20d));
                if (steps > MaximumRibbonPointCount ||
                    count + steps > MaximumRibbonPointCount)
                    return false;
                count += (long)steps;
            }
            return true;
        }

        private static bool IsZero(BoundaryFloatPoint pPoint)
        {
            return Math.Abs(pPoint.X) <= Epsilon &&
                   Math.Abs(pPoint.Y) <= Epsilon;
        }

        private static void Normalize(
            BoundaryFloatPoint pPoint,
            out float pX,
            out float pY)
        {
            float length = (float)Math.Sqrt(
                pPoint.X * pPoint.X + pPoint.Y * pPoint.Y);
            if (length <= Epsilon)
            {
                pX = 1f;
                pY = 0f;
                return;
            }
            pX = pPoint.X / length;
            pY = pPoint.Y / length;
        }

        private static void AddCrossSection(
            BoundaryFloatPoint pCenter,
            float pNormalX,
            float pNormalY,
            float pHalfWidth,
            BoundaryRibbonInput pInput,
            BoundaryCellRaster pRaster,
            bool pRiverTransitionTransparent,
            bool pFallback,
            IList<float> pPositionX,
            IList<float> pPositionY,
            IList<float> pNormalXs,
            IList<float> pNormalYs,
            IList<float> pSignedDistance,
            IList<byte> pTiers,
            IList<long> pLeftOwners,
            IList<long> pRightOwners,
            IList<uint> pLeftColors,
            IList<uint> pRightColors,
            IList<float> pHalfWidths,
            IList<byte> pFlags,
            IList<byte> pAlpha)
        {
            float[] distances = { pHalfWidth, 0f, 0f, -pHalfWidth };
            for (int i = 0; i < distances.Length; i++)
            {
                float distance = distances[i];
                pPositionX.Add(pCenter.X + pNormalX * distance);
                pPositionY.Add(pCenter.Y + pNormalY * distance);
                pNormalXs.Add(pNormalX);
                pNormalYs.Add(pNormalY);
                pSignedDistance.Add(distance);
                pTiers.Add((byte)pInput.Tier);
                pLeftOwners.Add(pInput.LeftOwnerId);
                pRightOwners.Add(pInput.RightOwnerId);
                pLeftColors.Add(pInput.LeftRgba);
                pRightColors.Add(pInput.RightRgba);
                pHalfWidths.Add(pHalfWidth);
                bool leftHalf = i <= 1;
                var vertex = new BoundaryFloatPoint(
                    pCenter.X + pNormalX * distance,
                    pCenter.Y + pNormalY * distance);
                bool transparent = pInput.IsRiver &&
                        (pRiverTransitionTransparent ||
                         IsRiverWater(vertex, pRaster)) ||
                    pInput.CoastSide == BoundaryRibbonCoastSide.Left && leftHalf ||
                    pInput.CoastSide == BoundaryRibbonCoastSide.Right && !leftHalf;
                BoundaryRibbonFlags flags = BoundaryRibbonFlags.None;
                if (pInput.IsRiver) flags |= BoundaryRibbonFlags.River;
                if (pInput.CoastSide != BoundaryRibbonCoastSide.None)
                    flags |= BoundaryRibbonFlags.Coast;
                if (transparent) flags |= BoundaryRibbonFlags.Transparent;
                if (pFallback) flags |= BoundaryRibbonFlags.RawFallback;
                pFlags.Add((byte)flags);
                pAlpha.Add(transparent ? (byte)0 : (byte)255);
            }
        }

        private static void AddQuad(
            IList<int> pIndices,
            int pPreviousOuter,
            int pPreviousInner,
            int pCurrentOuter,
            int pCurrentInner)
        {
            pIndices.Add(pPreviousOuter);
            pIndices.Add(pCurrentOuter);
            pIndices.Add(pCurrentInner);
            pIndices.Add(pPreviousOuter);
            pIndices.Add(pCurrentInner);
            pIndices.Add(pPreviousInner);
        }

        private static List<int> IndicesFor(
            BoundaryTier pTier,
            List<int> pCity,
            List<int> pVassal,
            List<int> pSystem)
        {
            switch (pTier)
            {
                case BoundaryTier.City:
                    return pCity;
                case BoundaryTier.VassalRealm:
                    return pVassal;
                default:
                    return pSystem;
            }
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

        private static BoundaryCellFacts FindOwnerFacts(
            BoundaryCellRaster pRaster,
            long pOwnerId,
            BoundaryTier pTier)
        {
            for (int y = pRaster.OriginY; y < pRaster.MaxYExclusive; y++)
            for (int x = pRaster.OriginX; x < pRaster.MaxXExclusive; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                if (cell.IsLand && OwnerId(cell, pTier) == pOwnerId)
                    return cell;
            }
            return new BoundaryCellFacts(
                0, 0, false, BoundaryWaterKind.Land, 0,
                -1, -1, -1, 0);
        }

        private static BoundaryMeshDraft EmptyFailureDraft(int pFailureCount)
        {
            return new BoundaryMeshDraft(
                Array.Empty<float>(), Array.Empty<float>(),
                Array.Empty<float>(), Array.Empty<float>(),
                Array.Empty<float>(), Array.Empty<byte>(),
                Array.Empty<long>(), Array.Empty<long>(),
                Array.Empty<uint>(), Array.Empty<uint>(),
                Array.Empty<float>(), Array.Empty<byte>(),
                Array.Empty<byte>(), Array.Empty<byte>(),
                Array.Empty<int>(), Array.Empty<int>(),
                Array.Empty<int>(), Array.Empty<int>(), pFailureCount);
        }

        private static T[] Fill<T>(int pCount, T pValue)
        {
            var result = new T[pCount];
            for (int i = 0; i < pCount; i++) result[i] = pValue;
            return result;
        }

    }
}
