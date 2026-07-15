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

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), nameof(City.addCapturePoints),
            new[] { typeof(Kingdom), typeof(int) })]
        public static bool AddCapturePoints_Prefix(City __instance, Kingdom pKingdom, int pValue)
        {
            bool contributorIsCityOwner = __instance?.kingdom?.data != null &&
                                          pKingdom == __instance.kingdom;
            bool hasActiveDefenders = contributorIsCityOwner &&
                                      CityOccupationAccelerationService.HasActiveDefenders(__instance);
            return CityOccupationAccelerationRules.ShouldApplyCapturePointContribution(
                contributorIsCityOwner, pValue, hasActiveDefenders);
        }
    }
}
