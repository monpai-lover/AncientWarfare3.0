using HarmonyLib;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_SimObjectsZonesPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static bool CheckUnits_Prefix(out bool __state)
        {
            __state = AWParallelSimObjectZoneUnits.TrySkipRedundantCheckUnits();
            return !__state;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SimObjectsZones), "checkUnits")]
        private static void CheckUnits_Postfix(bool __state)
        {
            if (!__state && AWPerformanceSettings.EnableFramePriorityScheduler)
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
