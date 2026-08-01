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
            Curve = curve ?? throw new ArgumentNullException(nameof(curve));
            RawPoints = rawPoints ?? throw new ArgumentNullException(nameof(rawPoints));
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

        public static BoundaryMeshDraft BuildFill(
            BoundaryCellRaster pRaster)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            var bounds = new BoundaryChunkBounds(
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive,
                pRaster.OriginX, pRaster.OriginY,
                pRaster.MaxXExclusive, pRaster.MaxYExclusive);
            return BuildFill(pRaster, BoundaryDisplayLayer.Countries, bounds);
        }

        public static BoundaryMeshDraft BuildFill(
            BoundaryCellRaster pRaster,
            BoundaryDisplayLayer pLayer,
            BoundaryChunkBounds pBounds)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
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
            Dictionary<long, uint> ownerColors = BuildHierarchyColors(
                pRaster, fillTier);
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
                if (!input.Curve.IsValid || input.Curve.Points.Count < 2 ||
                    input.Tier == BoundaryTier.None)
                    continue;
                IReadOnlyList<BoundaryFloatPoint> samples =
                    Resample(input.Curve.Points, 0.20f);
                bool rawFallback = false;
                float targetHalfWidth = TargetWidth(input.Tier) * 0.5f;
                float[] widths = SafeWidths(
                    samples, input.Curve, targetHalfWidth, pRaster, input);
                ConstrainSegmentFootprints(
                    samples, input.Curve, widths, pRaster, input);
                if (HasZeroWidth(widths))
                {
                    samples = Resample(ToFloatPoints(input.RawPoints), 0.20f);
                    widths = SafeWidths(
                        samples, input.Curve, targetHalfWidth, pRaster, input);
                    ConstrainSegmentFootprints(
                        samples, input.Curve, widths, pRaster, input);
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
                        input.Curve, samples, pointIndex);
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
            uint candidate = CanonicalHierarchyColor(
                rootRgba, rootSystemId, displayedOwnerId, tier);
            if (adjacentRgba == 0 || candidate != adjacentRgba)
                return candidate;
            for (int candidateIndex = 1; candidateIndex < 32; candidateIndex++)
            {
                candidate = HierarchyColorCandidate(
                    rootRgba, rootSystemId, displayedOwnerId,
                    tier, candidateIndex);
                if (candidate != adjacentRgba)
                    return candidate;
            }
            return candidate;
        }

        private static uint CanonicalHierarchyColor(
            uint rootRgba,
            long rootSystemId,
            long displayedOwnerId,
            BoundaryTier tier)
        {
            if (tier == BoundaryTier.SuzerainSystem &&
                displayedOwnerId == rootSystemId)
                return rootRgba;
            return HierarchyColorCandidate(
                rootRgba, rootSystemId, displayedOwnerId, tier, 0);
        }

        private static uint HierarchyColorCandidate(
            uint rootRgba,
            long rootSystemId,
            long displayedOwnerId,
            BoundaryTier tier,
            int candidateIndex)
        {
            uint stableHash = StableHash(
                rootSystemId, displayedOwnerId, tier);
            uint hash = candidateIndex == 0
                ? stableHash
                : CandidateHash(stableHash, candidateIndex);
            int red = (int)(rootRgba >> 24) & 255;
            int green = (int)(rootRgba >> 16) & 255;
            int blue = (int)(rootRgba >> 8) & 255;
            int alpha = (int)rootRgba & 255;
            RgbToHsv(red, green, blue,
                out float hue, out float saturation, out float value);
            hue = WrapHue(hue +
                ((int)(hash % 41u) - 20) * 0.6f);
            saturation = Clamp01(
                saturation +
                ((int)((hash / 41u) % 41u) - 20) * 0.0075f);
            value = Clamp01(
                value +
                ((int)((hash / 1681u) % 41u) - 20) * 0.0075f);
            return HsvToRgba(hue, saturation, value, alpha);
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
                       pPreviousWidth, pCurrentWidth, 0f, pRaster, pInput) &&
                   TriangleFootprintIsSafe(
                       previousLeft, pSamples[pCurrent], pSamples[pPrevious],
                       pPreviousWidth, 0f, 0f, pRaster, pInput) &&
                   TriangleFootprintIsSafe(
                       pSamples[pPrevious], pSamples[pCurrent], currentRight,
                       0f, 0f, -pCurrentWidth, pRaster, pInput) &&
                   TriangleFootprintIsSafe(
                       pSamples[pPrevious], currentRight, previousRight,
                       0f, -pCurrentWidth, -pPreviousWidth, pRaster, pInput);
        }

        private static bool TriangleFootprintIsSafe(
            BoundaryFloatPoint pA,
            BoundaryFloatPoint pB,
            BoundaryFloatPoint pC,
            float pDistanceA,
            float pDistanceB,
            float pDistanceC,
            BoundaryCellRaster pRaster,
            BoundaryRibbonInput pInput)
        {
            const int divisions = 4;
            for (int row = 0; row <= divisions; row++)
            for (int column = 0; column <= divisions - row; column++)
            {
                float wa = (float)row / divisions;
                float wb = (float)column / divisions;
                float wc = 1f - wa - wb;
                var point = new BoundaryFloatPoint(
                    pA.X * wa + pB.X * wb + pC.X * wc,
                    pA.Y * wa + pB.Y * wb + pC.Y * wc);
                float distance = pDistanceA * wa +
                                 pDistanceB * wb + pDistanceC * wc;
                if (!pInput.IsRiver &&
                    DistanceToRaw(point, pInput.RawPoints) >
                    MaximumCorridor + Epsilon)
                    return false;
                if (!IsFootprintPointAllowed(
                        point, distance, pRaster, pInput))
                    return false;
            }
            return true;
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

        private static Dictionary<long, uint> BuildHierarchyColors(
            BoundaryCellRaster pRaster,
            BoundaryTier pTier)
        {
            var owners = new SortedSet<long>();
            for (int y = pRaster.OriginY; y < pRaster.MaxYExclusive; y++)
            for (int x = pRaster.OriginX; x < pRaster.MaxXExclusive; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                long owner = OwnerId(cell, pTier);
                if (cell.IsLand && owner >= 0)
                    owners.Add(owner);
            }
            Dictionary<long, HashSet<long>> adjacency = BuildAdjacency(
                pRaster, pTier);
            var colors = new Dictionary<long, uint>();
            foreach (long owner in owners)
            {
                BoundaryCellFacts facts = FindOwnerFacts(
                    pRaster, owner, pTier);
                uint candidate = CanonicalHierarchyColor(
                    facts.Rgba, facts.SystemId, owner, pTier);
                for (int candidateIndex = 0;
                     candidateIndex < 32; candidateIndex++)
                {
                    if (candidateIndex > 0)
                    {
                        candidate = HierarchyColorCandidate(
                            facts.Rgba, facts.SystemId, owner,
                            pTier, candidateIndex);
                    }
                    if (!MatchesAssignedNeighbor(
                            owner, candidate, adjacency, colors))
                        break;
                }
                colors.Add(owner, candidate);
            }
            return colors;
        }

        private static bool MatchesAssignedNeighbor(
            long pOwner,
            uint pCandidate,
            IReadOnlyDictionary<long, HashSet<long>> pAdjacency,
            IReadOnlyDictionary<long, uint> pColors)
        {
            if (!pAdjacency.TryGetValue(
                    pOwner, out HashSet<long> neighbors))
                return false;
            foreach (long neighbor in neighbors)
            {
                if (pColors.TryGetValue(neighbor, out uint color) &&
                    color == pCandidate)
                    return true;
            }
            return false;
        }

        private static Dictionary<long, HashSet<long>> BuildAdjacency(
            BoundaryCellRaster pRaster,
            BoundaryTier pTier)
        {
            var result = new Dictionary<long, HashSet<long>>();
            for (int y = pRaster.OriginY;
                 y < pRaster.MaxYExclusive; y++)
            for (int x = pRaster.OriginX;
                 x < pRaster.MaxXExclusive; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(x, y);
                if (!cell.IsLand)
                    continue;
                long owner = OwnerId(cell, pTier);
                AddAdjacentOwner(result, owner,
                    OwnerId(pRaster.GetOrInvalid(x - 1, y), pTier));
                AddAdjacentOwner(result, owner,
                    OwnerId(pRaster.GetOrInvalid(x + 1, y), pTier));
                AddAdjacentOwner(result, owner,
                    OwnerId(pRaster.GetOrInvalid(x, y - 1), pTier));
                AddAdjacentOwner(result, owner,
                    OwnerId(pRaster.GetOrInvalid(x, y + 1), pTier));
            }
            return result;
        }

        private static void AddAdjacentOwner(
            IDictionary<long, HashSet<long>> pAdjacency,
            long pFirst,
            long pSecond)
        {
            if (pFirst < 0 || pSecond < 0 || pFirst == pSecond)
                return;
            AddNeighbor(pAdjacency, pFirst, pSecond);
            AddNeighbor(pAdjacency, pSecond, pFirst);
        }

        private static void AddNeighbor(
            IDictionary<long, HashSet<long>> pAdjacency,
            long pOwner,
            long pNeighbor)
        {
            if (!pAdjacency.TryGetValue(pOwner, out HashSet<long> neighbors))
            {
                neighbors = new HashSet<long>();
                pAdjacency.Add(pOwner, neighbors);
            }
            neighbors.Add(pNeighbor);
        }

        private static bool HasAdjacentColorCollision(
            IReadOnlyDictionary<long, HashSet<long>> pAdjacency,
            IDictionary<long, uint> pKnownColors,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier)
        {
            foreach (KeyValuePair<long, HashSet<long>> pair in pAdjacency)
            {
                uint color = ColorForOwner(
                    pair.Key, pKnownColors, pRaster, pTier);
                foreach (long neighbor in pair.Value)
                {
                    if (pair.Key >= neighbor)
                        continue;
                    uint neighborColor = ColorForOwner(
                        neighbor, pKnownColors, pRaster, pTier);
                    if (color == neighborColor)
                        return true;
                }
            }
            return false;
        }

        private static uint ColorForOwner(
            long pOwnerId,
            IDictionary<long, uint> pKnownColors,
            BoundaryCellRaster pRaster,
            BoundaryTier pTier)
        {
            if (pKnownColors.TryGetValue(pOwnerId, out uint color))
                return color;
            BoundaryCellFacts facts = FindOwnerFacts(
                pRaster, pOwnerId, pTier);
            color = CanonicalHierarchyColor(
                facts.Rgba, facts.SystemId, pOwnerId, pTier);
            pKnownColors[pOwnerId] = color;
            return color;
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

        private static uint StableHash(
            long pRootSystemId,
            long pDisplayedOwnerId,
            BoundaryTier pTier)
        {
            unchecked
            {
                ulong value = (ulong)pRootSystemId * 11400714819323198485UL;
                value ^= (ulong)pDisplayedOwnerId + 0x9E3779B97F4A7C15UL;
                value ^= (ulong)pTier * 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                return (uint)(value ^ (value >> 32));
            }
        }

        private static uint CandidateHash(uint pStableHash, int pCandidateIndex)
        {
            unchecked
            {
                uint value = pStableHash +
                    (uint)pCandidateIndex * 0x9E3779B9u;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }

        private static void RgbToHsv(
            int pRed, int pGreen, int pBlue,
            out float pHue, out float pSaturation, out float pValue)
        {
            float red = pRed / 255f;
            float green = pGreen / 255f;
            float blue = pBlue / 255f;
            float maximum = Math.Max(red, Math.Max(green, blue));
            float minimum = Math.Min(red, Math.Min(green, blue));
            float delta = maximum - minimum;
            pValue = maximum;
            pSaturation = maximum <= 0f ? 0f : delta / maximum;
            if (delta <= 0f)
            {
                pHue = 0f;
                return;
            }
            if (maximum == red)
                pHue = 60f * (((green - blue) / delta) % 6f);
            else if (maximum == green)
                pHue = 60f * (((blue - red) / delta) + 2f);
            else
                pHue = 60f * (((red - green) / delta) + 4f);
            pHue = WrapHue(pHue);
        }

        private static uint HsvToRgba(
            float pHue, float pSaturation, float pValue, int pAlpha)
        {
            float chroma = pValue * pSaturation;
            float section = pHue / 60f;
            float secondary = chroma * (1f - Math.Abs(section % 2f - 1f));
            float red = 0f;
            float green = 0f;
            float blue = 0f;
            if (section < 1f) { red = chroma; green = secondary; }
            else if (section < 2f) { red = secondary; green = chroma; }
            else if (section < 3f) { green = chroma; blue = secondary; }
            else if (section < 4f) { green = secondary; blue = chroma; }
            else if (section < 5f) { red = secondary; blue = chroma; }
            else { red = chroma; blue = secondary; }
            float match = pValue - chroma;
            return Pack(
                ClampColor((int)Math.Round((red + match) * 255f)),
                ClampColor((int)Math.Round((green + match) * 255f)),
                ClampColor((int)Math.Round((blue + match) * 255f)),
                pAlpha);
        }

        private static float WrapHue(float pHue)
        {
            pHue %= 360f;
            return pHue < 0f ? pHue + 360f : pHue;
        }

        private static float Clamp01(float pValue)
        {
            if (pValue < 0f) return 0f;
            return pValue > 1f ? 1f : pValue;
        }

        private static int ClampColor(int pValue)
        {
            return Math.Max(24, Math.Min(231, pValue));
        }

        private static uint Pack(int pRed, int pGreen, int pBlue, int pAlpha)
        {
            return (uint)(pRed << 24 | pGreen << 16 | pBlue << 8 | pAlpha);
        }
    }
}
