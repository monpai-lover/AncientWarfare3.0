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
            if (heir != null) { __result = heir; return false; }   // 世袭君主

            // 尚未共和时,先尝试城主继位(君主制延续);已是共和则跳过,直接推举平民。
            if (!RepublicGovernmentService.IsRepublic(pKingdom))
            {
                Actor leaderCandidate = HeirService.GetLeaderSuccessionCandidate(pKingdom);
                if (leaderCandidate != null)
                {
                    HeirService.MarkLeaderFallbackSuccession(pKingdom, leaderCandidate);
                    __result = leaderCandidate;
                    return false;
                }
            }

            // 无世系继承人、无城主候选 → 共和国:从平民中随机推举首领(选举、不世袭)。
            __result = RepublicGovernmentService.ElectCommonerLeader(pKingdom);
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
