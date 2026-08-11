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
            public InheritanceLaw AccessionLaw;
            public bool IdentityPrepared;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromRoyalClan))]
        public static bool GetKingFromRoyalClan_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            __result = HeirService.PeekRegisteredHeir(pKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SuccessionTool), nameof(SuccessionTool.getKingFromLeaders))]
        public static bool GetKingFromLeaders_Prefix(Kingdom pKingdom, ref Actor __result)
        {
            if (!UsesManagedSuccession(pKingdom)) return true;
            if (RepublicGovernmentService.IsRepublic(pKingdom))
                __result = RepublicGovernmentService.ResolveRulerForVacancy(
                    pKingdom);
            else if (HeirService.PeekRegisteredHeir(pKingdom) == null &&
                     HeirService.ShouldUseOrdinaryFallbackSuccession(pKingdom))
                __result = HeirService.GetLeaderSuccessionCandidate(pKingdom);
            else
                __result = null;
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
            bool managedSuccession = UsesManagedSuccession(__instance);
            if (!managedSuccession) return true;
            if (AccessionIdentityService.IsConfirmedCityless(__instance))
                return true;
            __state.PreviousKing = __instance.king;
            HeirService.RememberPreSuccessionKing(__instance, __state.PreviousKing);

            pActor.data.get(LineageKeys.NOBLE_DISTANCE, out int preNobleDistance, 0);
            __state.PreNobleDistance = preNobleDistance;

            __instance.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            __instance.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE,
                out __state.SuccessionSourceMode, SuccessionMode.NONE);
            HeirService.RememberAccessionModeSnapshot(__instance, pActor,
                __state.SuccessionSourceMode);
            pActor.data.get(LineageKeys.IS_HEIR, out bool heirFlag, false);
            __state.WasRegisteredHeir = heirFlag || heirId == pActor.data.id;
            __state.AccessionLaw =
                InheritanceLawService.GetEffectiveLaw(__instance);
            if (AccessionIdentityRules.ShouldDeferForInitialKingdomCreation(
                    managedSuccession,
                    pHasCurrentKing: __instance.king?.data != null,
                    pHasCapital: __instance.capital?.data != null,
                    pCandidateJoinedKingdom: pActor.kingdom == __instance))
                return true;
            __state.IdentityPrepared =
                AccessionIdentityService.Prepare(__instance, pActor);
            if (!__state.IdentityPrepared)
            {
                AccessionIdentityService.DeferInstalledKing(__instance,
                    pActor);
                return true;
            }
            AccessionIdentityService.ClearDeferredInstalledKing(__instance);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.setKing))]
        public static void SetKing_Postfix(Kingdom __instance, Actor pActor, bool pFromLoad,
            KingBranchContext __state)
        {
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.data == null) return;
            Actor king = pActor ?? __instance.king;
            bool setKingSucceeded = king?.data != null &&
                                     (pActor == null ||
                                      __instance.king == pActor);
            if (setKingSucceeded)
                ArmyRtsSuccessionRecoveryService.OnKingInstalled(
                    __instance, king, pFromLoad);
            if (pFromLoad)
            {
                HeirService.RestoreAccessionModeSnapshotRetry(__instance);
                return;
            }
            if (!UsesManagedSuccession(__instance)) return;
            if (!setKingSucceeded) return;
            if (AccessionIdentityService.IsConfirmedCityless(__instance))
                return;
            AuthoritativeSuccessionService.OnSuccessorInstalled(__instance,
                __state.PreviousKing);
            if (!__state.IdentityPrepared)
            {
                ReigningRoyalLineageIndex.OnKingInstalled(__instance, king);
                AccessionIdentityService.DeferInstalledKing(__instance,
                    king, CreateCompletionContext(__state));
                return;
            }

            if (!AccessionIdentityService.Commit(__instance, king))
            {
                ModClass.LogWarning("Accession identity commit failed for actor " +
                                    (king?.data?.id ?? -1L) + " in kingdom " +
                                    (__instance?.id ?? -1L));
                ReigningRoyalLineageIndex.OnKingInstalled(__instance, king);
                AccessionIdentityService.DeferInstalledKing(__instance, king,
                    CreateCompletionContext(__state));
                return;
            }
            AccessionIdentityService.ClearDeferredInstalledKing(__instance);
            AccessionCompletionContext completionContext =
                CreateCompletionContext(__state);
            if (!AccessionIdentityService.CompleteInstalledKing(__instance,
                    king, completionContext))
                AccessionIdentityService.DeferInstalledKing(__instance, king,
                    completionContext, pIdentityCommitted: true);
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

        private static AccessionCompletionContext CreateCompletionContext(
            KingBranchContext pContext)
        {
            return new AccessionCompletionContext
            {
                PreviousKing = pContext.PreviousKing,
                WasRegisteredHeir = pContext.WasRegisteredHeir,
                PreNobleDistance = pContext.PreNobleDistance,
                SuccessionSourceMode = pContext.SuccessionSourceMode,
                AccessionLaw = pContext.AccessionLaw,
                Captured = true
            };
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
