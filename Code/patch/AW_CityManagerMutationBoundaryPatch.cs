using System;
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityManagerMutationBoundaryPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
        private static void CityUpdatePrefix()
        {
            CityManagerMutationScope.EnterCityUpdate();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(CityManager), nameof(CityManager.update))]
        private static Exception CityUpdateFinalizer(Exception __exception)
        {
            CityManagerMutationScope.ExitCityUpdate();
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorldPrefix()
        {
            CityManagerMutationScope.Reset();
        }
    }
}
