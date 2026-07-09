using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityOccupationAccelerationPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static void UpdateCapture_Postfix(City __instance, float pElapsed)
        {
            CityOccupationAccelerationService.AfterUpdateCapture(__instance, pElapsed);
        }
    }
}
