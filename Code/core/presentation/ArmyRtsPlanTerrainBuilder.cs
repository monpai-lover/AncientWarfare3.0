using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.presentation
{
    public static class ArmyRtsPlanTerrainBuilder
    {
        public static ArmyRtsPlanTerrain Build(int pWidth, int pHeight,
            IReadOnlyList<ArmyRtsPlanColor> pColors,
            IReadOnlyList<long> pOwnerIds,
            ISet<long> pParticipantIds,
            IReadOnlyDictionary<long, ArmyRtsPlanColor> pKingdomColors,
            bool pDrawNonParticipantBoundaries = true)
        {
            int width = Math.Max(1, pWidth);
            int height = Math.Max(1, pHeight);
            int count = checked(width * height);
            if (pColors == null || pColors.Count != count)
                throw new ArgumentException(
                    "Terrain colors must match dimensions.",
                    nameof(pColors));
            if (pOwnerIds == null || pOwnerIds.Count != count)
                throw new ArgumentException(
                    "Terrain owners must match dimensions.",
                    nameof(pOwnerIds));
            var pixels = new byte[count];
            for (int i = 0; i < count; i++)
            {
                ArmyRtsPlanColor color = pColors[i];
                long ownerId = pOwnerIds[i];
                if (ownerId >= 0L && pKingdomColors != null &&
                    pKingdomColors.TryGetValue(ownerId,
                        out ArmyRtsPlanColor kingdomColor))
                    color = Tint(color, kingdomColor,
                        pParticipantIds?.Contains(ownerId) == true);
                pixels[i] = ArmyRtsPlanPalette.IndexOf(color);
            }

            byte participantBoundary = ArmyRtsPlanPalette.IndexOf(
                ArmyRtsPlanRasterizer.ParticipantBorderColor);
            byte otherBoundary = ArmyRtsPlanPalette.IndexOf(
                ArmyRtsPlanRasterizer.MarkerColor);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (x + 1 < width && pOwnerIds[index] !=
                        pOwnerIds[index + 1] &&
                        (pDrawNonParticipantBoundaries ||
                         IsParticipantBoundary(pOwnerIds[index],
                             pOwnerIds[index + 1], pParticipantIds)))
                        MarkBoundary(pixels, index, index + 1,
                            pOwnerIds[index], pOwnerIds[index + 1],
                            pParticipantIds, participantBoundary,
                            otherBoundary);
                    if (y + 1 < height && pOwnerIds[index] !=
                        pOwnerIds[index + width] &&
                        (pDrawNonParticipantBoundaries ||
                         IsParticipantBoundary(pOwnerIds[index],
                             pOwnerIds[index + width], pParticipantIds)))
                        MarkBoundary(pixels, index, index + width,
                            pOwnerIds[index], pOwnerIds[index + width],
                            pParticipantIds, participantBoundary,
                            otherBoundary);
                }
            }
            return new ArmyRtsPlanTerrain(width, height, pixels);
        }

        private static bool IsParticipantBoundary(long pFirstOwner,
            long pSecondOwner, ISet<long> pParticipantIds)
        {
            return pParticipantIds != null &&
                ((pFirstOwner >= 0L && pParticipantIds.Contains(pFirstOwner)) ||
                 (pSecondOwner >= 0L && pParticipantIds.Contains(pSecondOwner)));
        }

        private static void MarkBoundary(byte[] pPixels, int pFirst,
            int pSecond, long pFirstOwner, long pSecondOwner,
            ISet<long> pParticipantIds, byte pParticipantColor,
            byte pOtherColor)
        {
            bool participant = pParticipantIds != null &&
                (pParticipantIds.Contains(pFirstOwner) ||
                 pParticipantIds.Contains(pSecondOwner));
            byte color = participant ? pParticipantColor : pOtherColor;
            pPixels[pFirst] = color;
            pPixels[pSecond] = color;
        }

        private static ArmyRtsPlanColor Tint(ArmyRtsPlanColor pTerrain,
            ArmyRtsPlanColor pKingdom, bool pParticipant)
        {
            int terrainWeight = pParticipant ? 3 : 7;
            int divisor = terrainWeight + 1;
            return new ArmyRtsPlanColor(
                (byte)((pTerrain.Red * terrainWeight + pKingdom.Red) /
                       divisor),
                (byte)((pTerrain.Green * terrainWeight + pKingdom.Green) /
                       divisor),
                (byte)((pTerrain.Blue * terrainWeight + pKingdom.Blue) /
                       divisor));
        }
    }
}
