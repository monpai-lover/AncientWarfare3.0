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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Docks), nameof(Docks.Dispose))]
        private static void Dispose_Prefix(Docks __instance)
        {
            AWDockTransportService.Remove(__instance);
        }
    }
}
