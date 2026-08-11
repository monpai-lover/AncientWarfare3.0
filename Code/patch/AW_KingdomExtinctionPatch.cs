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
            bool cityIndexStable = !manager.hasDirtyCities();
            int liveCityCount;
            try { liveCityCount = __instance.countCities(); }
            catch { liveCityCount = __instance.hasCities() ? 1 : 0; }
            if (!cityIndexStable)
            {
                if (KingdomExtinctionRules.ShouldQueueVerification(
                        __instance.isCiv(), cityIndexStable, liveCityCount))
                    KingdomExtinctionQueue.Schedule(__instance);
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
                SuccessionDisputeService.OnZeroCityKingdom(__instance);
                if (manager.hasDirtyCities())
                {
                    KingdomExtinctionQueue.Schedule(__instance);
                    return true;
                }
                try { liveCityCount = __instance.countCities(); }
                catch { liveCityCount = __instance.hasCities() ? 1 : 0; }
                if (liveCityCount > 0) return true;
                __result = true;
                return false;
            }
            return true;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        internal static void RemoveKingdom_Prefix(Kingdom pKingdom)
        {
            AccessionIdentityService.OnKingdomRemoved(pKingdom);
        }
    }
}
