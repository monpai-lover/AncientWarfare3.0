using AncientWarfare3.core.court;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CourtVacancyEventPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "eventBecomeAdult")]
        private static void EventBecomeAdult_Postfix(Actor __instance)
        {
            if (__instance?.data == null || SmoothLoader.isLoading() ||
                !Config.game_loaded) return;
            Kingdom kingdom = __instance.kingdom;
            if (kingdom?.data == null || kingdom.isRekt()) return;
            OfficerCandidateCatalog.Invalidate(kingdom);
            CourtVacancyReconciliationService.CandidatePoolChanged(kingdom);
        }
    }
}
