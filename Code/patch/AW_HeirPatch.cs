using AncientWarfare3.api.multiplayer;
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
            public string SuccessionSourceMode;
            public bool IdentityPrepared;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromRoyalClan))]
        public static bool GetKingFromRoyalClan_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            __result = pKingdom.hasKing()
                ? HeirService.GetHeir(pKingdom)
                : RepublicGovernmentService.ResolveRulerForVacancy(pKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromLeaders))]
        public static bool GetKingFromLeaders_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            __result = RepublicGovernmentService.ResolveRulerForVacancy(pKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static bool SetKing_CaptureBranchContext_Prefix(Kingdom __instance, Actor pActor, bool pFromLoad,
            out KingBranchContext __state)
        {
            __state = default;
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            if (pFromLoad || __instance?.data == null || pActor?.data == null)
                return true;
            __state.PreviousKing = __instance.king;
            HeirService.RememberPreSuccessionKing(__instance, __state.PreviousKing);

            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int preNobleDistance, 0);
            __state.PreNobleDistance = preNobleDistance;

            __instance.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            __instance.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out __state.SuccessionSourceMode, SuccessionMode.NONE);
            pActor.data.get(LineageKeys.IS_HEIR, out bool heirFlag, false);
            __state.WasRegisteredHeir = heirFlag || heirId == pActor.data.id;
            bool managedSuccession = UsesManagedSuccession(__instance);
            if (!managedSuccession) return true;
            if (AccessionIdentityRules.ShouldDeferForInitialKingdomCreation(
                    managedSuccession,
                    pHasCurrentKing: __instance.king?.data != null,
                    pHasCapital: __instance.capital?.data != null,
                    pCandidateJoinedKingdom: pActor.kingdom == __instance))
                return true;
            __state.IdentityPrepared =
                AccessionIdentityService.Prepare(__instance, pActor);
            if (!__state.IdentityPrepared) return false;
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad,
            KingBranchContext __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (pFromLoad || __instance?.data == null) return;
            if (!UsesManagedSuccession(__instance)) return;
            if (!__state.IdentityPrepared) return;

            Actor king = pActor ?? __instance.king;
            bool setKingSucceeded = king?.data != null &&
                                     (pActor == null || __instance.king == pActor);
            if (!setKingSucceeded) return;
            if (!AccessionIdentityService.Commit(__instance, king))
            {
                ModClass.LogWarning("Accession identity commit failed for actor " +
                                    (king?.data?.id ?? -1L) + " in kingdom " +
                                    (__instance?.id ?? -1L));
                return;
            }
            FormerHeirService.ClearSnapshot(king);
            FormerKingService.ClearSnapshot(king);
            if (SuccessionTransitionRules.ShouldMarkMonarchyEstablished(
                    setKingSucceeded,
                    RepublicGovernmentService.IsRepublic(__instance),
                    RepublicGovernmentService.IsRepublicLeader(king)))
                RepublicGovernmentService.MarkMonarchyEstablished(__instance);
            LineageService.OnKingFoundBranch(__instance, king, __state.PreviousKing,
                __state.WasRegisteredHeir, __state.PreNobleDistance,
                __state.SuccessionSourceMode);
            HeirService.RecallForSuccession(__instance, king, __state.WasRegisteredHeir);
            InheritanceLawService.EstablishHereditaryBranchAfterAccession(
                __instance, king, __state.SuccessionSourceMode);
            SuccessionDisputeService.OnSuccessorInstalled(__instance, king);

            HeirService.ClearHeir(__instance);
            HeirService.RefreshHeir(__instance);
            FeudatoryService.OnPrinceAccededToEmpire(__instance, king);
            NobleRemarriageService.MarkDirty(__instance);
            CourtDirectionService.MarkDirty(__instance);
            AW3MultiplayerSuccessionFacade.NotifyKingInstalled(__instance, king);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setCapital))]
        public static void SetCapital_FinalizeDeferredFounding_Postfix(
            Kingdom __instance)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            AccessionIdentityService.FinalizeDeferredFounding(__instance);
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
