using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.presentation;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    ///     战争编年史:
    ///     Postfix WarManager.newWar  → WarRecordWriter.OnWarStart + 双国写 war_start 国家史。
    ///     Postfix WarManager.endWar  → WarRecordWriter.OnWarEnd   + 双国写 war_end   国家史。
    ///     两方法均在 WarManager 自身声明,typeof 正确。
    /// </summary>
    [HarmonyPatch]
    public static class AW_WarPatch
    {
        private sealed class WarEndParticipantSnapshot
        {
            public readonly List<long> ParticipantIds = new List<long>();
            public readonly List<Kingdom> Attackers = new List<Kingdom>();
            public readonly List<Kingdom> Defenders = new List<Kingdom>();
        }

        public readonly struct WarJoinState
        {
            public WarJoinState(bool pWasOnSide, bool pHasSource,
                WarParticipantEntrySourceKind pSourceKind,
                long pSourceKingdomId)
            {
                WasOnSide = pWasOnSide;
                HasSource = pHasSource;
                SourceKind = pSourceKind;
                SourceKingdomId = pSourceKingdomId;
            }

            public bool WasOnSide { get; }
            public bool HasSource { get; }
            public WarParticipantEntrySourceKind SourceKind { get; }
            public long SourceKingdomId { get; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.newWar))]
        private static void NewWarJoinScope_Prefix(War __instance,
            Kingdom pAttacker, Kingdom pDefender,
            out WarInitializationJoinScope __state)
        {
            __state = WarInitializationJoinScope.Open(__instance,
                pAttacker, pDefender);
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(War), nameof(War.newWar))]
        private static Exception NewWarJoinScope_Finalizer(
            WarInitializationJoinScope __state, Exception __exception)
        {
            __state?.Dispose();
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DiplomacyManager), "startWar",
            new[] { typeof(Kingdom), typeof(Kingdom), typeof(WarTypeAsset), typeof(bool) })]
        public static bool DiplomacyStartWar_Prefix(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pAsset,
            ref War __result)
        {
            if (!PeasantRebelRouteService.CanStartWar(pAttacker,
                    pDefender, out _, out _))
            {
                __result = null;
                return false;
            }
            if (!WarDecisionService.ShouldBlockWarStart(pAttacker, pDefender, pAsset)) return true;
            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
        public static bool NewWar_Prefix(Kingdom pAttacker, Kingdom pDefender, WarTypeAsset pType, ref War __result)
        {
            if (!PeasantRebelRouteService.CanStartWar(pAttacker,
                    pDefender, out _, out _))
            {
                __result = null;
                return false;
            }
            if (WarDecisionService.ShouldBlockWarStart(pAttacker, pDefender,
                    pType))
            {
                __result = null;
                return false;
            }
            if (pType?.id == ZhuluWarRules.WarTypeId) return true;
            if (CityReservePoolService.PrepareWarEntry(pAttacker,
                    pDefender)) return true;
            __result = null;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.newWar))]
        public static void NewWar_Postfix(War __result)
        {
            if (__result?.data == null) return;
            PeasantRebelRouteService.OnWarStarted(__result);
            SpecialGovernmentWarParticipationService.OnWarStarted(__result);
            if (ZhuluWarService.IsZhuluWar(__result,
                    requireActive: false))
            {
                if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
                ZhuluWarService.PersistDeclaredDefender(__result,
                    ZhuluWarDeclarationScope.CurrentDefenderId);
                WarRecordWriter.OnWarStart(__result);
                RecordNativeZhuluStart(__result);
                WarScoreService.StartWar(__result);
                return;
            }
            WarRecordWriter.OnWarStart(__result);
            RecordMainBelligerents(__result);
            WarScoreService.StartWar(__result);
            SyntheticMobilizationLedgerService.OnWarStarted(__result);
            CityReservePoolService.OnWarStarted(__result);
            ArmyRtsWarLifecycleService.OnWarStarted(__result);
            KingdomWarDirectorService.OnWarStarted(__result);
            ArmyLogisticsService.OnWarStarted(__result);
            DiplomacyProposalService.RegisterWarSettlementBaseline(__result);
            VassalService.OnWarStarted(__result);
            CentralizationBorderDeploymentService.OnWarStarted(__result);
            MandateService.OnWarStarted(__result);
            RoyalAsylumService.OnWarStarted(__result);
            MilitaryEmergencyService.OnWarStarted(__result);
            WartimeGarrisonService.OnWarStarted(__result);
            TemporarySlaveVanguardService.OnWarStarted(__result);
            WarNoticeService.OnWarStarted(__result);
            DiplomaticCoalitionService.OnWarStarted(__result);
            CoalitionWarTaskService.OnWarStarted(__result);
            DiplomacyConversationService.RecordWarStarted(__result);
            VassalMapModeService.DirtyMapIfActive();
            HierarchicalVassalMapModeService.MarkHierarchyDirty();
            ArmyRtsPlanSnapshotService.OnWarStarted(__result);

            Kingdom atk = __result.getMainAttacker();
            Kingdom def = __result.getMainDefender();
            string warTypeName = WarRuntimeDisplayService.Resolve(__result);
            if (atk?.data != null)
                ChronicleEvents.OnWarStart(atk, def, def?.name ?? "未知", warTypeName);
            if (def?.data != null)
                ChronicleEvents.OnWarStart(def, atk, atk?.name ?? "未知", warTypeName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.increaseDeathsAttackers))]
        public static void IncreaseDeathsAttackers_Postfix(War __instance)
        {
            WarScoreService.RecordDeath(__instance,
                casualtyWasAttacker: true);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.increaseDeathsDefenders))]
        public static void IncreaseDeathsDefenders_Postfix(War __instance)
        {
            WarScoreService.RecordDeath(__instance,
                casualtyWasAttacker: false);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
        public static bool JoinAttackers_Prefix(War __instance,
            Kingdom pKingdom, out WarJoinState __state)
        {
            if (ZhuluWarService.IsZhuluWar(__instance,
                    requireActive: false))
            {
                __state = default;
                return true;
            }
            return CanJoin(__instance, pKingdom, pDefender: false,
                out __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.joinAttackers))]
        public static void JoinAttackers_Postfix(War __instance,
            Kingdom pKingdom, WarJoinState __state)
        {
            if (ZhuluWarService.IsZhuluWar(__instance,
                    requireActive: false)) return;
            CompleteJoin(__instance, pKingdom, pDefender: false, __state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
        public static bool JoinDefenders_Prefix(War __instance,
            Kingdom pKingdom, out WarJoinState __state)
        {
            if (ZhuluWarService.IsZhuluWar(__instance,
                    requireActive: false))
            {
                __state = default;
                return true;
            }
            return CanJoin(__instance, pKingdom, pDefender: true,
                out __state);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), nameof(War.joinDefenders))]
        public static void JoinDefenders_Postfix(War __instance,
            Kingdom pKingdom, WarJoinState __state)
        {
            if (ZhuluWarService.IsZhuluWar(__instance,
                    requireActive: false)) return;
            CompleteJoin(__instance, pKingdom, pDefender: true, __state);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), nameof(War.update))]
        private static bool Update_Prefix(War __instance)
        {
            return WarRosterIntegrityService.PrepareForUpdate(__instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(War), "removeFromWar")]
        private static bool RemoveFromWar_Prefix(War __instance,
            Kingdom pKingdom, out bool __state)
        {
            __state = false;
            if (pKingdom?.data == null)
            {
                WarRosterIntegrityService.RepairActiveWarRoster(__instance);
                return false;
            }
            try
            {
                __state = __instance?.data != null &&
                          pKingdom?.data != null &&
                          __instance.hasKingdom(pKingdom);
            }
            catch { }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(War), "removeFromWar")]
        private static void RemoveFromWar_Postfix(War __instance,
            Kingdom pKingdom, bool __state)
        {
            if (ZhuluWarService.IsZhuluWar(__instance,
                    requireActive: false)) return;
            bool remainsOnSide;
            try
            {
                remainsOnSide = __instance?.data != null &&
                                pKingdom?.data != null &&
                                __instance.hasKingdom(pKingdom);
            }
            catch { return; }
            if (!WarParticipantLifecycleRules.ShouldNotifyDeparture(
                    __state,
                    remainsOnSideAfterRemove: remainsOnSide) ||
                __instance?.data == null || pKingdom?.data == null) return;
            OnKingdomLeftWar(__instance, pKingdom);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
        private static bool EndWar_Prefix(War pWar,
            out WarEndParticipantSnapshot __state)
        {
            __state = ZhuluWarService.IsZhuluWar(pWar,
                requireActive: false)
                ? null
                : CaptureParticipants(pWar);
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(WarManager), nameof(WarManager.endWar))]
        private static void EndWar_Postfix(War pWar, WarWinner pWinner,
            WarEndParticipantSnapshot __state, bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (pWar?.data == null) return;
            if (ZhuluWarService.IsZhuluWar(pWar,
                    requireActive: false))
            {
                if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
                CleanupLegacyZhuluRuntime(pWar);
                DiplomaticWarDeclarationService.OnWarEnded(pWar);
                WarRecordWriter.OnWarEnd(pWar, pWinner);
                DiplomacyConversationService.RecordWarEnded(pWar, pWinner);
                RecordNativeZhuluEnd(pWar, pWinner);
                return;
            }
            WarParticipantEntrySourceService.Instance.
                TryEndAllActiveSourcesForWar(pWar.data.id,
                    LineageService.CurTime());
            CloseParticipantSources(pWar.data.id,
                __state?.ParticipantIds);
            ArmyReplenishmentOperationService.OnWarEnded(pWar);
            DiplomaticWarDeclarationService.OnWarEnded(pWar);
            KingdomWarDirectorService.OnWarEnded(pWar);
            ArmyRtsWarLifecycleService.OnWarEnded(pWar);
            CoalitionWarTaskService.OnWarEnded(pWar);
            WarMilitaryFactsService.OnWarEnded(pWar);
            ArmyLogisticsService.OnWarEnded(pWar);
            ArmyStallWatchdogService.OnWarEnded(pWar);
            WarBattleEpisodeService.OnWarEnded(pWar);
            WarScoreService.EndWar(pWar, pWinner);
            SpecialGovernmentWarParticipationService.OnWarEnded(pWar);
            RoyalAsylumService.OnWarEnded(pWar);
            MilitaryEmergencyService.OnWarEnded(pWar);
            SyntheticMobilizationLedgerService.OnWarEnded(pWar);
            CityReservePoolService.OnWarEnded(pWar);
            TemporaryLevyService.OnReplenishmentWarEnded(pWar);
            WartimeGarrisonService.OnWarEnded(pWar);
            TemporarySlaveVanguardService.OnWarEnded(pWar);
            WarRecordWriter.OnWarEnd(pWar, pWinner);
            WarTerritoryService.OnWarEnded(pWar, pWinner);
            AutonomousRestorationService.OnWarEnded(pWar);
            ApplyDiplomacyWarResult(pWar, pWinner);
            MandateService.OnWarEnded(pWar, pWinner);
            MandateRebelService.OnWarEnded(pWar, pWinner);
            FeudatoryJingnanService.OnWarEnded(pWar, pWinner);
            CoupRestorationService.OnWarEnded(pWar, pWinner);
            SuccessionDisputeService.OnWarEnded(pWar, pWinner);
            GeneralService.OnWarEnded(pWar, pWinner);
            DiplomacyConversationService.RecordWarEnded(pWar, pWinner);
            if (!DiplomacyProposalService.RegisterCoalitionTruces(pWar,
                    __state?.Attackers, __state?.Defenders))
                ModClass.LogWarning("Coalition truce registration failed for war " +
                                    pWar.data.id + ".");
            VassalMapModeService.DirtyMapIfActive();
            HierarchicalVassalMapModeService.MarkHierarchyDirty();
            ArmyRtsPlanSnapshotService.OnWarEnded(pWar);

            Kingdom atk = pWar.getMainAttacker();
            Kingdom def = pWar.getMainDefender();
            var result = WarRecordWriter.WinnerLabelRich(pWinner, atk, def);
            string warName = WarRuntimeDisplayService.Resolve(pWar);
            if (atk?.data != null)
                ChronicleEvents.OnWarEnd(atk, def, def?.name ?? "未知",
                    warName, result);
            if (def?.data != null)
                ChronicleEvents.OnWarEnd(def, atk, atk?.name ?? "未知",
                    warName, result);
        }

        private static void OnKingdomJoinedWar(War pWar, Kingdom pKingdom, bool pDefender)
        {
            WarScoreService.RegisterParticipantMobilization(pWar, pKingdom);
            ArmyRtsWarLifecycleService.OnWarParticipantChanged(pWar,
                pKingdom);
            KingdomWarDirectorService.OnWarParticipantChanged(pWar,
                pKingdom);
            CoalitionWarTaskService.OnWarParticipantChanged(pWar,
                pKingdom);
            ArmyLogisticsService.OnWarParticipantJoined(pWar, pKingdom,
                pDefender ? false : true);
            WarParticipantCityBaselineService.RegisterParticipant(pWar, pKingdom);
            SyntheticMobilizationLedgerService.OnKingdomJoinedWar(pWar,
                pKingdom);
            CityReservePoolService.OnKingdomJoinedWar(pWar, pKingdom);
            MilitaryEmergencyService.OnKingdomJoinedWar(pWar, pKingdom, pDefender);
            WartimeGarrisonService.OnKingdomWarStateChanged(pKingdom);
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
            VassalMapModeService.DirtyMapIfActive();
            HierarchicalVassalMapModeService.MarkHierarchyDirty();
        }

        private static void RecordNativeZhuluStart(War pWar)
        {
            DiplomacyConversationService.RecordWarStarted(pWar);
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender =
                ZhuluWarService.ResolvePrincipalDefender(pWar);
            HistoryText defenderHistory =
                DeclaredDefenderHistory(pWar, defender);
            string name = WarRuntimeDisplayService.Resolve(pWar);
            if (attacker?.data != null)
                ChronicleEvents.OnWarStart(attacker, defenderHistory, name);
            if (defender?.data != null)
                ChronicleEvents.OnWarStart(defender, attacker,
                    attacker?.name ?? "未知", name);
        }

        private static void RecordNativeZhuluEnd(War pWar,
            WarWinner pWinner)
        {
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender =
                ZhuluWarService.ResolvePrincipalDefender(pWar);
            HistoryText defenderHistory =
                DeclaredDefenderHistory(pWar, defender);
            var result = WarRecordWriter.WinnerLabelRich(pWinner,
                attacker, defender);
            string name = WarRuntimeDisplayService.Resolve(pWar);
            if (attacker?.data != null)
                ChronicleEvents.OnWarEnd(attacker, defenderHistory, name,
                    result);
            if (defender?.data != null)
                ChronicleEvents.OnWarEnd(defender, attacker,
                    attacker?.name ?? "未知", name, result);
        }

        private static HistoryText DeclaredDefenderHistory(War pWar,
            Kingdom pDefender)
        {
            if (pDefender?.data != null)
                return HistoryText.Kingdom(pDefender,
                    pDefender.name ?? "\u672A\u77E5");
            if (!ZhuluWarService.TryGetDeclaredDefenderIdentity(pWar,
                    out long defenderId, out string name,
                    out string color))
                return HistoryText.PlainText("\u672A\u77E5");
            if (string.IsNullOrEmpty(name)) name = "\u672A\u77E5";
            return HistoryText.Reference(name, color, "kingdom", defenderId);
        }

        private static void CleanupLegacyZhuluRuntime(War pWar)
        {
            if (pWar?.data == null) return;
            WarParticipantEntrySourceService.Instance.
                TryEndAllActiveSourcesForWar(pWar.data.id,
                    LineageService.CurTime());
            ArmyReplenishmentOperationService.OnWarEnded(pWar);
            KingdomWarDirectorService.CleanupExcludedWar(pWar);
            ArmyRtsWarLifecycleService.OnWarEnded(pWar);
            CoalitionWarTaskService.OnWarEnded(pWar);
            WarMilitaryFactsService.OnWarEnded(pWar);
            ArmyLogisticsService.OnWarEnded(pWar);
            ArmyStallWatchdogService.OnWarEnded(pWar);
            ArmyRtsPlanSnapshotService.OnWarEnded(pWar);
            WarTerritoryService.ResolveLegacyZhuluGoals(pWar.data.id,
                "legacy_zhulu_closed");
        }

        private static void OnKingdomLeftWar(War pWar, Kingdom pKingdom)
        {
            ArmyRtsControllerService.InvalidateWarParticipant(
                pWar?.data?.id ?? -1L, pKingdom?.data?.id ?? -1L);
            ArmyRtsWarLifecycleService.OnWarParticipantChanged(pWar,
                pKingdom);
            WarParticipantEntrySourceService.Instance.TryEndAllActiveSources(
                pWar?.data?.id ?? -1L, pKingdom?.data?.id ?? -1L,
                LineageService.CurTime());
            KingdomWarDirectorService.OnWarParticipantChanged(pWar,
                pKingdom);
            CoalitionWarTaskService.OnWarParticipantChanged(pWar,
                pKingdom);
            ArmyLogisticsService.OnWarParticipantLeft(pWar, pKingdom);
            WarScoreService.ClearDepartedParticipantControls(pWar, pKingdom);
            MilitaryEmergencyService.OnKingdomLeftWar(pWar, pKingdom);
            SyntheticMobilizationLedgerService.OnKingdomLeftWar(pWar,
                pKingdom);
            CityReservePoolService.OnKingdomLeftWar(pWar, pKingdom);
            WartimeGarrisonService.OnKingdomWarStateChanged(pKingdom);
            TemporarySlaveVanguardService.OnEmergencyChanged(pKingdom);
            VassalMapModeService.DirtyMapIfActive();
            HierarchicalVassalMapModeService.MarkHierarchyDirty();
        }

        private static bool CanJoin(War pWar, Kingdom pKingdom,
            bool pDefender, out WarJoinState pState)
        {
            bool wasOnSide = false;
            try
            {
                wasOnSide = pWar?.data != null && pKingdom?.data != null &&
                            (pDefender
                                ? pWar.isDefender(pKingdom)
                                : pWar.isAttacker(pKingdom));
            }
            catch { }
            bool hasSource = WarParticipantEntrySourceScope.TryCurrent(
                pWar, pKingdom, out WarParticipantEntrySourceKind sourceKind,
                out long sourceKingdomId);
            bool initializingMainBelligerent =
                WarInitializationJoinScope.Contains(pWar, pKingdom);
            if (!hasSource && initializingMainBelligerent)
            {
                hasSource = true;
                sourceKind = WarParticipantEntrySourceKind.MainBelligerent;
                sourceKingdomId = pKingdom?.data?.id ?? -1L;
            }
            pState = new WarJoinState(wasOnSide, hasSource, sourceKind,
                sourceKingdomId);
            if (pWar?.data == null || pKingdom?.data == null || wasOnSide)
                return true;
            bool lookupSucceeded =
                WarParticipantEntrySourceService.Instance.TryCanJoinWar(
                    pWar.data.id, pKingdom.id, out bool sourceAllowsJoin);
            bool canJoin = WarParticipantLifecycleRules.CanJoin(wasOnSide,
                initializingMainBelligerent, lookupSucceeded,
                hasSeparatePeaceExit: !sourceAllowsJoin);
            if (!canJoin) return false;
            return !CityReservePoolRules.
                       ShouldPrepareWarEntryFromJoinPrefix(canJoin,
                           initializingMainBelligerent) ||
                   CityReservePoolService.PrepareWarEntry(pKingdom);
        }

        private static void CompleteJoin(War pWar, Kingdom pKingdom,
            bool pDefender, WarJoinState pState)
        {
            if (pWar?.data == null || pKingdom?.data == null) return;
            bool joined;
            try
            {
                joined = pDefender
                    ? pWar.isDefender(pKingdom)
                    : pWar.isAttacker(pKingdom);
            }
            catch { return; }
            if (!joined) return;
            bool sourceWriteSucceeded = true;
            if (pState.HasSource)
                sourceWriteSucceeded = WarParticipantEntrySourceService.
                    Instance.TryRecordSource(
                    pWar.data.id, pKingdom.id, pState.SourceKind,
                    pState.SourceKingdomId, LineageService.CurTime());
            if (WarParticipantLifecycleRules.ShouldRollbackJoin(
                    pState.WasOnSide, joined,
                    sourceRequired: WarParticipantLifecycleRules.RequiresDurableJoinSource(
                        pState.HasSource, pState.SourceKind),
                    sourceWriteSucceeded: sourceWriteSucceeded))
            {
                bool rollbackVerified = TryRollbackJoin(pWar, pKingdom,
                    pDefender);
                bool lookupSucceeded = TryIsKingdomInWar(pWar, pKingdom,
                    out bool remainsOnSide);
                if (WarParticipantLifecycleRules.ShouldQueueRollbackRepair(
                        rollbackVerified, lookupSucceeded, remainsOnSide))
                {
                    bool participantServicesStarted = !rollbackVerified;
                    if (participantServicesStarted)
                        OnKingdomJoinedWar(pWar, pKingdom, pDefender);
                    QueueRollbackJoin(pWar, pKingdom, pDefender,
                        participantServicesStarted);
                }
                return;
            }
            if (!pState.WasOnSide)
                OnKingdomJoinedWar(pWar, pKingdom, pDefender);
        }

        private static bool TryRollbackJoin(War pWar, Kingdom pKingdom,
            bool pDefender)
        {
            if (pWar?.data == null || pKingdom?.data == null) return true;
            try
            {
                if (!pWar.hasKingdom(pKingdom)) return true;
                if (pDefender)
                {
                    pWar.removeDefender(pKingdom, pInPeace: true);
                    pWar.data.past_defenders.Remove(pKingdom.id);
                    pWar.data.died_defenders.Remove(pKingdom.id);
                }
                else
                {
                    pWar.removeAttacker(pKingdom, pInPeace: true);
                    pWar.data.past_attackers.Remove(pKingdom.id);
                    pWar.data.died_attackers.Remove(pKingdom.id);
                }
                pWar.prepare();
                return !pWar.hasKingdom(pKingdom);
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "War join provenance rollback failed: war=" +
                    (pWar?.data?.id ?? -1L) + " kingdom=" +
                    (pKingdom?.data?.id ?? -1L) + " " + error.Message);
                return false;
            }
        }

        private static void QueueRollbackJoin(War pWar, Kingdom pKingdom,
            bool pDefender, bool pParticipantServicesStarted)
        {
            long warId = pWar?.data?.id ?? -1L;
            long kingdomId = pKingdom?.data?.id ?? -1L;
            QueueRollbackJoin(warId, kingdomId, pDefender,
                pParticipantServicesStarted);
        }

        private static void QueueRollbackJoin(long pWarId, long pKingdomId,
            bool pDefender, bool pParticipantServicesStarted)
        {
            if (pWarId < 0 || pKingdomId < 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "war_join_rollback:" + pWarId + ":" + pKingdomId,
                DeferredWorkClass.CriticalRuntime,
                () => RetryRollbackJoin(pWarId, pKingdomId, pDefender,
                    pParticipantServicesStarted));
        }

        private static void RetryRollbackJoin(long pWarId, long pKingdomId,
            bool pDefender, bool pParticipantServicesStarted)
        {
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            Kingdom kingdom = WarPeaceSettlementWorld.FindKingdom(pKingdomId);
            if (war?.data == null || kingdom?.data == null || war.hasEnded())
                return;
            if (!TryIsKingdomInWar(war, kingdom, out bool remainsOnSide))
            {
                QueueRollbackJoin(pWarId, pKingdomId, pDefender,
                    pParticipantServicesStarted);
                return;
            }
            if (remainsOnSide)
                TryRollbackJoin(war, kingdom, pDefender);
            bool lookupSucceeded = TryIsKingdomInWar(war, kingdom,
                out remainsOnSide);
            if (!lookupSucceeded || remainsOnSide)
            {
                QueueRollbackJoin(pWarId, pKingdomId, pDefender,
                    pParticipantServicesStarted);
                return;
            }
            if (WarParticipantLifecycleRules.ShouldNotifyRollbackDeparture(
                    participantServicesStarted: pParticipantServicesStarted,
                    membershipLookupSucceeded: lookupSucceeded,
                    remainsOnSideAfterRollback: remainsOnSide))
                OnKingdomLeftWar(war, kingdom);
        }

        private static bool TryIsKingdomInWar(War pWar, Kingdom pKingdom,
            out bool pRemainsOnSide)
        {
            pRemainsOnSide = false;
            if (pWar?.data == null || pKingdom?.data == null) return true;
            try
            {
                pRemainsOnSide = pWar.hasKingdom(pKingdom);
                return true;
            }
            catch { return false; }
        }

        private static void RecordMainBelligerents(War pWar)
        {
            Kingdom attacker = pWar?.getMainAttacker();
            Kingdom defender = pWar?.getMainDefender();
            double time = LineageService.CurTime();
            if (attacker?.data != null)
                WarParticipantEntrySourceService.Instance.TryRecordSource(
                    pWar.data.id, attacker.id,
                    WarParticipantEntrySourceKind.MainBelligerent,
                    attacker.id, time);
            if (defender?.data != null)
                WarParticipantEntrySourceService.Instance.TryRecordSource(
                    pWar.data.id, defender.id,
                    WarParticipantEntrySourceKind.MainBelligerent,
                    defender.id, time);
        }

        private static WarEndParticipantSnapshot CaptureParticipants(War pWar)
        {
            var result = new WarEndParticipantSnapshot();
            if (pWar?.data == null) return result;
            var seen = new HashSet<long>();
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    AddParticipant(kingdom, result.Attackers,
                        result.ParticipantIds, seen);
                foreach (Kingdom kingdom in pWar.getDefenders())
                    AddParticipant(kingdom, result.Defenders,
                        result.ParticipantIds, seen);
            }
            catch { }
            return result;
        }

        private static void AddParticipant(Kingdom pKingdom,
            List<Kingdom> pSide, List<long> pParticipantIds,
            HashSet<long> pSeen)
        {
            long id = pKingdom?.data?.id ?? -1L;
            if (id < 0 || !pSeen.Add(id)) return;
            pSide.Add(pKingdom);
            pParticipantIds.Add(id);
        }

        private static void CloseParticipantSources(long pWarId,
            IReadOnlyList<long> pParticipantIds)
        {
            if (pWarId < 0 || pParticipantIds == null) return;
            double endedTime = LineageService.CurTime();
            for (int i = 0; i < pParticipantIds.Count; i++)
                WarParticipantEntrySourceService.Instance.
                    TryEndAllActiveSources(pWarId, pParticipantIds[i],
                        endedTime);
        }

        private static void ApplyDiplomacyWarResult(War pWar, WarWinner pWinner)
        {
            VassalService.OnWarEnded(pWar, pWinner);
        }

    }
}
