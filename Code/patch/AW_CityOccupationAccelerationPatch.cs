using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityOccupationAccelerationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static bool UpdateCapture_Prefix(City __instance, float pElapsed)
        {
            if (CityOccupationAccelerationService.TryCompleteAfterDefenderDefeat(__instance)) return false;
            CityOccupationAccelerationService.BeforeUpdateCapture(__instance, pElapsed);
            return true;
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), nameof(City.addCapturePoints),
            new[] { typeof(BaseSimObject), typeof(int) })]
        public static void AddCapturePointsObject_Postfix(City __instance, BaseSimObject pObject)
        {
            CityOccupationAccelerationService.RecordActiveMilitaryPresence(__instance, pObject);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "clearCurrentCaptureAmounts")]
        public static void ClearCurrentCaptureAmounts_Postfix(City __instance)
        {
            CityOccupationAccelerationService.ClearActiveMilitaryPresence(__instance);
        }
    }
}
