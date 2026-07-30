using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
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
        private static void UpdateColor_Postfix(Kingdom __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance == null || __instance.isRekt()) return;
            KingdomArchiveWriter.Upsert(__instance);
        }
    }
}
