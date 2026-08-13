using AncientWarfare3.core.grandstrategy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_GrandStrategyArmyWorldLifecyclePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorldPrefix()
        {
            GrandStrategyRuntimeHost.ClearWorld();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.generateNewMap))]
        private static void GenerateNewWorldPostfix()
        {
            GrandStrategyRuntimeHost.Initialize();
        }
    }
}
