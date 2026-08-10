using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityReservePoolPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Prefix(City __instance,
            out Kingdom __state)
        {
            __state = __instance?.kingdom;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "setKingdom")]
        private static void CitySetKingdom_Postfix(City __instance,
            Kingdom pKingdom, bool pFromLoad, Kingdom __state)
        {
            if (pFromLoad || AW3MultiplayerReplicaScope.IsApplying) return;
            SyntheticMobilizationLedgerService.OnCityKingdomChanged(
                __instance, __state, __instance?.kingdom ?? pKingdom);
            CityReservePoolService.OnCityKingdomChanged(__instance, __state,
                __instance?.kingdom ?? pKingdom);
        }
    }
}
