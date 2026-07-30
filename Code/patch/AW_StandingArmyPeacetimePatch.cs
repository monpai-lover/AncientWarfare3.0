using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_StandingArmyPeacetimePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor),
            nameof(Actor.canCurrentTaskBeCancelledByReproduction))]
        public static void CanCurrentTaskBeCancelledByReproduction_Postfix(
            Actor __instance, ref bool __result)
        {
            if (__result) return;
            __result = StandingArmyPeacetimeService
                .CanYieldToReproduction(__instance);
        }
    }
}
