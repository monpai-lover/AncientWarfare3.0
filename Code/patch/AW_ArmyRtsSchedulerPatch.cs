using System;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ArmyRtsSchedulerPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ArmyManager), nameof(ArmyManager.update))]
        private static void ProcessArmyRtsAfterNativeUpdate()
        {
            try
            {
                ArmyRtsSchedulingService.ProcessNativeArmyUpdate();
            }
            catch (Exception error)
            {
                AWFramePriorityGovernor.MarkFault(error);
                Config.paused = true;
                ModClass.LogWarning(
                    "AW native Army RTS scheduling failed; game paused: " +
                    error);
            }
        }
    }
}
