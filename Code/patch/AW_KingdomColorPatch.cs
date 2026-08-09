using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomColorPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.newCivKingdom))]
        private static void NewCivKingdom_Postfix(Kingdom __instance)
        {
            KingdomVisualRandomizationService.RerollNewCivVisuals(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.updateColor))]
        private static void UpdateColor_Postfix(Kingdom __instance,
            bool __result)
        {
            if (__result)
                MilitaryGovernorateColorService.OnSuzerainColorChanged(
                    __instance);
        }
    }
}
