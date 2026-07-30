using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_OccupiedCityCivilianProtectionPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseSimObject), "canAttackTarget")]
        private static void CanAttackTarget_Postfix(
            BaseSimObject __instance, BaseSimObject pTarget,
            ref bool __result)
        {
            if (__result && OccupiedCityCivilianProtectionService.
                    ShouldSuppressHostility(__instance, pTarget))
                __result = false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "getHit")]
        private static bool GetHit_Prefix(Actor __instance,
            BaseSimObject pAttacker)
        {
            return !OccupiedCityCivilianProtectionService.
                ShouldSuppressDamage(__instance, pAttacker);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Building), "getHit")]
        private static bool BuildingGetHit_Prefix(Building __instance,
            BaseSimObject pAttacker)
        {
            return !OccupiedCityCivilianProtectionService.
                ShouldSuppressDamage(__instance, pAttacker);
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        [HarmonyPatch(typeof(Actor), "updateNutritionDecay")]
        private static bool UpdateNutritionDecay_Prefix(Actor __instance)
        {
            return !OccupiedCityCivilianProtectionService.
                TryRestoreEmergencyResident(__instance);
        }

    }
}
