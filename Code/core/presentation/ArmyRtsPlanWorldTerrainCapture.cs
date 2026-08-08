using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.presentation
{
    public sealed class ArmyRtsPlanWorldTerrainSnapshot
    {
        public ArmyRtsPlanWorldTerrainSnapshot(int pWidth, int pHeight,
            int pWorldWidth, int pWorldHeight,
            IReadOnlyList<ArmyRtsPlanColor> pColors,
            IReadOnlyList<long> pOwnerIds,
            IReadOnlyList<long> pCityIds,
            IReadOnlyList<bool> pWater)
        {
            Width = Math.Max(1, pWidth);
            Height = Math.Max(1, pHeight);
            WorldWidth = Math.Max(1, pWorldWidth);
            WorldHeight = Math.Max(1, pWorldHeight);
            int count = checked(Width * Height);
            if (pColors == null || pColors.Count != count)
                throw new ArgumentException("Terrain colors must match dimensions.",
                    nameof(pColors));
            if (pOwnerIds == null || pOwnerIds.Count != count)
                throw new ArgumentException("Terrain owners must match dimensions.",
                    nameof(pOwnerIds));
            if (pCityIds == null || pCityIds.Count != count)
                throw new ArgumentException("Terrain cities must match dimensions.",
                    nameof(pCityIds));
            if (pWater == null || pWater.Count != count)
                throw new ArgumentException("Terrain water flags must match dimensions.",
                    nameof(pWater));
            Colors = new List<ArmyRtsPlanColor>(pColors).ToArray();
            OwnerIds = new List<long>(pOwnerIds).ToArray();
            CityIds = new List<long>(pCityIds).ToArray();
            Water = new List<bool>(pWater).ToArray();
        }

        public int Width { get; }
        public int Height { get; }
        public int WorldWidth { get; }
        public int WorldHeight { get; }
        public IReadOnlyList<ArmyRtsPlanColor> Colors { get; }
        public IReadOnlyList<long> OwnerIds { get; }
        public IReadOnlyList<long> CityIds { get; }
        public IReadOnlyList<bool> Water { get; }
    }

    public static class ArmyRtsPlanWorldTerrainCapture
    {
        public static ArmyRtsPlanWorldTerrainSnapshot Capture(
            int pMaximumLongEdge = ArmyRtsPlanRules.DefaultMaximumLongEdge)
        {
            if (World.world == null || World.world.tiles_list == null)
                throw new InvalidOperationException(
                    "Live world terrain is unavailable.");

            int worldWidth = Math.Max(1, MapBox.width);
            int worldHeight = Math.Max(1, MapBox.height);
            ArmyRtsPlanCanvas canvas = ArmyRtsPlanRules.Project(worldWidth,
                worldHeight, pMaximumLongEdge);
            int count = checked(canvas.Width * canvas.Height);
            var colors = new ArmyRtsPlanColor[count];
            var owners = new long[count];
            var cities = new long[count];
            var water = new bool[count];
            WorldTile[] tiles = World.world.tiles_list;
            for (int y = 0; y < canvas.Height; y++)
            {
                int worldY = UnprojectAxis(canvas.Height - 1 - y,
                    canvas.Height, worldHeight);
                for (int x = 0; x < canvas.Width; x++)
                {
                    int index = y * canvas.Width + x;
                    int worldX = UnprojectAxis(x, canvas.Width, worldWidth);
                    int tileIndex = worldX + worldY * worldWidth;
                    WorldTile tile = tileIndex >= 0 && tileIndex < tiles.Length
                        ? tiles[tileIndex]
                        : null;
                    colors[index] = TileColor(tile);
                    City city = tile?.zone?.city;
                    Kingdom kingdom = city?.kingdom;
                    owners[index] = kingdom?.data?.id ?? -1L;
                    cities[index] = city?.data?.id ?? -1L;
                    water[index] = IsWater(tile);
                }
            }
            return new ArmyRtsPlanWorldTerrainSnapshot(canvas.Width,
                canvas.Height, worldWidth, worldHeight, colors, owners,
                cities, water);
        }

        public static ArmyRtsPlanTerrain BuildPlanTerrain(
            ArmyRtsPlanWorldTerrainSnapshot pSnapshot,
            ISet<long> pParticipantIds,
            IReadOnlyDictionary<long, ArmyRtsPlanColor> pKingdomColors)
        {
            if (pSnapshot == null)
                throw new ArgumentNullException(nameof(pSnapshot));
            return ArmyRtsPlanTerrainBuilder.Build(pSnapshot.Width,
                pSnapshot.Height, pSnapshot.Colors, pSnapshot.OwnerIds,
                pParticipantIds, pKingdomColors);
        }

        private static int UnprojectAxis(int pCanvasValue,
            int pCanvasExtent, int pWorldExtent)
        {
            if (pCanvasExtent <= 1 || pWorldExtent <= 1) return 0;
            return Math.Max(0, Math.Min(pWorldExtent - 1,
                (int)Math.Round(pCanvasValue * (pWorldExtent - 1d) /
                                (pCanvasExtent - 1d))));
        }

        private static ArmyRtsPlanColor TileColor(WorldTile pTile)
        {
            try
            {
                if (pTile == null) return ArmyRtsPlanRasterizer.LandColor;
                Color32 color = pTile.getColor();
                return new ArmyRtsPlanColor(color.r, color.g, color.b,
                    color.a);
            }
            catch
            {
                return ArmyRtsPlanRasterizer.LandColor;
            }
        }

        private static bool IsWater(WorldTile pTile)
        {
            return pTile == null || pTile.Type == null ||
                pTile.Type.liquid || pTile.Type.ocean || pTile.Type.lava ||
                !pTile.Type.ground;
        }
    }
}
