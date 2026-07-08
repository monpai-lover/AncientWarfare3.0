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
            MetaColorCacheService.RefreshKingdomAfterGeneratedColor(__instance);
        }
    }
}
