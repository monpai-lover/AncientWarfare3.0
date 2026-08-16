using System;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_ActorDeathPatch
    {
        internal static long DyingKingActorId = -1L;

        public sealed class DieState
        {
            public long Diagnostic;
            public Kingdom DyingKingdom;
            public long DyingKingActorId = -1L;
            public Army DyingCaptainArmy;
            public long DyingCaptainActorId = -1L;
            public long BanditStrongholdCityId = -1L;
            public long HostileKillerKingdomId = -1L;
        }

        [HarmonyPriority(Priority.Last)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        public static void ClearWorld_Prefix()
        {
            if (!AWAsyncClearWorldGuard.CleanupAllowed) return;
            NobleRankService.ClearPendingDeathSuccessions();
            VirtualNobleTitleService.ClearRuntime();
            HeirMinimapMarkerIndex.Reset();
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static void Die_Prefix(Actor __instance, AttackType pType,
            out DieState __state)
        {
            __state = new DieState();
            if (AW3MultiplayerReplicaScope.IsApplying) return;
            if (__instance?.data == null) return;
            City dyingCity = __instance.getCity();
            if (PeasantRebelBanditStrongholdService.IsStronghold(dyingCity))
            {
                __state.BanditStrongholdCityId = dyingCity.getID();
                Kingdom attacker = __instance.attackedBy?.kingdom;
                if (PeasantRebelBanditStrongholdService.IsHostileKingdom(
                        dyingCity.kingdom, attacker))
                    __state.HostileKillerKingdomId = attacker.getID();
            }
            Army dyingArmy = __instance.army;
            bool wasCaptain = false;
            try
            {
                wasCaptain = dyingArmy?.data != null &&
                             dyingArmy.getCaptain() == __instance;
            }
            catch { }
            if (ArmyRtsSuccessionRecoveryRules.
                ShouldEnqueueCaptainRecovery(
                        dyingArmy?.data != null, wasCaptain,
                        dyingArmy?.data != null &&
                        ArmyRtsControllerService.HasActiveMission(
                            dyingArmy.id),
                        MilitaryEmergencyService.HasAny(__instance.kingdom),
                        RoyalGuardService.IsRoyalGuard(__instance)))
            {
                __state.DyingCaptainArmy = dyingArmy;
                __state.DyingCaptainActorId = __instance.data.id;
            }
            bool suppressPersonalHistory =
                SyntheticLevyService.SuppressPersonalHistory(__instance);
            ActorAgeWorkService.Remove(__instance.data.id);
            if (!__instance.isAlive()) return;
            __state.Diagnostic = RuntimePerformanceDiagnostic.BeginDeathEvent();
            if (__instance.isKing() && __instance.kingdom != null)
            {
                __state.DyingKingdom = __instance.kingdom;
                __state.DyingKingActorId = __instance.data.id;
                HeirService.RememberPreSuccessionKing(__state.DyingKingdom, __instance);
            }

            long militaryStage = RuntimePerformanceDiagnostic.BeginDeathStage(
                ActorDeathPerformanceStage.MilitaryIndexes);
            try
            {
                ArmyLogisticsService.OnActorDying(__instance);
                ArmyRetreatService.OnActorDying(__instance);
                KingdomMilitaryReadinessService.MarkOrdinaryArmyActorDirty(__instance);
                WarNoticeService.QueueArmyChanged(__instance.kingdom, __instance.army);
                WartimeGarrisonService.OnActorInvalidated(__instance);
                TemporarySlaveVanguardService.OnMemberInvalidated(__instance);
                SlavePopulationIndexService.Deactivate(__instance);
                DynasticLivingSonIndexService.OnActorDying(__instance);
                SuccessionRelationshipIndex.OnDying(__instance);
                HeirService.MarkSuccessionDirtyForActor(__instance);
                if (__state.DyingKingdom?.data != null &&
                    __state.DyingKingActorId == __instance.data.id)
                    ReigningRoyalLineageIndex.OnKingDying(
                        __state.DyingKingdom, __instance);
                NobleRemarriageService.MarkDirtyForPartnerDeath(__instance);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(
                    ActorDeathPerformanceStage.MilitaryIndexes, militaryStage);
            }
            if (suppressPersonalHistory) return;
            TryRunDeathStage(__instance, ActorDeathPerformanceStage.DynasticTitle,
                "dynastic title and feudatory succession",
                () => DynasticTitleService.OnActorDying(__instance));
            TryRunDeathStage(__instance, ActorDeathPerformanceStage.NobleTitle,
                "noble title succession", () =>
                {
                    NobleRankService.OnActorDying(__instance);
                    VirtualNobleTitleService.OnActorDying(__instance);
                });
            TryRunDeathStage(__instance, ActorDeathPerformanceStage.BondDeath,
                "ruler household closure", () =>
                RulerHouseholdService.OnActorDying(__instance));

            TryRunDeathStage(__instance, ActorDeathPerformanceStage.DeathCause,
                "death cause", () =>
                EnsureDeathCause(__instance, pType));
            TryRunDeathStage(__instance, ActorDeathPerformanceStage.RulerSnapshot,
                "ruler fact snapshot", () =>
                RulerTitleFactService.ArchivePersonalSnapshot(__instance));
            TryRunDeathStage(__instance, ActorDeathPerformanceStage.HistoricalFigure,
                "historical figure death", () =>
            {
                if (__instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIRST) ||
                    __instance.hasTrait(content.figures.HistoricalFigureService.TRAIT_FIGURE))
                    content.figures.HistoricalFigureService.OnFigureDied(__instance);
            });

            if (!TryEvaluateDeathStage(__instance,
                    ActorDeathPerformanceStage.LineageEligibility,
                    "lineage eligibility",
                    () => LineageService.UsesAwLineageSystem(__instance),
                    out bool usesAwLineage)) return;
            if (!usesAwLineage)
            {
                if (!TryEvaluateDeathStage(__instance,
                        ActorDeathPerformanceStage.LineageEligibility,
                        "traceable archive eligibility",
                        () => LineageService.HasTraceableArchive(__instance),
                        out bool hasTraceableArchive) || !hasTraceableArchive) return;
                TryRunDeathStage(__instance,
                    ActorDeathPerformanceStage.LineageArchive,
                    "traceable actor archive", () =>
                    LineageArchiveWriter.QueueDeath(__instance,
                        pTraceOnly: true));
                return;
            }

            TryRunDeathStage(__instance, ActorDeathPerformanceStage.LineageArchive,
                "lineage actor archive", () =>
                LineageArchiveWriter.QueueDeath(__instance,
                    pTraceOnly: false));

            bool dyingKing = false;
            Kingdom dyingKingdom = null;
            TryRunDeathStage(__instance,
                ActorDeathPerformanceStage.KingSuccession,
                "king death context", () =>
            {
                if (__instance.isKing() && __instance.kingdom != null)
                {
                    dyingKing = true;
                    dyingKingdom = __instance.kingdom;
                }
            });

            if (dyingKing)
            {
                DyingKingActorId = __instance.data.id;
                TryRunDeathStage(__instance,
                    ActorDeathPerformanceStage.KingChronicle,
                    "king death chronicle", () =>
                    ChronicleEvents.OnKingDied(dyingKingdom, __instance));
            }
            else
            {
                TryRunDeathStage(__instance,
                    ActorDeathPerformanceStage.FormerRuler,
                    "former ruler death", () =>
                    PosthumousTitleService.OnFormerRulerDied(__instance));
            }

            TryRunDeathStage(__instance, ActorDeathPerformanceStage.PersonHistory,
                "person death history", () =>
            {
                __instance.data.get(LineageKeys.LINEAGE_ID, out long lid, -1L);
                if (lid >= 0)
                {
                    string name = __instance.getName();
                    __instance.data.get(LineageKeys.DEATH_CAUSE, out string cause, "");
                    HistoryText causeText = string.IsNullOrEmpty(cause)
                        ? HistoryText.PlainText("")
                        : HistoryLocalizationRules.H("aw_hist_death_cause_prefix") +
                          HistoryText.PlainText(cause) +
                          HistoryLocalizationRules.H("aw_hist_death_cause_suffix");
                    Kingdom deathContext = PosthumousTitleService.ResolveCapturedRulerLiveKingdom(__instance) ??
                                           __instance.kingdom;
                    HistoryWriter.RecordPerson(
                        __instance.data.id, deathContext, name,
                        PersonEvent.DEATH,
                        HistoryText.Actor(__instance, name) +
                        HistoryLocalizationRules.H("aw_hist_person_died") + causeText,
                        ChronicleCategory.LIFE);
                }
            });

            TryRunDeathStage(__instance, ActorDeathPerformanceStage.BondDeath,
                "bond death", () =>
                ChronicleEvents.OnBondDeath(__instance));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static void Die_Postfix(Actor __instance, DieState __state,
            bool __runOriginal)
        {
            if (!__runOriginal) return;
            if (AW3MultiplayerReplicaScope.IsApplying ||
                AW3MultiplayerReplicaScope.IsReplicaSession) return;
            SyntheticLevyService.OnActorDied(__instance);
            if (__state?.BanditStrongholdCityId > 0 &&
                __instance != null && !__instance.isAlive())
            {
                try
                {
                    PeasantRebelBanditStrongholdService.
                        OnBanditResidentDied(
                            __state.BanditStrongholdCityId,
                            __state.HostileKillerKingdomId);
                }
                catch (Exception error)
                {
                    LogDeathStageFailure(__instance,
                        "bandit stronghold settlement", error);
                }
            }
            if (__state?.DyingCaptainArmy?.data != null &&
                __state.DyingCaptainActorId >= 0L &&
                __instance != null && !__instance.isAlive())
                ArmyRtsSuccessionRecoveryService.OnCaptainDied(
                    __state.DyingCaptainArmy,
                    __state.DyingCaptainActorId);
            if (__state?.DyingKingdom == null ||
                __state.DyingKingActorId < 0L || __instance == null ||
                __instance.isAlive()) return;
            TryRunDeathStage(__instance,
                ActorDeathPerformanceStage.KingCivilService,
                "civil-service ranking ruler death", () =>
                CivilServiceExamService.OnCurrentRulerDied(
                    __state.DyingKingdom));
            TryRunDeathStage(__instance,
                ActorDeathPerformanceStage.KingSuccession,
                "military governorate ruler succession", () =>
                MilitaryGovernorateSuccessionService.OnRulerDied(
                    __state.DyingKingdom, __state.DyingKingActorId));
        }

        private static void TryRunDeathStage(Actor pActor,
            ActorDeathPerformanceStage pPerformanceStage, string pStage,
            Action pAction)
        {
            if (pAction == null) return;
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeathStage(
                pPerformanceStage);
            try
            {
                pAction();
            }
            catch (Exception error)
            {
                LogDeathStageFailure(pActor, pStage, error);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(pPerformanceStage,
                    diagnostic);
            }
        }

        private static bool TryEvaluateDeathStage(Actor pActor,
            ActorDeathPerformanceStage pPerformanceStage, string pStage,
            Func<bool> pAction, out bool pResult)
        {
            pResult = false;
            if (pAction == null) return false;
            long diagnostic = RuntimePerformanceDiagnostic.BeginDeathStage(
                pPerformanceStage);
            try
            {
                pResult = pAction();
                return true;
            }
            catch (Exception error)
            {
                LogDeathStageFailure(pActor, pStage, error);
                return false;
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathStage(pPerformanceStage,
                    diagnostic);
            }
        }

        private static void LogDeathStageFailure(Actor pActor, string pStage, Exception pError)
        {
            try
            {
                ModClass.LogWarning("Actor death " + pStage + " failed for actor=" +
                                    (pActor?.data?.id ?? -1L) + ": " + pError.Message);
            }
            catch
            {
                // The engine death path must continue even if diagnostics fail.
            }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Actor), "die", new[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        public static Exception Die_Finalizer(Actor __instance, DieState __state,
            Exception __exception)
        {
            try
            {
                if (__state != null &&
                    DyingKingActorId == __state.DyingKingActorId)
                    DyingKingActorId = -1L;
                return __exception;
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDeathEvent(
                    __state?.Diagnostic ?? 0L);
            }
        }

        private static void EnsureDeathCause(Actor pActor, AttackType pType)
        {
            if (pActor?.data == null) return;
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string existing, "");
            if (!string.IsNullOrEmpty(existing)) return;
            pActor.data.set(LineageKeys.DEATH_CAUSE, DescribeDeathType(pType));
        }

        private static string DescribeDeathType(AttackType pType)
        {
            switch (pType)
            {
                case AttackType.Acid: return HistoryLocalizationRules.Text("aw_death_cause_acid");
                case AttackType.Fire: return HistoryLocalizationRules.Text("aw_death_cause_fire");
                case AttackType.Age: return HistoryLocalizationRules.Text("aw_death_cause_age");
                case AttackType.Starvation: return HistoryLocalizationRules.Text("aw_death_cause_starvation");
                case AttackType.Plague:
                case AttackType.Infection:
                case AttackType.Tumor:
                case AttackType.AshFever: return HistoryLocalizationRules.Text("aw_death_cause_disease");
                case AttackType.Eaten: return HistoryLocalizationRules.Text("aw_death_cause_eaten");
                case AttackType.Weapon: return HistoryLocalizationRules.Text("aw_death_cause_weapon");
                case AttackType.Poison: return HistoryLocalizationRules.Text("aw_death_cause_poison");
                case AttackType.Drowning: return HistoryLocalizationRules.Text("aw_death_cause_drowning");
                case AttackType.Water: return HistoryLocalizationRules.Text("aw_death_cause_water");
                case AttackType.Gravity: return HistoryLocalizationRules.Text("aw_death_cause_gravity");
                case AttackType.Explosion: return HistoryLocalizationRules.Text("aw_death_cause_explosion");
                case AttackType.Divine: return HistoryLocalizationRules.Text("aw_death_cause_divine");
                case AttackType.Metamorphosis: return HistoryLocalizationRules.Text("aw_death_cause_metamorphosis");
                case AttackType.Smile: return HistoryLocalizationRules.Text("aw_death_cause_mysterious");
                case AttackType.Other: return HistoryLocalizationRules.Text("aw_death_cause_accident");
                case AttackType.None: return HistoryLocalizationRules.Text("aw_death_cause_natural");
                default: return HistoryLocalizationRules.Text("aw_death_cause_unknown");
            }
        }
    }
}
