using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Treats partially disposed buildings as non-constructing objects. Such
    /// references can survive in city and zone lists for one cleanup cycle.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_BuildingNullSafetyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Building), nameof(Building.isUnderConstruction))]
        private static bool IsUnderConstructionPrefix(
            Building __instance,
            ref bool __result)
        {
            if (__instance != null && __instance.asset != null &&
                __instance.data != null)
                return true;

            __result = false;
            return false;
        }
    }
}
