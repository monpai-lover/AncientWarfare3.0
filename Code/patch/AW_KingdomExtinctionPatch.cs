using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomExtinctionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.isReadyForRemoval))]
        internal static void IsReadyForRemoval_Prefix(Kingdom __instance)
        {
            KingdomManager manager = World.world?.kingdoms;
            if (__instance == null || manager == null) return;

            bool cityIndexStable = !manager.hasDirtyCities();
            bool hasCities = !cityIndexStable || __instance.hasCities();
            if (KingdomExtinctionRules.ShouldDisbandSurvivors(
                    __instance.isCiv(), cityIndexStable, hasCities))
            {
                RoyalAsylumService.NaturalizeBeforeExtinction(__instance);
                FormerHeirService.ArchiveAndClear(__instance);
                __instance.makeSurvivorsToNomads();
            }
        }
    }
}
