using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_HeirPatch
    {
        public struct KingBranchContext
        {
            public Actor PreviousKing;
            public bool  WasRegisteredHeir;
            public int   PreNobleDistance;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromRoyalClan))]
        public static bool GetKingFromRoyalClan_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!LineageService.IsXiaKingdom(pKingdom)) return true;
            __result = HeirService.GetHeir(pKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromLeaders))]
        public static bool GetKingFromLeaders_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!LineageService.IsXiaKingdom(pKingdom)) return true;
            Actor heir = HeirService.GetHeir(pKingdom);
            Actor leaderCandidate = heir == null ? HeirService.GetLeaderSuccessionCandidate(pKingdom) : null;
            __result = HeirRecallRules.ShouldPreferRegisteredHeirBeforeLeaderFallback(heir != null)
                ? heir
                : (HeirRecallRules.ShouldUseLeaderFallbackForXiaizedSuccession(
                    pHasRegisteredHeir: false,
                    pHasLeaderCandidate: leaderCandidate != null)
                    ? leaderCandidate
                    : null);
            if (heir == null) HeirService.MarkLeaderFallbackSuccession(pKingdom, __result);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_CaptureBranchContext_Prefix(Kingdom __instance, Actor pActor, bool pFromLoad,
            out KingBranchContext __state)
        {
            __state = default;
            if (pFromLoad || __instance?.data == null || pActor?.data == null) return;
            __state.PreviousKing = __instance.king;
            HeirService.RememberPreSuccessionKing(__instance, __state.PreviousKing);

            // 成王前捕获"非嫡系代际距离"(AW_PromotionPatch 高优先 Postfix 会先把它归零,这里 Prefix 抢先读)。
            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int preNobleDistance, 0);
            __state.PreNobleDistance = preNobleDistance;

            __instance.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            pActor.data.get(LineageKeys.IS_HEIR, out bool heirFlag, false);
            __state.WasRegisteredHeir = heirFlag || heirId == pActor.data.id;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad, KingBranchContext __state)
        {
            if (pFromLoad) return;
            if (__instance?.data == null) return;
            if (!LineageService.IsXiaKingdom(__instance)) return;

            Actor king = pActor ?? __instance.king;
            if (pActor != null && __instance.king != pActor) return;
            if (king != null)
            {
                LineageService.OnKingFoundBranch(__instance, king, __state.PreviousKing, __state.WasRegisteredHeir,
                    __state.PreNobleDistance);
                HeirService.RecallForSuccession(__instance, king, __state.WasRegisteredHeir);
            }

            HeirService.ClearHeir(__instance);
            HeirService.RefreshHeir(__instance);
            YearNameService.OnNewKing(__instance);
        }
    }
}
