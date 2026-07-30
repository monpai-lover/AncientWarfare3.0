using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityReservePoolPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "eventBecomeAdult")]
        private static void EventBecomeAdult_Postfix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityReservePoolService.OnActorBecameAdult(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static void JoinCity_Prefix(Actor __instance,
            out City __state)
        {
            __state = __instance?.city;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), nameof(Actor.joinCity))]
        private static void JoinCity_Postfix(Actor __instance, City __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            CityReservePoolService.OnActorCityChanged(__instance, __state);
        }

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
            CityReservePoolService.OnCityKingdomChanged(__instance, __state,
                __instance?.kingdom ?? pKingdom);
        }
    }
}
