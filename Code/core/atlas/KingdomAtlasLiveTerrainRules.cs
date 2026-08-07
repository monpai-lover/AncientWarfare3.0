using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasLiveTerrainRules
    {
        internal static long[] ProjectHistoricalOwners(
            IReadOnlyList<long> pCityIds, IReadOnlyList<bool> pWater,
            IReadOnlyDictionary<long, long> pHistoricalOwners)
        {
            if (pCityIds == null)
                throw new ArgumentNullException(nameof(pCityIds));
            if (pWater == null || pWater.Count != pCityIds.Count)
                throw new ArgumentException(
                    "Terrain water flags must match city dimensions.",
                    nameof(pWater));

            var result = new long[pCityIds.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = -1L;
                if (pWater[index]) continue;
                long cityId = pCityIds[index];
                if (cityId < 0L || pHistoricalOwners == null ||
                    !pHistoricalOwners.TryGetValue(cityId, out long ownerId) ||
                    ownerId < 0L) continue;
                result[index] = ownerId;
            }
            return result;
        }

        internal static int MapOutputYToCaptureY(int pOutputY,
            int pOutputHeight, int pCaptureHeight)
        {
            if (pOutputHeight <= 1 || pCaptureHeight <= 1) return 0;
            int output = Math.Max(0, Math.Min(pOutputHeight - 1,
                pOutputY));
            int sampled = (int)Math.Round(output * (pCaptureHeight - 1d) /
                (pOutputHeight - 1d));
            return Math.Max(0, Math.Min(pCaptureHeight - 1,
                pCaptureHeight - 1 - sampled));
        }

        internal static int MapOutputXToCaptureX(int pOutputX,
            int pOutputWidth, int pCaptureWidth)
        {
            if (pOutputWidth <= 1 || pCaptureWidth <= 1) return 0;
            int output = Math.Max(0, Math.Min(pOutputWidth - 1, pOutputX));
            int sampled = (int)Math.Round(output * (pCaptureWidth - 1d) /
                (pOutputWidth - 1d));
            return Math.Max(0, Math.Min(pCaptureWidth - 1, sampled));
        }

        internal static bool ShouldOverlayArchivedOwner(bool pTargetWater,
            bool pArchivedWater, long pOwnerId)
        {
            return pOwnerId >= 0L && !pTargetWater && !pArchivedWater;
        }

        internal static KingdomAtlasColor ResolveOwnerColor(
            KingdomAtlasColor pTerrain, long pOwnerId,
            ISet<long> pVisibleOwners,
            IReadOnlyDictionary<long, KingdomAtlasColor> pHistoricalColors)
        {
            if (pOwnerId >= 0L && pVisibleOwners != null &&
                pVisibleOwners.Contains(pOwnerId) &&
                pHistoricalColors != null &&
                pHistoricalColors.TryGetValue(pOwnerId,
                    out KingdomAtlasColor color)) return color;
            return pTerrain;
        }

        internal static KingdomAtlasColor NormalizeRasterAlpha(
            KingdomAtlasColor pColor)
        {
            return new KingdomAtlasColor(pColor.Red, pColor.Green,
                pColor.Blue, 255);
        }

        internal static byte[] ComposeRgba(int pWidth, int pHeight,
            IReadOnlyList<KingdomAtlasColor> pTerrain,
            IReadOnlyList<long> pOwnerIds, ISet<long> pVisibleOwners,
            IReadOnlyDictionary<long, KingdomAtlasColor> pHistoricalColors,
            KingdomAtlasColor pBoundaryColor)
        {
            return ComposeRgba(pWidth, pHeight, pTerrain, pOwnerIds,
                pVisibleOwners, pHistoricalColors, null, pBoundaryColor);
        }

        internal static byte[] ComposeRgba(int pWidth, int pHeight,
            IReadOnlyList<KingdomAtlasColor> pTerrain,
            IReadOnlyList<long> pOwnerIds, ISet<long> pVisibleOwners,
            IReadOnlyDictionary<long, KingdomAtlasColor> pHistoricalColors,
            IReadOnlyDictionary<long, KingdomAtlasColor> pBoundaryColors,
            KingdomAtlasColor pBoundaryColor)
        {
            int width = Math.Max(1, pWidth);
            int height = Math.Max(1, pHeight);
            int count = checked(width * height);
            if (pTerrain == null || pTerrain.Count != count)
                throw new ArgumentException("Terrain colors must match dimensions.",
                    nameof(pTerrain));
            if (pOwnerIds == null || pOwnerIds.Count != count)
                throw new ArgumentException("Terrain owners must match dimensions.",
                    nameof(pOwnerIds));

            var rgba = new byte[checked(count * 4)];
            for (int index = 0; index < count; index++)
            {
                KingdomAtlasColor color = pTerrain[index];
                long owner = pOwnerIds[index];
                color = CompositeOwnerColor(color, ResolveOwnerColor(color,
                    owner, pVisibleOwners, pHistoricalColors));
                Write(rgba, index, color);
            }

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (x + 1 < width && ShouldDrawBoundary(
                            pOwnerIds[index], pOwnerIds[index + 1],
                            pVisibleOwners))
                    {
                        WriteBoundaryPair(rgba, index, index + 1,
                            pOwnerIds[index], pOwnerIds[index + 1],
                            pVisibleOwners, pBoundaryColors, pBoundaryColor);
                    }
                    if (y + 1 < height && ShouldDrawBoundary(
                            pOwnerIds[index], pOwnerIds[index + width],
                            pVisibleOwners))
                    {
                        WriteBoundaryPair(rgba, index, index + width,
                            pOwnerIds[index], pOwnerIds[index + width],
                            pVisibleOwners, pBoundaryColors, pBoundaryColor);
                    }
                }
            return rgba;
        }

        internal static KingdomAtlasColor CompositeOwnerColor(
            KingdomAtlasColor pTerrain, KingdomAtlasColor pOverlay)
        {
            int alpha = pOverlay.Alpha;
            if (alpha <= 0) return pTerrain;
            if (alpha >= 255) return pOverlay;
            int inverse = 255 - alpha;
            return new KingdomAtlasColor(
                BlendChannel(pTerrain.Red, pOverlay.Red, inverse, alpha),
                BlendChannel(pTerrain.Green, pOverlay.Green, inverse, alpha),
                BlendChannel(pTerrain.Blue, pOverlay.Blue, inverse, alpha),
                pTerrain.Alpha);
        }

        internal static bool ShouldDrawBoundary(long pFirstOwner,
            long pSecondOwner, ISet<long> pVisibleOwners)
        {
            if (pFirstOwner == pSecondOwner || pVisibleOwners == null)
                return false;
            return (pFirstOwner >= 0L && pVisibleOwners.Contains(pFirstOwner)) ||
                   (pSecondOwner >= 0L && pVisibleOwners.Contains(pSecondOwner));
        }

        private static void Write(byte[] pRgba, int pIndex,
            KingdomAtlasColor pColor)
        {
            int offset = pIndex * 4;
            pRgba[offset] = pColor.Red;
            pRgba[offset + 1] = pColor.Green;
            pRgba[offset + 2] = pColor.Blue;
            pRgba[offset + 3] = pColor.Alpha;
        }

        private static void WriteBoundaryPair(byte[] pRgba, int pFirstIndex,
            int pSecondIndex, long pFirstOwner, long pSecondOwner,
            ISet<long> pVisibleOwners,
            IReadOnlyDictionary<long, KingdomAtlasColor> pBoundaryColors,
            KingdomAtlasColor pFallback)
        {
            KingdomAtlasColor first = BoundaryColorFor(pFirstOwner,
                pSecondOwner, pVisibleOwners, pBoundaryColors, pFallback);
            KingdomAtlasColor second = BoundaryColorFor(pSecondOwner,
                pFirstOwner, pVisibleOwners, pBoundaryColors, pFallback);
            Write(pRgba, pFirstIndex, first);
            Write(pRgba, pSecondIndex, second);
        }

        private static KingdomAtlasColor BoundaryColorFor(long pOwner,
            long pOtherOwner, ISet<long> pVisibleOwners,
            IReadOnlyDictionary<long, KingdomAtlasColor> pBoundaryColors,
            KingdomAtlasColor pFallback)
        {
            if (pOwner >= 0L && pVisibleOwners != null &&
                pVisibleOwners.Contains(pOwner) && pBoundaryColors != null &&
                pBoundaryColors.TryGetValue(pOwner,
                    out KingdomAtlasColor own)) return own;
            if (pOtherOwner >= 0L && pVisibleOwners != null &&
                pVisibleOwners.Contains(pOtherOwner) &&
                pBoundaryColors != null && pBoundaryColors.TryGetValue(
                    pOtherOwner, out KingdomAtlasColor other)) return other;
            return pFallback;
        }

        private static byte BlendChannel(byte pTerrain, byte pOverlay,
            int pInverse, int pAlpha)
        {
            return (byte)((pTerrain * pInverse + pOverlay * pAlpha + 127) /
                255);
        }
    }
}
