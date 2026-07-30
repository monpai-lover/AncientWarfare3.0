using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_BuildingTweenSafetyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BuildingTweenExtension),
            nameof(BuildingTweenExtension.updateScale))]
        private static bool UpdateScale_Prefix(Building pBuilding)
        {
            // A removed or half-created building can remain in the parallel
            // scale queue for one update. The original method dereferences it.
            if (pBuilding == null || pBuilding.asset == null ||
                pBuilding.batch == null || World.world == null)
                return false;
            return true;
        }
    }
}
