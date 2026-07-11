using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityOccupationAccelerationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static void UpdateCapture_Prefix(City __instance, float pElapsed)
        {
            CityOccupationAccelerationService.BeforeUpdateCapture(__instance, pElapsed);
        }
    }
}
