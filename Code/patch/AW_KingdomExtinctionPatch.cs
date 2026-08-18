using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomExtinctionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.isReadyForRemoval))]
        internal static bool IsReadyForRemoval_Prefix(Kingdom __instance,
            ref bool __result)
        {
            KingdomManager manager = World.world?.kingdoms;
            if (__instance == null || manager == null) return true;
            int liveCityCount;
            try { liveCityCount = __instance.countCities(); }
            catch { liveCityCount = __instance.hasCities() ? 1 : 0; }
            bool cityIndexStable = !manager.hasDirtyCities();
            if (KingdomExtinctionQueue.IsVerifiedForVanillaRemoval(
                    __instance,
                    liveCityCount))
            {
                return true;
            }
            if (KingdomExtinctionRules.ShouldQueueVerification(
                    __instance.isCiv(), cityIndexStable, liveCityCount))
                KingdomExtinctionQueue.Schedule(__instance);
            if (liveCityCount > 0 &&
                SuccessionDisputeService.ShouldPreserveOriginalKingdom(
                    __instance))
            {
                __result = false;
                return false;
            }

            if (KingdomExtinctionRules.ShouldForceImmediateRemoval(
                    __instance.isCiv(), cityIndexStable, liveCityCount))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
