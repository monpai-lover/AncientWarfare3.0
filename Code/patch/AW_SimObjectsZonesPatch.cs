using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SimObjectsZonesPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static bool CheckUnits_Prefix()
        {
            return !AWParallelSimObjectZoneUnits.TrySkipRedundantCheckUnits();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void CheckUnits_Postfix()
        {
            if (AWPerformanceSettings.EnableFramePriorityScheduler)
                AWParallelSimObjectZoneUnits.NotifyUnitMembershipRebuilt();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "fullClear")]
        private static void FullClear_Prefix()
        {
            AWParallelSimObjectZoneUnits.Invalidate();
        }
    }
}
