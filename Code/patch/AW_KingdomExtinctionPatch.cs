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
                // 零城已确认,但尸体/单位/建筑/弹道还指着本王国时不能抢跑:
                // Dispose 会把 asset 置 null,而尸体仍在 visible_units 里渲染。
                if (!KingdomExtinctionRules.ShouldForceImmediateRemoval(
                        __instance.isCiv(), cityIndexStable, liveCityCount,
                        VanillaLiveReferencesCleared(__instance)))
                    return true;
                __result = true;
                return false;
            }
            return true;
        }

        /// <summary>
        ///     读原版那几个活引用闸门。读不到就当作「未放开」,交回原版判定。
        /// </summary>
        private static bool VanillaLiveReferencesCleared(Kingdom pKingdom)
        {
            try
            {
                return KingdomExtinctionRules.
                    AreVanillaLiveReferencesCleared(
                        pKingdom._force_preserve_alive,
                        pKingdom.units.Count,
                        pKingdom.buildings.Count,
                        World.world.projectiles.hasActiveProjectiles(
                            pKingdom));
            }
            catch
            {
                return false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.removeObject))]
        internal static void RemoveKingdom_Prefix(Kingdom pKingdom)
        {
            AccessionIdentityService.OnKingdomRemoved(pKingdom);
        }
    }
}
