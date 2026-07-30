using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.schools;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_HistoricalSchoolTaxiPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "disembarkTo",
            new[] { typeof(Boat), typeof(WorldTile) })]
        private static void DisembarkTo_Postfix(Actor __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            HistoricalSchoolEducationJourneyService.TryResumeAfterDisembark(__instance);
            HistoricalSchoolTravelService.TryResumeAfterDisembark(__instance);
        }
    }
}
