using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(ZoneCalculator), "applyMetaColorsToZone")]
    internal static class AW_HierarchicalVassalMapWaterPatch
    {
        [HarmonyPostfix]
        private static void RestoreWaterColors(ZoneCalculator __instance,
            TileZone pZone)
        {
            if (__instance == null || pZone?.tiles == null ||
                !HierarchicalVassalMapModeRules.ShouldRestoreMixedZoneWater(
                    HierarchicalVassalMapModeService.NativeDrawPassActive,
                    pZone.tiles_with_ground, pZone.tiles.Length)) return;

            WorldTile[] tiles = pZone.tiles;
            for (int index = 0; index < tiles.Length; index++)
            {
                WorldTile tile = tiles[index];
                if (tile?.data == null ||
                    HierarchicalVassalMapModeService.IsVisibleLand(tile))
                    continue;
                try
                {
                    __instance.pixels[tile.data.tile_id] = Toolbox.clear;
                }
                catch { }
            }
        }
    }
}
