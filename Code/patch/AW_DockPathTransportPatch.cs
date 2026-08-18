using AncientWarfare3.core.pathfinding;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DockPathTransportPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Docks), "create")]
        private static void Create_Postfix(Docks __instance)
        {
            AWDockTransportService.Register(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Docks), "recalculateOceanTiles")]
        private static void RecalculateOceanTiles_Postfix(Docks __instance)
        {
            AWDockTransportService.Register(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Docks), nameof(Docks.Dispose))]
        private static void Dispose_Prefix(Docks __instance)
        {
            AWDockTransportService.Remove(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapChunkManager), nameof(MapChunkManager.updateDirty))]
        private static void UpdateDirty_Prefix(MapChunkManager __instance,
            ref bool __state)
        {
            __state = false;
            try
            {
                var dirtyLinks = __instance?._dirty_chunks_links;
                var dirtyRegions = __instance?._dirty_chunks_regions;
                __state = (dirtyLinks?.Count ?? 0) > 0 ||
                          (dirtyRegions?.Count ?? 0) > 0;
            }
            catch { }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapChunkManager), nameof(MapChunkManager.updateDirty))]
        private static void UpdateDirty_Postfix(bool __state)
        {
            if (__state) AWDockTransportService.MarkTopologyDirty();
        }
    }
}
