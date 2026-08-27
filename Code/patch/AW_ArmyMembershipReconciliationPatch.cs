using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    // Keep army ownership reconciliation attached to the vanilla dirty rebuild
    // without replacing the vanilla dirty-index implementation.
    [HarmonyPatch]
    internal static class AW_ArmyMembershipReconciliationPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ArmyManager), "updateDirtyUnits")]
        private static void UpdateDirtyUnits_Postfix(ArmyManager __instance)
        {
            ArmyMembershipReconciliationService.EnqueueAll(__instance);
        }
    }
}
