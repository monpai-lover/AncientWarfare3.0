using AncientWarfare3.core.lineage;
using AncientWarfare3.core.court;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_HeirPatch
    {
        public struct KingBranchContext
        {
            public Actor PreviousKing;
            public bool WasRegisteredHeir;
            public int PreNobleDistance;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromRoyalClan))]
        public static bool GetKingFromRoyalClan_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            __result = HeirService.GetHeir(pKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromLeaders))]
        public static bool GetKingFromLeaders_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            Actor heir = HeirService.GetHeir(pKingdom);
            if (heir != null)
            {
                __result = heir;
                return false;
            }

            if (SuccessionTransitionRules.ShouldUseInitialFounderFallback(
                    RepublicGovernmentService.IsRepublic(pKingdom),
                    RepublicGovernmentService.HasEstablishedMonarchy(pKingdom)))
            {
                Actor founder = HeirService.GetLeaderSuccessionCandidate(pKingdom);
                HeirService.MarkLeaderFallbackSuccession(pKingdom, founder);
                __result = founder;
                return false;
            }

            __result = RepublicGovernmentService.ElectLeaderForVacancy(pKingdom);
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

            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int preNobleDistance, 0);
            __state.PreNobleDistance = preNobleDistance;

            __instance.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            pActor.data.get(LineageKeys.IS_HEIR, out bool heirFlag, false);
            __state.WasRegisteredHeir = heirFlag || heirId == pActor.data.id;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad,
            KingBranchContext __state)
        {
            if (pFromLoad || __instance?.data == null) return;
            if (!UsesManagedSuccession(__instance)) return;

            Actor king = pActor ?? __instance.king;
            bool setKingSucceeded = king?.data != null &&
                                    (pActor == null || __instance.king == pActor);
            if (!setKingSucceeded) return;
            if (SuccessionTransitionRules.ShouldMarkMonarchyEstablished(
                    setKingSucceeded,
                    RepublicGovernmentService.IsRepublic(__instance),
                    RepublicGovernmentService.IsRepublicLeader(king)))
                RepublicGovernmentService.MarkMonarchyEstablished(__instance);
            LineageService.OnKingFoundBranch(__instance, king, __state.PreviousKing,
                __state.WasRegisteredHeir, __state.PreNobleDistance);
            HeirService.RecallForSuccession(__instance, king, __state.WasRegisteredHeir);

            HeirService.ClearHeir(__instance);
            HeirService.RefreshHeir(__instance);
            CourtDirectionService.MarkDirty(__instance);
            YearNameService.OnNewKing(__instance);
        }

        private static bool UsesManagedSuccession(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   SuccessionTransitionRules.ShouldUseManagedSuccession(
                       LineageService.IsXiaKingdom(pKingdom),
                       XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
        }
    }
}
