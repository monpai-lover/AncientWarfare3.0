using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class HierarchicalVassalBoundaryHeightRules
    {
        public const int MaximumInteriorSize = 32;
        public const int MaximumHalo = 2;
        public const int MaximumSampleCount = 36 * 36;

        public const float MinimumLightFactor = 0.65f;
        public const float MaximumLightFactor = 1.15f;
        public const float NeutralLightFactor = 1f;
        public const float DiffuseStrength = 0.35f;
        public const float RidgeStrength = 0.08f;

        public static BoundaryHeightDraft Pack(
            BoundaryCellRaster pRaster,
            int interiorSize,
            int halo,
            long terrainRevision = 0L)
        {
            if (pRaster == null)
                throw new ArgumentNullException(nameof(pRaster));
            if (interiorSize <= 0 || interiorSize > MaximumInteriorSize)
                throw new ArgumentOutOfRangeException(nameof(interiorSize));
            if (halo < 0 || halo > MaximumHalo)
                throw new ArgumentOutOfRangeException(nameof(halo));
            if (terrainRevision < 0L)
                throw new ArgumentOutOfRangeException(nameof(terrainRevision));

            int dimension = checked(interiorSize + halo * 2);
            int sampleCount = checked(dimension * dimension);
            if (dimension > BoundaryHeightDraft.MaximumDimension ||
                sampleCount > MaximumSampleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(interiorSize));
            }
            if (pRaster.Width != dimension || pRaster.Height != dimension)
            {
                throw new ArgumentException(
                    "Raster dimensions must equal interior size plus both halos.",
                    nameof(pRaster));
            }

            var samples = new byte[sampleCount];
            var validSamples = new List<ValidHeightSample>(sampleCount);
            for (int y = 0; y < dimension; y++)
            {
                int worldY = pRaster.OriginY + y;
                for (int x = 0; x < dimension; x++)
                {
                    BoundaryCellFacts cell = pRaster.GetOrInvalid(
                        pRaster.OriginX + x, worldY);
                    if (!cell.IsValid)
                        continue;
                    samples[y * dimension + x] = cell.Height;
                    validSamples.Add(new ValidHeightSample(x, y, cell.Height));
                }
            }
            if (validSamples.Count == 0)
            {
                throw new ArgumentException(
                    "Height packing requires at least one valid world sample.",
                    nameof(pRaster));
            }

            for (int y = 0; y < dimension; y++)
            for (int x = 0; x < dimension; x++)
            {
                BoundaryCellFacts cell = pRaster.GetOrInvalid(
                    pRaster.OriginX + x, pRaster.OriginY + y);
                if (cell.IsValid)
                    continue;
                samples[y * dimension + x] = NearestValidHeight(
                    x, y, validSamples);
            }

            return new BoundaryHeightDraft(
                samples, dimension, dimension,
                pRaster.OriginX, pRaster.OriginY,
                halo, terrainRevision);
        }

        public static BoundaryFloat3 NormalAt(
            BoundaryHeightDraft pDraft,
            int pX,
            int pY,
            float pSlopeScale)
        {
            ValidateSampleCoordinate(pDraft, pX, pY);
            ValidateFiniteNonNegative(pSlopeScale, nameof(pSlopeScale));

            float left = pDraft.SampleAtUnchecked(pX - 1, pY);
            float right = pDraft.SampleAtUnchecked(pX + 1, pY);
            float down = pDraft.SampleAtUnchecked(pX, pY - 1);
            float up = pDraft.SampleAtUnchecked(pX, pY + 1);
            float dx = (right - left) / 255f * pSlopeScale;
            float dy = (up - down) / 255f * pSlopeScale;
            return Normalize(new BoundaryFloat3(-dx, -dy, 1f));
        }

        public static float LightAt(
            BoundaryHeightDraft pDraft,
            int pX,
            int pY,
            BoundaryFloat3 pLightDirection,
            float pSlopeScale)
        {
            ValidateFinite(pLightDirection.X, nameof(pLightDirection));
            ValidateFinite(pLightDirection.Y, nameof(pLightDirection));
            ValidateFinite(pLightDirection.Z, nameof(pLightDirection));
            BoundaryFloat3 light = Normalize(pLightDirection);
            BoundaryFloat3 normal = NormalAt(
                pDraft, pX, pY, pSlopeScale);

            float flatResponse = light.Z;
            float diffuseDelta = Dot(normal, light) - flatResponse;
            float ridge = (1f - normal.Z) * RidgeStrength;
            float factor = NeutralLightFactor +
                           diffuseDelta * DiffuseStrength + ridge;
            return Clamp(factor, MinimumLightFactor, MaximumLightFactor);
        }

        private static byte NearestValidHeight(
            int pX,
            int pY,
            IReadOnlyList<ValidHeightSample> pValidSamples)
        {
            long nearestDistance = long.MaxValue;
            byte nearestHeight = 0;
            for (int i = 0; i < pValidSamples.Count; i++)
            {
                ValidHeightSample candidate = pValidSamples[i];
                long dx = candidate.X - pX;
                long dy = candidate.Y - pY;
                long distance = dx * dx + dy * dy;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearestHeight = candidate.Height;
            }
            return nearestHeight;
        }

        private static void ValidateSampleCoordinate(
            BoundaryHeightDraft pDraft,
            int pX,
            int pY)
        {
            if (pDraft == null)
                throw new ArgumentNullException(nameof(pDraft));
            if (pX <= 0 || pX >= pDraft.Width - 1)
                throw new ArgumentOutOfRangeException(nameof(pX));
            if (pY <= 0 || pY >= pDraft.Height - 1)
                throw new ArgumentOutOfRangeException(nameof(pY));
        }

        private static void ValidateFiniteNonNegative(
            float pValue, string pParameterName)
        {
            ValidateFinite(pValue, pParameterName);
            if (pValue < 0f)
                throw new ArgumentOutOfRangeException(pParameterName);
        }

        private static void ValidateFinite(float pValue, string pParameterName)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue))
                throw new ArgumentOutOfRangeException(pParameterName);
        }

        private static BoundaryFloat3 Normalize(BoundaryFloat3 pValue)
        {
            double x = pValue.X;
            double y = pValue.Y;
            double z = pValue.Z;
            double maximum = Math.Max(
                Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (maximum <= 0d || double.IsNaN(maximum) ||
                double.IsInfinity(maximum))
                throw new ArgumentOutOfRangeException(nameof(pValue));

            double scaledX = x / maximum;
            double scaledY = y / maximum;
            double scaledZ = z / maximum;
            double scaledLength = Math.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ);
            if (scaledLength <= 0d || double.IsNaN(scaledLength) ||
                double.IsInfinity(scaledLength))
            {
                throw new ArgumentOutOfRangeException(nameof(pValue));
            }
            return new BoundaryFloat3(
                ToFiniteFloat(scaledX / scaledLength, nameof(pValue)),
                ToFiniteFloat(scaledY / scaledLength, nameof(pValue)),
                ToFiniteFloat(scaledZ / scaledLength, nameof(pValue)));
        }

        private static float Dot(BoundaryFloat3 pLeft, BoundaryFloat3 pRight)
        {
            return pLeft.X * pRight.X + pLeft.Y * pRight.Y +
                   pLeft.Z * pRight.Z;
        }

        private static float Clamp(float pValue, float pMinimum, float pMaximum)
        {
            if (float.IsNaN(pValue) || float.IsInfinity(pValue))
                pValue = NeutralLightFactor;
            if (pValue < pMinimum)
                return pMinimum;
            return pValue > pMaximum ? pMaximum : pValue;
        }

        private static float ToFiniteFloat(
            double pValue, string pParameterName)
        {
            if (double.IsNaN(pValue) || double.IsInfinity(pValue) ||
                pValue < -float.MaxValue || pValue > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(pParameterName);
            }
            float result = (float)pValue;
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw new ArgumentOutOfRangeException(pParameterName);
            return result;
        }

        private readonly struct ValidHeightSample
        {
            public ValidHeightSample(int pX, int pY, byte pHeight)
            {
                X = pX;
                Y = pY;
                Height = pHeight;
            }

            public int X { get; }
            public int Y { get; }
            public byte Height { get; }
        }
    }
}
