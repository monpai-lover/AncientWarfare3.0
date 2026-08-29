using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
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
            // 补进当年名单即可,不能 Invalidate —— 那会把整张表丢掉,
            // 逼下一次补缺重走 getUnits() 加一次全国排序。
            OfficerCandidateCatalog.EnsurePresent(kingdom, __instance);
            // 进不了候选池的人成年,唤醒也只是把名单白扫一遍。
            if (!CourtCandidateWakeRules.ShouldWakeForNewAdult(
                    __instance.isSexMale(),
                    SlaveService.IsSlave(__instance),
                    __instance.isKing(),
                    HeirService.PeekRegisteredHeir(kingdom) == __instance))
                return;
            CourtVacancyReconciliationService.CandidatePoolChanged(kingdom);
        }
    }
}
