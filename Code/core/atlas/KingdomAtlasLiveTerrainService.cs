using System;
using System.Collections.Generic;
using AncientWarfare3.core.presentation;

namespace AncientWarfare3.core.atlas
{
    internal static class KingdomAtlasLiveTerrainService
    {
        private static readonly KingdomAtlasColor Boundary =
            new KingdomAtlasColor(245, 245, 225);

        internal static ArmyRtsPlanWorldTerrainSnapshot Capture(int pResolution)
        {
            return ArmyRtsPlanWorldTerrainCapture.Capture(pResolution);
        }

        internal static void AttachNodeGeometry(KingdomAtlasNode pNode,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain)
        {
            if (pNode == null || pTerrain == null) return;
            pNode.TerrainWorldWidth = pTerrain.WorldWidth;
            pNode.TerrainWorldHeight = pTerrain.WorldHeight;
            HashSet<long> visible = VisibleOwners(pNode);
            var cells = new List<KingdomAtlasZoneCell>();
            var seen = new HashSet<KingdomAtlasZoneCell>();

            // Historical archive geometry remains useful for cities that no
            // longer exist in the live world.  It never determines ownership.
            IReadOnlyList<KingdomAtlasZoneCell> archived = pNode.VisibleZones;
            if (archived != null)
                for (int index = 0; index < archived.Count; index++)
                {
                    KingdomAtlasZoneCell cell = archived[index];
                    if (!TryGetVisibleHistoricalOwner(pNode, cell.CityId,
                            visible, out _)) continue;
                    if (seen.Add(cell)) cells.Add(cell);
                }

            for (int index = 0; index < pTerrain.CityIds.Count; index++)
            {
                long cityId = pTerrain.CityIds[index];
                if (!TryGetVisibleHistoricalOwner(pNode, cityId, visible,
                        out _)) continue;
                PointFor(pTerrain, index, out int x, out int y);
                var cell = new KingdomAtlasZoneCell(cityId, x, y,
                    pTerrain.Water[index], 0);
                if (seen.Add(cell)) cells.Add(cell);
            }
            pNode.VisibleZones = cells;
        }

        internal static KingdomAtlasRaster Render(KingdomAtlasNode pNode,
            int pResolution, ArmyRtsPlanWorldTerrainSnapshot pTerrain)
        {
            if (pNode?.Event == null)
                throw new ArgumentNullException(nameof(pNode));
            if (pTerrain == null)
                throw new InvalidOperationException(
                    "Live world terrain is unavailable.");

            int resolution = Math.Max(64, Math.Min(8192, pResolution));
            int count = checked(resolution * resolution);
            var terrain = new KingdomAtlasColor[count];
            var owners = new long[count];
            var targetWater = new bool[count];
            long[] historicalOwners =
                KingdomAtlasLiveTerrainRules.ProjectHistoricalOwners(
                    pTerrain.CityIds, pTerrain.Water, pNode.CityOwners);
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    int sourceX = KingdomAtlasLiveTerrainRules.
                        MapOutputXToCaptureX(x, resolution, pTerrain.Width);
                    int sourceY = KingdomAtlasLiveTerrainRules.
                        MapOutputYToCaptureY(y, resolution, pTerrain.Height);
                    int source = sourceY * pTerrain.Width + sourceX;
                    terrain[y * resolution + x] = ToAtlasColor(
                        pTerrain.Colors[source]);
                    owners[y * resolution + x] = historicalOwners[source];
                    targetWater[y * resolution + x] = pTerrain.Water[source];
                }

            OverlayArchivedGeometry(pNode, pTerrain, resolution, targetWater,
                owners);

            return new KingdomAtlasRaster(resolution, resolution,
                KingdomAtlasLiveTerrainRules.ComposeRgba(resolution,
                    resolution, terrain, owners, VisibleOwners(pNode),
                    ResolveMapColors(pNode.DisplayColors),
                    ResolveBoundaryColors(pNode.DisplayColors), Boundary));
        }

        private static IReadOnlyDictionary<long, KingdomAtlasColor>
            ResolveMapColors(IReadOnlyDictionary<long, KingdomAtlasColor> pColors)
        {
            var result = new Dictionary<long, KingdomAtlasColor>();
            if (pColors == null) return result;
            foreach (KeyValuePair<long, KingdomAtlasColor> pair in pColors)
            {
                KingdomAtlasColor color = pair.Value;
                try
                {
                    string hex = string.Format("#{0:X2}{1:X2}{2:X2}",
                        color.Red, color.Green, color.Blue);
                    ColorAsset asset = ColorAsset.getExistingColorAsset(hex);
                    if (asset != null)
                    {
                        UnityEngine.Color main = asset.getColorMain();
                        color = new KingdomAtlasColor(
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(main.r * 255f), 0, 255),
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(main.g * 255f), 0, 255),
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(main.b * 255f), 0, 255),
                            color.Alpha);
                    }
                }
                catch { }
                color = new KingdomAtlasColor(color.Red, color.Green,
                    color.Blue, 170);
                result[pair.Key] = color;
            }
            return result;
        }

        private static IReadOnlyDictionary<long, KingdomAtlasColor>
            ResolveBoundaryColors(IReadOnlyDictionary<long, KingdomAtlasColor> pColors)
        {
            var result = new Dictionary<long, KingdomAtlasColor>();
            if (pColors == null) return result;
            foreach (KeyValuePair<long, KingdomAtlasColor> pair in pColors)
            {
                KingdomAtlasColor color = pair.Value;
                try
                {
                    string hex = string.Format("#{0:X2}{1:X2}{2:X2}",
                        color.Red, color.Green, color.Blue);
                    ColorAsset asset = ColorAsset.getExistingColorAsset(hex);
                    if (asset != null)
                    {
                        UnityEngine.Color secondary = asset.getColorMainSecond();
                        color = new KingdomAtlasColor(
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(secondary.r * 255f), 0, 255),
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(secondary.g * 255f), 0, 255),
                            (byte)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.
                                RoundToInt(secondary.b * 255f), 0, 255), 255);
                    }
                }
                catch { }
                result[pair.Key] = color;
            }
            return result;
        }

        private static HashSet<long> VisibleOwners(KingdomAtlasNode pNode)
        {
            return KingdomAtlasRules.BuildVisibleOwnerIds(
                new[] { pNode?.Event?.OldKingdomId ?? -1L,
                        pNode?.Event?.NewKingdomId ?? -1L },
                pNode?.VassalRelations, pNode?.Event?.WorldTime ?? 0d,
                pNode?.KingdomId ?? -1L);
        }

        private static bool TryGetVisibleHistoricalOwner(KingdomAtlasNode pNode,
            long pCityId, ISet<long> pVisibleOwners, out long pOwnerId)
        {
            pOwnerId = -1L;
            if (pCityId < 0L || pNode?.CityOwners == null ||
                !pNode.CityOwners.TryGetValue(pCityId, out long ownerId) ||
                ownerId < 0L || pVisibleOwners == null ||
                !pVisibleOwners.Contains(ownerId)) return false;
            pOwnerId = ownerId;
            return true;
        }

        private static void OverlayArchivedGeometry(KingdomAtlasNode pNode,
            ArmyRtsPlanWorldTerrainSnapshot pTerrain, int pResolution,
            IReadOnlyList<bool> pTargetWater, long[] pOwners)
        {
            if (pNode?.VisibleZones == null || pTerrain == null ||
                pTargetWater == null ||
                pOwners == null || pOwners.Length != pResolution * pResolution)
                return;
            HashSet<long> visible = VisibleOwners(pNode);
            for (int index = 0; index < pNode.VisibleZones.Count; index++)
            {
                KingdomAtlasZoneCell cell = pNode.VisibleZones[index];
                if (cell.Water || !TryGetVisibleHistoricalOwner(pNode,
                        cell.CityId, visible, out long ownerId)) continue;
                int x = ProjectWorldAxis(cell.X, pTerrain.WorldWidth,
                    pResolution);
                int y = ProjectWorldAxis(cell.Y,
                    pTerrain.WorldHeight, pResolution);
                int destination = y * pResolution + x;
                if (destination < 0 || destination >= pTargetWater.Count ||
                    !KingdomAtlasLiveTerrainRules.ShouldOverlayArchivedOwner(
                        pTargetWater[destination], cell.Water, ownerId)) continue;
                if (pOwners[destination] < 0L) pOwners[destination] = ownerId;
            }
        }

        private static int SampleAxis(int pValue, int pExtent,
            int pSourceExtent)
        {
            if (pExtent <= 1 || pSourceExtent <= 1) return 0;
            return Math.Max(0, Math.Min(pSourceExtent - 1,
                (int)Math.Round(pValue * (pSourceExtent - 1d) /
                                (pExtent - 1d))));
        }

        private static void PointFor(
            ArmyRtsPlanWorldTerrainSnapshot pTerrain, int pIndex,
            out int pX, out int pY)
        {
            int row = pIndex / pTerrain.Width;
            int column = pIndex - row * pTerrain.Width;
            pX = SampleWorldAxis(column, pTerrain.Width, pTerrain.WorldWidth);
            pY = SampleWorldAxis(pTerrain.Height - 1 - row,
                pTerrain.Height, pTerrain.WorldHeight);
        }

        private static int SampleWorldAxis(int pValue, int pExtent,
            int pWorldExtent)
        {
            if (pExtent <= 1 || pWorldExtent <= 1) return 0;
            return Math.Max(0, Math.Min(pWorldExtent - 1,
                (int)Math.Round(pValue * (pWorldExtent - 1d) /
                                (pExtent - 1d))));
        }

        private static int ProjectWorldAxis(int pValue, int pWorldExtent,
            int pResolution)
        {
            if (pResolution <= 1 || pWorldExtent <= 1) return 0;
            int value = Math.Max(0, Math.Min(pWorldExtent - 1, pValue));
            return Math.Max(0, Math.Min(pResolution - 1,
                (int)Math.Round(value * (pResolution - 1d) /
                                (pWorldExtent - 1d))));
        }

        private static KingdomAtlasColor ToAtlasColor(
            ArmyRtsPlanColor pColor)
        {
            return KingdomAtlasLiveTerrainRules.NormalizeRasterAlpha(
                new KingdomAtlasColor(pColor.Red, pColor.Green,
                    pColor.Blue, pColor.Alpha));
        }
    }
}
