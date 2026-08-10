using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.content.policies;
using AncientWarfare3.core.db;
using AncientWarfare3.core.policy;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal sealed class MandateReport
    {
        public bool active;
        public long kingdom_id = -1;
        public long period_id = -1;
        public string kingdom_name = "";
        public string kingdom_color = "";
        public string dynasty_name = "";
        public long emperor_actor_id = -1;
        public string emperor_name = "";
        public int mandate_value;
        public int imperial_authority;
        public int dynasty_prestige;
        public float core_control;
        public float vassal_loyalty;
        public string crisis_level = "";
        public int core_count;
        public int controlled_core_count;
        public int vassal_count;
        public int original_core_count;
        public long rebel_origin_kingdom_id = -1;
        public string rebel_origin_kingdom_name = "";
        public string origin_type = "native";
        public string claimant_kind = "orthodox";
        public string map_marker_kind = "moh";
    }

    internal static class MandateService
    {
        public const string WAR_TIANMING = "tianming";
        public const string WAR_TIANMING_REBEL = "tianmingrebel";
        public const string TRAIT_TIANMING = "天命";

        private const long STATE_ID = 1;
        private const int START_VALUE = 30;
        private const int MAX_VALUE = 100;
        private const int MIN_VALUE = -30;
        private const float RESTORE_CORE_THRESHOLD = 0.65f;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null && LineageArchiveManager.Instance.InitializeSuccessful;

        private static bool _cacheDirty = true;
        private static MandateReport _cachedReport;
        private static long _runtimeMarkerKingdomId = -1L;
        private static string _runtimeMarkerKind = "";
        private static HashSet<long> _coreCityIds = new HashSet<long>();
        private static ColorAsset _coreControlledColor;
        private static ColorAsset _coreVassalColor;
        private static ColorAsset _coreLostColor;
        private static int _autoCandidateYear = int.MinValue;
        private static int _autoCandidateKingdomCount = -1;
        private static long _autoCandidateKingdomId = -1L;
        private static long _pendingFallenMandateKingdomId = -1L;
        private static long _pendingMandateConquerorKingdomId = -1L;
        private static int _lastProjectionResumeYear = int.MinValue;

        public static bool Exists => GetCurrentMandateKingdom() != null;

        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        public static bool IsMandateKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && GetCurrentMandateKingdom()?.id == pKingdom.id;
        }

        public static bool IsMandateKingdomReadOnly(Kingdom pKingdom,
            MandateReport pReport = null)
        {
            MandateReport report = pReport ?? ReadReportReadOnly();
            return pKingdom?.data != null && report.active &&
                   report.kingdom_id == pKingdom.id;
        }

        public static bool IsRuntimeMandateKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && pKingdom.id == _runtimeMarkerKingdomId;
        }

        public static bool TryGetRuntimeMarkerKind(long pKingdomId, out string pMarkerKind)
        {
            pMarkerKind = "";
            if (pKingdomId < 0 || pKingdomId != _runtimeMarkerKingdomId) return false;
            pMarkerKind = _runtimeMarkerKind;
            return true;
        }

        public static void RebuildRuntimeMarkerProjection()
        {
            _cachedReport = null;
            _runtimeMarkerKingdomId = -1L;
            _runtimeMarkerKind = "";
            _coreCityIds = new HashSet<long>();
            _autoCandidateYear = int.MinValue;
            _autoCandidateKingdomCount = -1;
            _autoCandidateKingdomId = -1L;
            _pendingFallenMandateKingdomId = -1L;
            _pendingMandateConquerorKingdomId = -1L;
            _lastProjectionResumeYear = int.MinValue;
            MandateRebelService.ClearRuntime();
            _cacheDirty = true;
            ReadReport();
        }

        public static int ResumePendingProjections(int pMax = 2)
        {
            int limit = Math.Min(2, Math.Max(0, pMax));
            bool worldReady = World.world?.kingdoms != null;
            bool replicaSession =
                AW3MultiplayerReplicaScope.IsReplicaSession;
            if (!MandateProjectionResumeRules.ShouldRun(Ready, worldReady,
                    replicaSession, limit))
                return 0;

            MandateReport report = ReadReport();
            bool resumed = MandateProjectionOutboxPersistence.
                TryResumePendingBatch(DB, limit,
                    (pending, effect) =>
                    {
                        Kingdom kingdom = FindKingdom(pending.KingdomId);
                        bool alive = kingdom?.data != null &&
                                     !kingdom.isRekt();
                        MandateProjectionDisposition disposition =
                            MandateProjectionResumeRules.ResolveDisposition(
                                report.active, report.period_id,
                                report.kingdom_id, pending.PeriodId,
                                pending.KingdomId, alive);
                        long installedActorId =
                            kingdom?.king?.data?.id ?? -1L;
                        long runtimeActorId = MandateProjectionResumeRules.
                            ResolveRuntimeActorId(disposition,
                                installedActorId, pending.RulerActorId);
                        Actor installedKing = runtimeActorId >= 0L
                            ? kingdom?.king
                            : null;
                        return PublishMandateProjectionEffect(effect,
                            kingdom, installedKing, pending, disposition);
                    }, out _, out int completed, out string error);
            if (!resumed && !string.IsNullOrEmpty(error))
                ModClass.LogWarning("Mandate projection resume deferred: " +
                                    error);
            return completed;
        }

        public static void RefreshKingdomNameProjection(Kingdom pKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (!Ready || pKingdom?.data == null || pKingdom.isRekt()) return;
            MandateReport report = ReadReport();
            string kingdomName = pKingdom.name?.Trim() ?? "";
            if (!MandateNameProjectionRules.ShouldRefresh(report.active,
                    report.kingdom_id, pKingdom.id, report.period_id,
                    StateNameRules.IsValid(kingdomName))) return;

            string dynastyName = MakeDynastyName(pKingdom);
            try
            {
                using SQLiteTransaction transaction = DB.BeginTransaction();
                using (var state = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    state.CommandText = "UPDATE " +
                        MandateStateTableItem.GetTableName() +
                        " SET KINGDOM_NAME=@name,DYNASTY_NAME=@dynasty," +
                        "UPDATED_TIME=@time WHERE STATE_ID=@state AND ACTIVE=1 " +
                        "AND KINGDOM_ID=@kingdom";
                    state.Parameters.AddWithValue("@name", kingdomName);
                    state.Parameters.AddWithValue("@dynasty", dynastyName);
                    state.Parameters.AddWithValue("@time", LineageService.CurTime());
                    state.Parameters.AddWithValue("@state", STATE_ID);
                    state.Parameters.AddWithValue("@kingdom", pKingdom.id);
                    if (state.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return;
                    }
                }

                using (var period = new SQLiteCommand(DB) { Transaction = transaction })
                {
                    period.CommandText = "UPDATE " +
                        MandatePeriodTableItem.GetTableName() +
                        " SET KINGDOM_NAME=@name,DYNASTY_NAME=@dynasty " +
                        "WHERE PERIOD_ID=@period AND KINGDOM_ID=@kingdom AND END_TIME=-1";
                    period.Parameters.AddWithValue("@name", kingdomName);
                    period.Parameters.AddWithValue("@dynasty", dynastyName);
                    period.Parameters.AddWithValue("@period", report.period_id);
                    period.Parameters.AddWithValue("@kingdom", pKingdom.id);
                    if (period.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return;
                    }
                }

                transaction.Commit();
                MarkDirty();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Mandate name projection failed: " +
                                    error.Message);
            }
        }

        public static Kingdom GetCurrentMandateKingdom()
        {
            MandateReport report = ReadReport();
            if (!report.active || report.kingdom_id < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(report.kingdom_id);
                if (kingdom?.data != null && !kingdom.isRekt()) return kingdom;
            }
            catch { }

            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == report.kingdom_id && !kingdom.isRekt()) return kingdom;
            return null;
        }

        public static Kingdom GetCurrentMandateKingdomReadOnly(
            MandateReport pReport = null)
        {
            MandateReport report = pReport ?? ReadReportReadOnly();
            if (!report.active || report.kingdom_id < 0 ||
                World.world?.kingdoms == null) return null;
            try
            {
                Kingdom kingdom = World.world.kingdoms.get(report.kingdom_id);
                if (kingdom?.data != null && !kingdom.isRekt()) return kingdom;
            }
            catch { }
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == report.kingdom_id &&
                    !kingdom.isRekt()) return kingdom;
            return null;
        }

        public static MandateReport ReadReport()
        {
            if (!_cacheDirty && _cachedReport != null) return _cachedReport;

            _cachedReport = ReadReportFromDb();
            RebuildCoreCache(_cachedReport.period_id);
            _cacheDirty = false;
            PublishRuntimeMarkerProjection(_cachedReport.active, _cachedReport.kingdom_id,
                _cachedReport.map_marker_kind);
            return _cachedReport;
        }

        public static MandateReport ReadReportReadOnly()
        {
            if (!_cacheDirty && _cachedReport != null) return _cachedReport;
            return ReadReportFromDb();
        }

        public static bool OnRulerSucceeded(Kingdom pKingdom,
            Actor pNewKing)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            if (!Ready || pKingdom?.data == null || pNewKing?.data == null)
                return false;
            MandateReport report = ReadReport();
            long liveKingActorId = pKingdom.king?.data?.id ?? -1L;
            bool refresh = MandateSuccessionRules.ShouldRefreshRulerProjection(
                report.active, report.kingdom_id, pKingdom.id,
                pNewKing.data.id, liveKingActorId);
            if (!refresh) return true;

            long previousActorId = report.emperor_actor_id;
            string persistenceError = "";
            try
            {
                bool committed = MandateSuccessionRules.
                    TryCommitRulerProjection(
                        () => report.emperor_actor_id == pNewKing.data.id ||
                              MandateSuccessionPersistence.TryRefreshRuler(
                                  DB,
                                  MandateStateTableItem.GetTableName(),
                                  STATE_ID, pKingdom.id, report.period_id,
                                  pNewKing.data.id, pNewKing.getName(),
                                  MakeDynastyName(pKingdom),
                                  LineageService.CurTime(),
                                  out persistenceError),
                        () =>
                        {
                            MarkDirty();
                            CommitRulerProjection(pKingdom, pNewKing,
                                previousActorId, refresh);
                        });
                if (!committed)
                    ModClass.LogWarning(
                        "Mandate succession persistence failed: " +
                        persistenceError);
                return committed;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Mandate succession commit failed: " +
                                    error.Message);
                return false;
            }
        }

        private static void CommitRulerProjection(Kingdom pKingdom,
            Actor pNewKing, long pPreviousActorId, bool pRefresh)
        {
            KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor);
            if (MandateSuccessionRules.ShouldTransferRulerTrait(
                    pRefresh, pPreviousActorId, pNewKing.data.id))
            {
                Actor previousRuler = FindActor(pPreviousActorId);
                if (previousRuler?.data != null &&
                    previousRuler.hasTrait(TRAIT_TIANMING))
                    previousRuler.removeTrait(TRAIT_TIANMING);
            }
            if (!pNewKing.hasTrait(TRAIT_TIANMING))
                pNewKing.addTrait(TRAIT_TIANMING);

            RulerAppellationService.RefreshLivingProjection(pKingdom);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.RankOrMandate);
            DirtyAllMaps();
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            TryResumePendingProjectionYear();
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;

            if (!Exists)
            {
                MandatePhaseService.EvaluateVacantWorldYear(
                    ReadReport(), Date.getCurrentYear());
                if (ZhuluWarRules.HasActiveClaimants(
                        MandateRebelService.HasActiveRebelClaimants(),
                        ZhuluWarService.HasActivePrincipalWars())) return;
                TryAutoDeclareMandate(pKingdom);
                return;
            }

            Kingdom mandate = GetCurrentMandateKingdom();
            if (mandate?.data == null) return;
            if (mandate != pKingdom) return;

            int currentYear = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_LAST_YEAR, out int lastYear, int.MinValue);
            if (lastYear == currentYear) return;
            pKingdom.data.set(LineageKeys.MANDATE_LAST_YEAR, currentYear);

            MandateReport before = ReadReport();
            int delta = CalculateYearlyDelta(pKingdom, before);
            int nextValue = Mathf.Clamp(before.mandate_value + delta, MIN_VALUE, MAX_VALUE);
            int authority = CalculateAuthority(pKingdom, nextValue, before.core_control, before.vassal_loyalty);
            int prestige = Mathf.Clamp(before.dynasty_prestige + Mathf.Max(0, delta), 0, 999);
            string crisis = CrisisLevel(nextValue);

            UpdateState(pKingdom, before.period_id, nextValue, authority, prestige, before.core_control,
                before.vassal_loyalty, crisis, currentYear);
            pKingdom.data.set(LineageKeys.MANDATE_VALUE, nextValue);
            pKingdom.data.set(LineageKeys.MANDATE_AUTHORITY, authority);
            pKingdom.data.set(LineageKeys.MANDATE_PRESTIGE, prestige);
            bool chaosCollapse = MandatePhaseService.EvaluateActiveMandateYear(
                ReadReport(), currentYear, nextValue, authority, delta);
            MandateDeclineRebellionService.OnMandateYear(pKingdom,
                nextValue, authority, MandatePhaseService.CatalystScore);

            if (Mathf.Abs(delta) >= 5 || crisis == "collapse" || crisis == "lost")
                RecordEvent("mandate_yearly", pKingdom, pKingdom.king, null, delta, nextValue,
                    pKingdom.name + T("aw_hist_mandate_changed_mid") + Signed(delta) +
                    T("aw_hist_mandate_current") + nextValue);

            if (nextValue <= MIN_VALUE || chaosCollapse)
            {
                MandateProtectionResolution protection = MandatePhaseService.
                    ResolveCollapseProtection(IsHistoricalFigureKing(pKingdom),
                        currentYear);
                if (protection == MandateProtectionResolution.StartGrace)
                {
                    RecordEvent("mandate_protected", pKingdom, pKingdom.king, null, 0, nextValue,
                        pKingdom.king.getName() + T("aw_hist_mandate_protected"));
                }
                else if (protection == MandateProtectionResolution.Collapse)
                {
                    CollapseMandate(pKingdom,
                        nextValue <= MIN_VALUE ? "low_mandate" : "chaos_timeout");
                }
            }
        }

        public static bool TryDeclareMandate(Kingdom pKingdom,
            string pReason = "decision", string pOriginType = "native",
            string pClaimantKind = "orthodox",
            Kingdom pRebelOrigin = null)
        {
            return TryDeclareMandateCore(pKingdom, pReason, pOriginType,
                pClaimantKind, pRebelOrigin, pForceZhuluAge: false);
        }

        public static bool TryForceGrantMandateForZhuluAge(
            Kingdom pTarget, out string pReason)
        {
            pReason = "";
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
            {
                pReason = "replica_read_only";
                return false;
            }
            if (!Ready)
            {
                pReason = "database_not_ready";
                return false;
            }
            if (pTarget?.data == null || pTarget.isRekt() ||
                !pTarget.isCiv() || pTarget.isNeutral())
            {
                pReason = "invalid";
                return false;
            }
            if (!pTarget.hasKing() || pTarget.king?.data == null)
            {
                pReason = "no_king";
                return false;
            }
            if (IsMandateKingdom(pTarget)) return true;

            bool orthodox = XiaizationService.CanUseMandateSystem(pTarget);
            string origin = orthodox ? "zhulu_age" : "pseudo_foreign";
            string claimant = orthodox ? "orthodox" : "foreign_pseudo";
            bool granted = TryDeclareMandateCore(pTarget,
                "zhulu_age_lead", origin, claimant, null,
                pForceZhuluAge: true);
            if (!granted) pReason = "grant_failed";
            return granted;
        }

        private static bool TryDeclareMandateCore(Kingdom pKingdom,
            string pReason, string pOriginType, string pClaimantKind,
            Kingdom pRebelOrigin, bool pForceZhuluAge)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            if (!pForceZhuluAge &&
                ZhuluWarService.HasActivePrincipalWars()) return false;
            if (pKingdom?.data != null)
            {
                pKingdom.data.get(LineageKeys.RESTORATION_COMPLETED,
                    out bool restorationCompleted, false);
                pKingdom.data.get(LineageKeys.RESTORATION_REFUNDER_ELIGIBLE,
                    out bool refounderEligible, false);
                pOriginType = MandateStartRecordRules.ResolveOrigin(
                    pOriginType, restorationCompleted, refounderEligible);
            }
            NormalizeForeignMandateOrigin(pKingdom, pReason, ref pOriginType, ref pClaimantKind);
            if (!Ready) return false;
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                !pKingdom.isCiv() || pKingdom.isNeutral() ||
                !pKingdom.hasKing() || pKingdom.king?.data == null)
                return false;
            MandateReport previousReport = ReadReport();
            string pendingReadError = "";
            MandateProjectionOutboxPersistence.PendingProjection
                existingPending = null;
            bool pendingRead = previousReport.active &&
                               previousReport.kingdom_id == pKingdom.id &&
                               MandateProjectionOutboxPersistence.
                                   TryReadPending(DB,
                                       previousReport.period_id,
                                       out existingPending,
                                       out pendingReadError);
            if (pendingRead && existingPending != null)
            {
                MarkDirty();
                return DrainMandateProjection(
                    pKingdom, existingPending);
            }
            if (!string.IsNullOrEmpty(pendingReadError))
                ModClass.LogWarning("Read pending Mandate projection failed: " +
                                    pendingReadError);
            if (!pForceZhuluAge &&
                !CanDeclareMandateForOrigin(pKingdom, pReason,
                    pOriginType, pClaimantKind, out _)) return false;
            bool hadPreviousMandate = previousReport.period_id >= 0;
            if (!MandateDeclarationRules.CanCreateNewPeriod(
                    previousReport.active, previousReport.kingdom_id, pKingdom.id))
                return false;
            long previousPeriodId = previousReport.active ||
                                    MandateFeudatoryCompletionRules.
                                        ShouldInheritPreviousLegalCores(
                                            hadPreviousMandate, pOriginType)
                ? previousReport.period_id
                : -1L;
            bool replacingActiveMandate = previousReport.active &&
                                           previousReport.kingdom_id !=
                                           pKingdom.id;
            Kingdom previousKingdom = replacingActiveMandate
                ? FindKingdom(previousReport.kingdom_id)
                : null;
            string replacementReason = pReason == "player_grant"
                ? "player_grant_replaced"
                : "replaced";

            long periodId = TableIdAllocator.Next(DB, MandatePeriodTableItem.GetTableName(), "PERIOD_ID");
            double now = LineageService.CurTime();
            Actor king = pKingdom.king;
            HeirService.EnsureLegitimateLine(pKingdom, king);
            string dynastyName = MakeDynastyName(pKingdom);
            bool wasAlreadyEmperor = KingdomTitleService.IsEmperor(pKingdom);
            int currentYear = Date.getCurrentYear();
            string markerKind = MarkerKind(pOriginType, pClaimantKind);
            if (!TryCaptureLegalCoreSnapshots(pKingdom, previousPeriodId,
                    "declaration",
                    out List<MandateProjectionOutboxPersistence.
                        CoreCitySnapshot> coreCitySnapshots))
                return false;
            var request = new MandateDeclarationPersistence.Request
            {
                StateId = STATE_ID,
                PeriodId = periodId,
                KingdomId = pKingdom.id,
                KingdomName = pKingdom.name ?? "",
                KingdomColor = HistoryColors.FromKingdom(pKingdom),
                DynastyName = dynastyName,
                RulerActorId = king?.data?.id ?? -1L,
                RulerName = king?.getName() ?? "",
                StartTime = now,
                CurrentYear = currentYear,
                StartMandate = START_VALUE,
                ImperialAuthority = 45,
                DynastyPrestige = 0,
                CoreControl = 1d,
                VassalLoyalty = 1d,
                CrisisLevel = "stable",
                OriginType = pOriginType ?? "native",
                RebelOriginKingdomId = pRebelOrigin?.id ?? -1L,
                RebelOriginKingdomName = pRebelOrigin?.name ?? "",
                ClaimantKind = pClaimantKind ?? "orthodox",
                MapMarkerKind = markerKind,
                EmperorTitle = (int)KingdomTitle.Emperor,
                ExpectedPreviousActive = replacingActiveMandate,
                PreviousPeriodId = previousPeriodId,
                PreviousKingdomId = replacingActiveMandate
                    ? previousReport.kingdom_id
                    : -1L,
                PreviousKingdomName = replacingActiveMandate
                    ? previousReport.kingdom_name ?? ""
                    : "",
                PreviousKingdomColor = replacingActiveMandate
                    ? previousReport.kingdom_color ?? ""
                    : "",
                PreviousRulerActorId = replacingActiveMandate
                    ? previousReport.emperor_actor_id
                    : -1L,
                PreviousRulerName = replacingActiveMandate
                    ? previousReport.emperor_name ?? ""
                    : "",
                PreviousMandateValue = previousReport.mandate_value,
                PreviousEndReason = replacementReason,
                NewYearPrefix = HistoryWriter.BuildYearPrefix(now,
                    pKingdom),
                NewYearPrefixRich = HistoryWriter.BuildYearPrefixRich(now,
                    pKingdom),
                PreviousYearPrefix = HistoryWriter.BuildYearPrefix(now,
                    previousKingdom),
                PreviousYearPrefixRich = HistoryWriter.BuildYearPrefixRich(
                    now, previousKingdom),
                OperationKey = "mandate-declare:" + periodId,
                WasAlreadyEmperor = wasAlreadyEmperor,
                CoreSnapshotSource = "declaration",
                CoreCitySnapshots = coreCitySnapshots
            };
            string persistenceError = "";
            bool committed = MandateDeclarationPersistence.TryCommit(
                DB, MandatePeriodTableItem.GetTableName(),
                MandateStateTableItem.GetTableName(),
                KingdomReignTableItem.GetTableName(), request,
                out persistenceError);
            if (!committed)
            {
                ModClass.LogWarning("Mandate declaration persistence failed: " +
                                    persistenceError);
                return false;
            }
            MarkDirty();
            if (!MandateProjectionOutboxPersistence.TryReadPending(
                    DB, periodId,
                    out MandateProjectionOutboxPersistence.PendingProjection
                        pending,
                    out persistenceError) || pending == null)
            {
                ModClass.LogWarning("Mandate projection outbox missing: " +
                                    persistenceError);
                return false;
            }
            return DrainMandateProjection(pKingdom, pending);
        }

        private static bool DrainMandateProjection(Kingdom pKingdom,
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            if (!MandateProjectionResumeRules.CanMutateOutbox(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            if (pPending == null) return false;
            MandateReport report = ReadReport();
            bool alive = pKingdom?.data != null && !pKingdom.isRekt() &&
                         pKingdom.id == pPending.KingdomId;
            MandateProjectionDisposition disposition =
                MandateProjectionResumeRules.ResolveDisposition(
                    report.active, report.period_id, report.kingdom_id,
                    pPending.PeriodId, pPending.KingdomId, alive);
            long installedActorId = pKingdom?.king?.data?.id ?? -1L;
            long runtimeActorId = MandateProjectionResumeRules.
                ResolveRuntimeActorId(disposition, installedActorId,
                    pPending.RulerActorId);
            Actor installedKing = runtimeActorId >= 0L
                ? pKingdom?.king
                : null;
            bool drained = MandateProjectionOutboxPersistence.TryDrain(
                DB, pPending.OperationKey,
                pEffect => PublishMandateProjectionEffect(
                    pEffect, pKingdom, installedKing, pPending,
                    disposition),
                out bool complete, out string error);
            if (!drained)
                ModClass.LogWarning("Mandate projection drain failed: " +
                                    error);
            return drained && complete;
        }

        private static bool PublishMandateProjectionEffect(string pEffect,
            Kingdom pKingdom, Actor pKing,
            MandateProjectionOutboxPersistence.PendingProjection pPending,
            MandateProjectionDisposition pDisposition)
        {
            if (!MandateProjectionResumeRules.ShouldPublishEffect(
                    pDisposition, pEffect))
                return true;
            Kingdom previous = pPending.OldEndRequired
                ? FindKingdom(pPending.PreviousKingdomId)
                : null;
            switch (pEffect)
            {
                case "old_runtime":
                    PublishMandateEndedRuntime(previous);
                    return true;
                case "old_revision":
                    FamilyTreeProjectionRevision.Advance(
                        FamilyTreeProjectionChange.RankOrMandate);
                    return true;
                case "old_kingdom_history":
                    return TryPublishMandateEndHistorySnapshot(pPending);
                case "old_mandate_event":
                    return TryPublishMandateEndEvent(pPending);
                case "new_runtime":
                    if (pKing?.data == null) return false;
                    PublishDeclaredMandateRuntime(
                        pKingdom, pKing, pPending);
                    return true;
                case "new_revision":
                    FamilyTreeProjectionRevision.Advance(
                        FamilyTreeProjectionChange.RankOrMandate);
                    return true;
                case "new_mandate_event":
                    return TryPublishMandateStartEvent(pPending);
                case "new_kingdom_history":
                    return TryPublishMandateStartKingdomHistory(pPending);
                case "new_person_history":
                    return TryPublishMandateStartPersonHistory(pPending);
                case "legal_cores":
                    MandateLegalCoreReplayDisposition coreReplay =
                        MandateProjectionResumeRules.ResolveLegalCoreReplay(
                            pDisposition, pPending.CoreSnapshotSource);
                    if (coreReplay ==
                        MandateLegalCoreReplayDisposition.Skip)
                        return true;
                    if (coreReplay == MandateLegalCoreReplayDisposition.
                            CaptureLegacySnapshot &&
                        !EnsurePendingCoreSnapshots(pKingdom, pPending))
                        return false;
                    return CreateLegalCores(pPending.PeriodId,
                        pPending.CoreCitySnapshots,
                        pPending.OperationKey + ":legal_cores:",
                        requireCurrentStateUpdate:
                            pDisposition ==
                            MandateProjectionDisposition.Current);
                case "new_maps":
                    DirtyAllMaps();
                    ClearPendingMandateConqueror();
                    return true;
                default:
                    return false;
            }
        }

        private static void TryResumePendingProjectionYear()
        {
            bool replicaSession =
                AW3MultiplayerReplicaScope.IsReplicaSession;
            if (!Ready || World.world?.kingdoms == null || replicaSession)
                return;
            int currentYear = Date.getCurrentYear();
            if (!MandateProjectionResumeRules.ShouldStartAnnualCycle(
                    _lastProjectionResumeYear, currentYear,
                    replicaSession))
                return;
            _lastProjectionResumeYear = currentYear;
            ResumePendingProjections(2);
        }

        private static void PublishDeclaredMandateRuntime(Kingdom pKingdom,
            Actor pKing,
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            string originType = pPending.OriginType ?? "native";
            string claimantKind = pPending.ClaimantKind ?? "orthodox";
            string markerKind = pPending.MapMarkerKind ??
                                MarkerKind(originType, claimantKind);
            pKingdom.data.set(LineageKeys.MANDATE_PERIOD_ID,
                pPending.PeriodId);
            pKingdom.data.set(LineageKeys.MANDATE_VALUE, START_VALUE);
            pKingdom.data.set(LineageKeys.MANDATE_AUTHORITY, 45);
            pKingdom.data.set(LineageKeys.MANDATE_ORIGIN_TYPE,
                originType);
            pKingdom.data.set(LineageKeys.MANDATE_CLAIMANT_KIND,
                claimantKind);
            pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND,
                markerKind);
            PublishRuntimeMarkerProjection(true, pKingdom.id, markerKind);
            MarkDirty();
            MandatePhaseService.OnMandateEstablished(
                pPending.PreviousPeriodId >= 0L, pPending.CurrentYear);
            if (originType == "self_restoration" ||
                originType == MandateFeudatoryCompletionRules.
                    RestorationOrigin)
            {
                pKingdom.data.set(
                    LineageKeys.RESTORATION_REFUNDER_ELIGIBLE, false);
                RulerTitleRestorationStateService.MarkMandateRegained(
                    pKingdom);
            }
            if (originType == "pseudo_foreign" ||
                claimantKind == "foreign_pseudo")
                XiaizationService.OnPseudoMandateDeclared(pKingdom);
            KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            if (pKing != null && !pKing.hasTrait(TRAIT_TIANMING))
                pKing.addTrait(TRAIT_TIANMING);
            if (pPending.WasAlreadyEmperor &&
                (pPending.PreviousPeriodId >= 0L ||
                 originType == "self_restoration" ||
                 originType == MandateFeudatoryCompletionRules.
                     RestorationOrigin))
                EraChangeTriggerService.Mark(pKingdom,
                    EraChangeReason.RestoredMandate,
                    "mandate:" + pPending.PeriodId);
        }

        private static bool TryPublishMandateStartEvent(
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            string startEventType = MandateStartRecordRules.EventType(
                pPending.OriginType, pPending.ClaimantKind);
            return RecordSnapshotEvent(startEventType, pPending.PeriodId,
                pPending.KingdomId, pPending.KingdomName,
                pPending.KingdomColor, pPending.RulerActorId,
                pPending.RulerName, pPending.CreatedTime,
                pPending.NewYearPrefix, 0, START_VALUE, 45,
                pPending.KingdomName +
                T("aw_hist_edict_mandate_claimed_mid") +
                pPending.DynastyName +
                T("aw_hist_edict_mandate_claimed_suffix"),
                pPending.OperationKey + ":new_mandate_event");
        }

        private static bool TryPublishMandateStartKingdomHistory(
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            string startEventType = MandateStartRecordRules.EventType(
                pPending.OriginType, pPending.ClaimantKind);
            return HistoryWriter.TryRecordKingdomSnapshot(
                pPending.CreatedTime, pPending.NewYearPrefix,
                pPending.NewYearPrefixRich, pPending.KingdomId,
                pPending.KingdomName, pPending.KingdomColor,
                startEventType,
                SnapshotReference(pPending.KingdomName,
                    pPending.KingdomColor, "kingdom", pPending.KingdomId) +
                H("aw_hist_edict_mandate_claimed_mid") +
                HistoryText.PlainText(pPending.DynastyName) +
                H("aw_hist_edict_mandate_claimed_suffix"),
                HistoryTarget.From("kingdom", pPending.KingdomId),
                pPending.OperationKey + ":new_kingdom_history");
        }

        private static bool TryPublishMandateStartPersonHistory(
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            string startEventType = MandateStartRecordRules.EventType(
                pPending.OriginType, pPending.ClaimantKind);
            return HistoryWriter.TryRecordPersonSnapshot(
                pPending.CreatedTime, pPending.NewYearPrefix,
                pPending.NewYearPrefixRich, pPending.KingdomId,
                pPending.KingdomName, pPending.KingdomColor,
                pPending.RulerActorId, pPending.RulerName, startEventType,
                SnapshotReference(pPending.RulerName,
                    pPending.KingdomColor, "actor",
                    pPending.RulerActorId) +
                H("aw_hist_edict_actor_claimed_mandate"),
                ChronicleCategory.HONOR,
                HistoryTarget.From("kingdom", pPending.KingdomId),
                pPending.OperationKey + ":new_person_history");
        }

        private static HistoryText SnapshotReference(string pName,
            string pColor, string pTargetType, long pTargetId)
        {
            return HistoryText.Reference(pName ?? "", pColor,
                pTargetType, pTargetId);
        }

        public static bool TryGrantMandateByPlayer(Kingdom pTarget, out string pReason)
        {
            pReason = "";
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
            {
                pReason = "replica_read_only";
                return false;
            }
            if (ZhuluWarService.HasActivePrincipalWars())
            {
                pReason = "zhulu_unresolved";
                return false;
            }
            bool validTarget = pTarget?.data != null && !pTarget.isRekt() &&
                               pTarget.isCiv() && !pTarget.isNeutral();
            if (!Ready)
            {
                pReason = "database_not_ready";
                return false;
            }

            Kingdom current = GetCurrentMandateKingdom();
            bool hadPreviousMandate = ReadReport()?.period_id >= 0L;
            if (!MandateDeclarationRules.CanPlayerGrant(
                    validTarget,
                    pTarget?.king?.data != null && pTarget.hasKing(),
                    current?.id == pTarget?.id,
                    out pReason))
                return false;

            if (TryDeclareMandate(pTarget, "player_grant", "player_grant", "player_grant"))
            {
                MandatePhaseService.OnMandateEstablished(
                    hadPreviousMandate, Date.getCurrentYear());
                return true;
            }

            pReason = "grant_failed";
            return false;
        }

        public static bool CanDeclareMandate(Kingdom pKingdom, out string pReason)
        {
            return CanDeclareMandateForSource(pKingdom,
                MandateDeclarationSource.Ordinary, out pReason);
        }

        private static bool CanDeclareMandateForSource(Kingdom pKingdom,
            MandateDeclarationSource pSource, out string pReason)
        {
            pReason = "";
            if (ZhuluWarService.HasActivePrincipalWars())
            {
                pReason = "zhulu_unresolved";
                return false;
            }
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral())
            {
                pReason = "invalid";
                return false;
            }
            if (!pKingdom.hasKing() || pKingdom.king?.data == null)
            {
                pReason = "no_king";
                return false;
            }
            MandateReport last = ReadReport();
            if (last.active)
            {
                pReason = "already_exists";
                return false;
            }
            if (!MandateRitesService.CanDeclare(pKingdom, pSource, out pReason))
                return false;
            if (VassalService.IsVassalKingdom(pKingdom))
            {
                pReason = "vassal";
                return false;
            }
            if (!IsSupportedKingdom(pKingdom))
            {
                pReason = "unsupported";
                return false;
            }

            bool historicalFigure = IsHistoricalFigureKing(pKingdom);
            KingdomTitle title = KingdomTitleService.GetTitle(pKingdom);
            int cityCount = CountCities(pKingdom);
            if (!MandateDeclarationRules.HasEnoughRealmToDeclare(cityCount, (int)title,
                    historicalFigure, 4, (int)KingdomTitle.King))
            {
                pReason = "too_small";
                return false;
            }

            if (MandateDeclarationRules.NeedsLegalCoreControl(last.core_count, last.active) &&
                !MandateDeclarationRules.HasEnoughLegalCoreControl(
                    GetCoreControlRatio(pKingdom, last.period_id), RESTORE_CORE_THRESHOLD))
            {
                pReason = "core_control";
                return false;
            }

            if (!IsMostPowerfulIndependent(pKingdom))
            {
                pReason = "not_strongest";
                return false;
            }

            return true;
        }

        private static bool IsHistoricalFigureKing(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            return king?.data != null && (king.hasTrait("first") || king.hasTrait("figure"));
        }

        private static bool CanDeclareMandateForOrigin(Kingdom pKingdom, string pDeclarationReason,
            string pOriginType, string pClaimantKind, out string pReason)
        {
            if (ZhuluWarService.HasActivePrincipalWars())
            {
                pReason = "zhulu_unresolved";
                return false;
            }
            MandateDeclarationSource source = MandateRitesRules.ResolveSource(
                pDeclarationReason, pOriginType, pClaimantKind);
            bool rebelOrigin = source == MandateDeclarationSource.MandateRebel;
            bool foreignPseudo = source == MandateDeclarationSource.ForeignPseudoDynasty;
            bool successfulOrdinaryWar = source == MandateDeclarationSource.MandateWarVictory;
            bool successfulDynasticRestoration =
                source == MandateDeclarationSource.FeudatoryRestoration;
            if (source == MandateDeclarationSource.PlayerGrant)
            {
                Kingdom current = GetCurrentMandateKingdom();
                return MandateDeclarationRules.CanPlayerGrant(
                    pKingdom?.data != null && !pKingdom.isRekt() &&
                    pKingdom.isCiv() && !pKingdom.isNeutral(),
                    pKingdom?.king?.data != null && pKingdom.hasKing(),
                    current?.id == pKingdom?.id,
                    out pReason);
            }
            if (successfulOrdinaryWar || successfulDynasticRestoration)
            {
                pReason = "";
                if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral())
                {
                    pReason = "invalid";
                    return false;
                }
                if (!pKingdom.hasKing())
                {
                    pReason = "no_king";
                    return false;
                }
                if (Exists)
                {
                    pReason = "already_exists";
                    return false;
                }
                return true;
            }
            if (rebelOrigin || foreignPseudo)
            {
                pReason = "";
                if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral())
                {
                    pReason = "invalid";
                    return false;
                }
                if (!pKingdom.hasKing())
                {
                    pReason = "no_king";
                    return false;
                }
                bool mandateExists = Exists;
                bool hasEnoughCore = MandateDeclarationRules.HasEnoughLegalCoreControl(
                    GetCoreControlRatioFor(pKingdom), RESTORE_CORE_THRESHOLD);

                if (foreignPseudo)
                {
                    return MandateDeclarationRules.CanDeclareForeignPseudo(
                        LineageService.IsXiaKingdom(pKingdom),
                        IsMandateWarDeclarationReason(pDeclarationReason),
                        hasEnoughCore,
                        mandateExists,
                        out pReason);
                }

                if (mandateExists)
                {
                    pReason = "already_exists";
                    return false;
                }
                if (!hasEnoughCore)
                {
                    pReason = "core_control";
                    return false;
                }
                return true;
            }

            return CanDeclareMandateForSource(pKingdom, source, out pReason);
        }

        private static bool IsMandateWarDeclarationReason(string pReason)
        {
            return pReason == "pseudo_foreign_war" ||
                   pReason == "tianming_war" ||
                   pReason == "tianmingrebel_war";
        }

        private static void NormalizeForeignMandateOrigin(Kingdom pKingdom, string pReason,
            ref string pOriginType, ref string pClaimantKind)
        {
            if (pKingdom?.data == null || LineageService.IsXiaKingdom(pKingdom)) return;
            if (!IsMandateWarDeclarationReason(pReason)) return;
            if (pOriginType == "rebel" || pClaimantKind == "rebel") return;
            pOriginType = "pseudo_foreign";
            pClaimantKind = "foreign_pseudo";
        }

        public static bool CanStabilizeMandate(Kingdom pKingdom)
        {
            Kingdom mandate = GetCurrentMandateKingdom();
            return mandate?.data != null && pKingdom?.data != null && mandate == pKingdom;
        }

        public static bool ApplySacrificeOutcome(Kingdom pKingdom,
            MandateSacrificeEffects pEffects, string pReason)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            return ApplySacrificeOutcome(pKingdom, pEffects, pReason, null);
        }

        internal static bool ApplySacrificeOutcome(Kingdom pKingdom,
            MandateSacrificeEffects pEffects, string pReason, string pContent)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            if (!CanStabilizeMandate(pKingdom)) return false;
            string eventType = pReason ?? "mandate_sacrifice";
            ChangeMandate(pKingdom, pEffects.MandateDelta,
                eventType, pContent, pRecordEvent: false);
            MandateReport r = ReadReport();
            int authority = Mathf.Clamp(
                r.imperial_authority + pEffects.AuthorityDelta, 0, 100);
            int prestige = Mathf.Clamp(
                r.dynasty_prestige + pEffects.PrestigeDelta, 0, 999);
            UpdateState(pKingdom, r.period_id, r.mandate_value, authority, prestige,
                r.core_control, r.vassal_loyalty, CrisisLevel(r.mandate_value), Date.getCurrentYear());
            pKingdom.data.set(LineageKeys.MANDATE_VALUE, r.mandate_value);
            pKingdom.data.set(LineageKeys.MANDATE_AUTHORITY, authority);
            pKingdom.data.set(LineageKeys.MANDATE_PRESTIGE, prestige);
            string content = string.IsNullOrEmpty(pContent)
                ? pKingdom.name + T("aw_hist_mandate_changed_mid") +
                  Signed(pEffects.MandateDelta) +
                  T("aw_hist_mandate_current") + r.mandate_value
                : pContent;
            RecordEvent(eventType, pKingdom, pKingdom.king, null,
                pEffects.MandateDelta, r.mandate_value, content);
            return true;
        }

        public static bool HasMandateProtection(Kingdom pKingdom)
        {
            return pKingdom?.data != null && IsMandateKingdom(pKingdom) &&
                   MandatePhaseService.IsCollapseProtectionActive(
                       Date.getCurrentYear());
        }

        public static bool ShouldBlockPeacefulFellApart(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || !IsMandateKingdom(pKingdom)) return false;
            MandateReport report = ReadReport();
            bool hasCandidate = HeirService.HasSuccessionCandidate(pKingdom);
            return MandateSuccessionRules.ShouldBlockPeacefulFellApart(
                pIsActiveMandate: report.active && report.kingdom_id == pKingdom.id,
                pMandateValue: report.mandate_value,
                pCrisisLevel: report.crisis_level,
                pHasSuccessionCandidate: hasCandidate);
        }

        public static void OnPeacefulFellApartBlocked(Kingdom pKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pKingdom?.data == null || !IsMandateKingdom(pKingdom)) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_SUCCESSION_CRISIS_YEAR, out int lastYear, int.MinValue);
            if (!MandateSuccessionRules.ShouldRecordSuccessionCrisis(lastYear, year)) return;

            pKingdom.data.set(LineageKeys.MANDATE_SUCCESSION_CRISIS_YEAR, year);
            ChangeMandate(pKingdom, -4, "mandate_succession_crisis",
                (pKingdom.name ?? "") + T("aw_hist_mandate_succession_unstable"));
            HistoryWriter.RecordKingdom(pKingdom, "mandate_succession_crisis",
                HistoryText.Kingdom(pKingdom) + H("aw_hist_mandate_succession_damaged"),
                HistoryTarget.Kingdom(pKingdom));
        }

        public static long GetCurrentPeriodId()
        {
            return ReadReport().period_id;
        }

        public static bool IsLegalCoreCity(City pCity)
        {
            if (pCity?.data == null) return false;
            ReadReport();
            return _coreCityIds.Contains(pCity.id);
        }

        public static void OnCityTransferred(City pCity, Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pCity?.data == null || pCity.isRekt()) return;
            if (_cacheDirty || _cachedReport == null) return;
            if (!MandateCoreTransferRules.ShouldInvalidate(
                    _cachedReport.period_id >= 0, _coreCityIds.Contains(pCity.id))) return;

            MarkDirty();
            MandateCoreMapModeService.DirtyMapIfActive();
        }

        public static void OnCityTransferStarting(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pCity?.data == null || pCity.kingdom != pOldKingdom) return;
            ApplyImmediateCoreCityLoss(pCity, pOldKingdom, pNewKingdom);
            TrackHostileMandateFinalCityConqueror(pOldKingdom,
                pNewKingdom);
        }

        private static void ApplyImmediateCoreCityLoss(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            MandateReport report = ReadReport();
            bool ownerChanged = pOldKingdom?.data != null &&
                                pNewKingdom != pOldKingdom;
            if (!MandateCoreTransferRules.ShouldApplyMandateLoss(
                    report.period_id >= 0,
                    _coreCityIds.Contains(pCity.id),
                    report.active && report.kingdom_id == pOldKingdom?.id,
                    ownerChanged)) return;

            int year = Date.getCurrentYear();
            pOldKingdom.data.get(LineageKeys.MANDATE_CITY_LOSS_YEAR,
                out int lossYear, int.MinValue);
            pOldKingdom.data.get(
                LineageKeys.MANDATE_CITY_LOSS_ACCUMULATED,
                out int accumulatedLoss, 0);
            if (lossYear != year) accumulatedLoss = 0;

            bool capital = pOldKingdom.capital == pCity;
            int requestedDelta = MandateDeclineRules.CityTransferDelta(capital);
            int allowedDelta = MandateCoreTransferRules.
                AllowedAnnualLossDelta(accumulatedLoss, requestedDelta);
            if (allowedDelta == 0) return;

            pOldKingdom.data.set(LineageKeys.MANDATE_CITY_LOSS_YEAR, year);
            pOldKingdom.data.set(
                LineageKeys.MANDATE_CITY_LOSS_ACCUMULATED,
                accumulatedLoss + allowedDelta);
            ChangeMandate(pOldKingdom, allowedDelta,
                capital ? "mandate_capital_lost" : "mandate_core_city_lost");
        }

        private static void TrackHostileMandateFinalCityConqueror(
            Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            if (pOldKingdom?.data == null ||
                pOldKingdom.id != _runtimeMarkerKingdomId) return;

            int losingCityCount;
            try { losingCityCount = pOldKingdom.countCities(); }
            catch { losingCityCount = pOldKingdom.hasCities() ? 2 : 0; }

            bool gainingValid = pNewKingdom?.data != null &&
                                !pNewKingdom.isRekt() &&
                                pNewKingdom.isCiv() &&
                                !pNewKingdom.isNeutral();
            bool hostile = false;
            if (gainingValid)
            {
                try { hostile = pNewKingdom.isEnemy(pOldKingdom); }
                catch { hostile = false; }
            }

            _pendingFallenMandateKingdomId = pOldKingdom.id;
            _pendingMandateConquerorKingdomId =
                MandateDeclarationRules.ResolveHostileMandateFinalCityConqueror(
                    mandateActive: true,
                    mandateKingdomId: _runtimeMarkerKingdomId,
                    losingKingdomId: pOldKingdom.id,
                    gainingKingdomId: pNewKingdom?.id ?? -1L,
                    gainingKingdomValid: gainingValid,
                    hostileTransfer: hostile,
                    losingCityCountBeforeTransfer: losingCityCount);
        }

        public static void OnKingdomCoreCreated(Kingdom pKingdom, City pCity, string pSourceType)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (!Ready || pKingdom?.data == null || pCity?.data == null) return;
            MandateReport report = ReadReport();
            bool isActiveMandateKingdom = report.active && report.kingdom_id == pKingdom.id && report.period_id >= 0;
            bool alreadyLegal = _coreCityIds.Contains(pCity.id);
            if (!MandateCoreMapRules.ShouldAddNewKingdomCoreToMandateLegalCore(isActiveMandateKingdom, alreadyLegal))
                return;

            long coreId = TableIdAllocator.Next(DB, MandateCoreCityTableItem.GetTableName(), "CORE_ID");
            try
            {
                DB.Insert(MandateCoreCityTableItem.GetTableName(),
                    ColumnVal.Create("CORE_ID", coreId),
                    ColumnVal.Create("PERIOD_ID", report.period_id),
                    ColumnVal.Create("CITY_ID", pCity.id),
                    ColumnVal.Create("CITY_NAME", pCity.data.name ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_ID", pKingdom.id),
                    ColumnVal.Create("ORIGINAL_KINGDOM_NAME", pKingdom.name ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("CORE_TYPE", string.IsNullOrEmpty(pSourceType) ? "expanded" : pSourceType),
                    ColumnVal.Create("ADDED_TIME", LineageService.CurTime()),
                    ColumnVal.Create("ACTIVE", 1));

                _coreCityIds.Add(pCity.id);
                UpdateOriginalCoreCount(report.period_id);
                RecordEvent("mandate_core_added", pKingdom, pKingdom.king, pCity, 0, report.mandate_value,
                    (pCity.data.name ?? "") + T("aw_hist_mandate_core_added"));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate legal core sync failed: " + e.Message);
            }
        }

        public static float GetCoreControlRatioFor(Kingdom pKingdom)
        {
            MandateReport report = ReadReport();
            if (report.period_id < 0) return 0f;
            return GetCoreControlRatio(pKingdom, report.period_id);
        }

        public static List<long> GetCurrentCoreCityIds()
        {
            MandateReport report = ReadReport();
            return ReadCoreCityIds(report.period_id).ToList();
        }

        public static void ClearMandate(string pReason)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            Kingdom current = GetCurrentMandateKingdom();
            MandateReport report = ReadReport();
            if (!Ready || !report.active) return;

            double now = LineageService.CurTime();
            DB.UpdateValue(MandatePeriodTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("PERIOD_ID", report.period_id) },
                ColumnVal.Create("END_TIME", now),
                ColumnVal.Create("END_REASON", pReason ?? "ended"),
                ColumnVal.Create("END_MANDATE", report.mandate_value));

            DB.UpdateValue(MandateStateTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID) },
                ColumnVal.Create("ACTIVE", 0),
                ColumnVal.Create("UPDATED_TIME", now),
                ColumnVal.Create("CRISIS_LEVEL", "ended"));

            PublishMandateEnded(current, report, pReason);
        }

        private static void PublishMandateEnded(Kingdom pCurrent,
            MandateReport pReport, string pReason)
        {
            PublishMandateEndedRuntime(pCurrent);
            FamilyTreeProjectionRevision.Advance(
                FamilyTreeProjectionChange.RankOrMandate);
            TryPublishMandateEndHistory(pCurrent, pReason);
            RecordEvent("mandate_end", pCurrent, pCurrent?.king, null, 0,
                pReport.mandate_value,
                (pCurrent?.name ?? "") +
                T("aw_hist_edict_mandate_lost_prefix") +
                EndReasonLabel(pReason) +
                T("aw_hist_edict_mandate_lost_suffix"),
                pReport.period_id);
            MarkDirty();
            DirtyAllMaps();
            ClearPendingMandateConqueror();
        }

        private static void PublishMandateEndedRuntime(Kingdom pCurrent)
        {
            RulerTitleRestorationStateService.MarkMandateLost(pCurrent);
            PublishRuntimeMarkerProjection(false, -1L, "");
            MandateMilitaryPhaseService.OnMandateEnded(pCurrent);

            if (pCurrent?.king != null &&
                pCurrent.king.hasTrait(TRAIT_TIANMING))
                pCurrent.king.removeTrait(TRAIT_TIANMING);

            RulerAppellationService.RefreshLivingProjection(pCurrent);
        }

        private static bool TryPublishMandateEndHistory(Kingdom pCurrent,
            string pReason, string pProjectionKey = "")
        {
            if (pCurrent?.data == null) return true;
            return HistoryWriter.TryRecordKingdom(pCurrent, "mandate_end",
                HistoryText.Kingdom(pCurrent) +
                H("aw_hist_edict_mandate_lost_prefix") +
                HistoryText.PlainText(EndReasonLabel(pReason)) +
                H("aw_hist_edict_mandate_lost_suffix"),
                HistoryTarget.Kingdom(pCurrent), pProjectionKey);
        }

        private static bool TryPublishMandateEndHistorySnapshot(
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            return HistoryWriter.TryRecordKingdomSnapshot(
                pPending.CreatedTime, pPending.PreviousYearPrefix,
                pPending.PreviousYearPrefixRich,
                pPending.PreviousKingdomId,
                pPending.PreviousKingdomName,
                pPending.PreviousKingdomColor, "mandate_end",
                SnapshotReference(pPending.PreviousKingdomName,
                    pPending.PreviousKingdomColor, "kingdom",
                    pPending.PreviousKingdomId) +
                H("aw_hist_edict_mandate_lost_prefix") +
                HistoryText.PlainText(
                    EndReasonLabel(pPending.PreviousEndReason)) +
                H("aw_hist_edict_mandate_lost_suffix"),
                HistoryTarget.From("kingdom",
                    pPending.PreviousKingdomId),
                pPending.OperationKey + ":old_kingdom_history");
        }

        private static bool TryPublishMandateEndEvent(
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            return RecordSnapshotEvent("mandate_end",
                pPending.PreviousPeriodId, pPending.PreviousKingdomId,
                pPending.PreviousKingdomName,
                pPending.PreviousKingdomColor,
                pPending.PreviousRulerActorId,
                pPending.PreviousRulerName, pPending.CreatedTime,
                pPending.PreviousYearPrefix, 0,
                pPending.PreviousMandateValue, 0,
                pPending.PreviousKingdomName +
                T("aw_hist_edict_mandate_lost_prefix") +
                EndReasonLabel(pPending.PreviousEndReason) +
                T("aw_hist_edict_mandate_lost_suffix"),
                pPending.OperationKey + ":old_mandate_event");
        }

        public static void CollapseMandate(Kingdom pKingdom, string pReason)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pKingdom?.data == null) return;
            MandatePhaseService.ForceChaos("mandate_collapse");
            HistoryWriter.RecordKingdom(pKingdom, "mandate_collapse",
                HistoryText.Kingdom(pKingdom) + H("aw_hist_edict_mandate_collapse"),
                HistoryTarget.Kingdom(pKingdom));
            FeudatoryCollapseService.ScheduleOnMandateCollapse(pKingdom);
            MandateRebelService.OnMandateCollapse(pKingdom, pReason);
            ClearMandate(pReason);
        }

        public static void OnWarStarted(War pWar)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pWar?.data == null) return;
            MandateBorderDefenseService.OnMandateWarStarted(pWar);
            string type = GetWarType(pWar);
            if (type != WAR_TIANMING && type != WAR_TIANMING_REBEL) return;

            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;
            RecordEvent("mandate_war_start", defender, defender.king, null, -5, ReadReport().mandate_value,
                attacker.name + T("aw_hist_mandate_war_declared_mid") + defender.name +
                T("aw_hist_mandate_war_declared_suffix"));
            ChangeMandate(defender, -5, "mandate_war_start");
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);

            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            Kingdom mandate = GetCurrentMandateKingdom();
            if (attacker?.data == null || defender?.data == null || mandate?.data == null) return;

            bool mandateWar = type == WAR_TIANMING ||
                              type == WAR_TIANMING_REBEL;
            if (!mandateWar)
            {
                if (defender == mandate && pWinner == WarWinner.Attackers)
                {
                    ChangeMandate(defender, ReadOrdinaryWarDefeatDelta(pWar,
                        defender), "mandate_war_lost");
                }
                return;
            }

            if (defender == mandate && pWinner == WarWinner.Attackers)
            {
                bool rebel = MandateRebelService.IsRebelKingdom(attacker) || type == WAR_TIANMING_REBEL;
                bool pseudo = !LineageService.IsXiaKingdom(attacker) || IsPseudoForeignClaimant(attacker);
                ClearMandate("war_lost");
                if (rebel)
                    TryDeclareMandate(attacker, "tianmingrebel_war", "rebel", "rebel", defender);
                else if (pseudo)
                    TryDeclareMandate(attacker, "pseudo_foreign_war", "pseudo_foreign", "foreign_pseudo", defender);
                else
                    TryDeclareMandate(attacker, "tianming_war");
                return;
            }

            if (defender == mandate && pWinner == WarWinner.Defenders)
                ChangeMandate(defender, 12, "mandate_war_won");
        }

        private static int ReadOrdinaryWarDefeatDelta(War pWar,
            Kingdom pDefender)
        {
            bool halfLoss = false;
            bool totalLoss = false;
            try
            {
                if (WarScoreRuntimeBridge.TryGetSnapshot(pWar, pDefender,
                        out WarScoreSnapshot snapshot))
                {
                    int baseline = snapshot.DefenderMobilizationBaseline;
                    int losses = snapshot.DefenderLosses;
                    if (baseline > 0)
                    {
                        totalLoss = losses >= baseline;
                        halfLoss = losses * 2 >= baseline;
                    }
                }
            }
            catch { }
            return MandateDeclineRules.WarDefeatDelta(halfLoss, totalLoss);
        }

        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pKingdom?.data == null) return;
            MandateReport report = ReadReport();
            if (MandateDeclarationRules.ShouldEndDestroyedMandate(
                    report.active, report.kingdom_id, pKingdom.id))
            {
                long candidateId = _pendingFallenMandateKingdomId == pKingdom.id
                    ? _pendingMandateConquerorKingdomId
                    : -1L;
                Kingdom candidate = FindKingdom(candidateId);
                bool candidateValid = candidate?.data != null &&
                                      !candidate.isRekt() &&
                                      candidate.isCiv() &&
                                      !candidate.isNeutral();
                bool transfer = MandateDeclarationRules.CanTransferDestroyedMandate(
                    report.active, report.kingdom_id, pKingdom.id, candidateId,
                    candidateValid, candidateValid && candidate.hasKing() &&
                                    candidate.king?.data != null);
                RulerTitleRestorationStateService.MarkMandateLost(pKingdom);
                MandatePhaseService.ForceChaos("mandate_kingdom_fell");
                ClearMandate("kingdom_fell");
                if (transfer) TryDeclareMandateAfterVictory(candidate, pKingdom);
            }
        }

        private static void TryDeclareMandateAfterVictory(Kingdom pVictor,
            Kingdom pFormerMandate)
        {
            if (pVictor?.data == null) return;
            bool rebel = MandateRebelService.IsRebelKingdom(pVictor);
            bool pseudo = !LineageService.IsXiaKingdom(pVictor) ||
                          IsPseudoForeignClaimant(pVictor);
            if (rebel)
                TryDeclareMandate(pVictor, "tianmingrebel_war",
                    "rebel", "rebel", pFormerMandate);
            else if (pseudo)
                TryDeclareMandate(pVictor, "pseudo_foreign_war",
                    "pseudo_foreign", "foreign_pseudo", pFormerMandate);
            else
                TryDeclareMandate(pVictor, "tianming_war");
        }

        private static void ClearPendingMandateConqueror()
        {
            _pendingFallenMandateKingdomId = -1L;
            _pendingMandateConquerorKingdomId = -1L;
        }

        public static void NormalizeMapMarkerAfterRebelSettlement(Kingdom pKingdom)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.MANDATE_MAP_MARKER_KIND, out string marker, "");
            if (marker == "rebel_claimant") pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, "moh");

            MandateReport report = ReadReport();
            if (!Ready || !report.active || report.kingdom_id != pKingdom.id || report.period_id < 0)
            {
                DirtyAllMaps();
                return;
            }
            if (report.map_marker_kind != "rebel_claimant") return;

            UpsertState(pKingdom, report.period_id, report.mandate_value, report.imperial_authority,
                report.dynasty_prestige, report.core_control, report.vassal_loyalty, report.crisis_level,
                Date.getCurrentYear(), ReadStartTime(), report.origin_type, report.claimant_kind, null, "moh");
            DirtyAllMaps();
        }

        public static ColorAsset GetDynastyMapColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            Kingdom mandate = GetCurrentMandateKingdom();
            if (mandate?.data == null || pKingdom?.data == null) return pFallback;
            if (pKingdom == mandate || VassalService.GetRootSuzerain(pKingdom) == mandate)
                return DirectKingdomColor(mandate, pFallback);
            return pFallback;
        }

        public static ColorAsset GetCoreMapColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || pKingdom.isNeutral()) return pFallback;
            MandateReport report = ReadReport();
            if (report.period_id < 0 || _coreCityIds.Count == 0) return pFallback;
            if (!ControlsAnyCurrentCore(pKingdom)) return pFallback;

            Kingdom mandate = GetCurrentMandateKingdom();
            if (mandate?.data != null)
            {
                if (pKingdom == mandate) return CoreControlledColor();
                if (VassalService.GetRootSuzerain(pKingdom) == mandate) return CoreVassalColor();
            }

            return CoreLostColor();
        }

        public static ColorAsset GetCoreMapColor(City pCity, ColorAsset pFallback)
        {
            string status = GetCoreMapStatus(pCity);
            switch (status)
            {
                case "controlled": return CoreControlledColor();
                case "vassal": return CoreVassalColor();
                case "lost": return CoreLostColor();
                case "orphan": return CoreOrphanColor();
                default:
                    return pFallback;
            }
        }

        public static string GetCoreMapStatus(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return "none";
            MandateReport report = ReadReport();
            if (report.period_id < 0 || _coreCityIds.Count == 0) return "none";

            Kingdom mandate = GetCurrentMandateKingdom();
            Kingdom owner = pCity.kingdom;
            return MandateCoreMapRules.SelectCoreStatus(
                _coreCityIds.Contains(pCity.id),
                mandate?.data != null,
                owner?.data != null,
                owner?.data != null && mandate?.data != null && owner == mandate,
                owner?.data != null && mandate?.data != null && VassalService.GetRootSuzerain(owner) == mandate);
        }

        public static Color32 GetDynastyTileColor(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            Kingdom mandate = GetCurrentMandateKingdom();
            if (kingdom?.data == null || mandate?.data == null) return new Color32(0, 0, 0, 0);
            if (kingdom != mandate && VassalService.GetRootSuzerain(kingdom) != mandate) return new Color32(0, 0, 0, 0);
            Color32 color = (DirectKingdomColor(mandate, null)?.getColorMain32()) ?? new Color32(220, 190, 80, 255);
            color.a = kingdom == mandate ? (byte)230 : (byte)150;
            return color;
        }

        public static Color32 GetCoreTileColor(City pCity)
        {
            if (pCity?.data == null) return new Color32(0, 0, 0, 0);
            MandateReport report = ReadReport();
            if (report.period_id < 0 || !_coreCityIds.Contains(pCity.id)) return new Color32(0, 0, 0, 0);

            Kingdom mandate = GetCurrentMandateKingdom();
            Kingdom owner = pCity.kingdom;
            if (owner?.data == null || mandate?.data == null) return new Color32(140, 140, 140, 120);
            if (owner == mandate) return new Color32(34, 107, 58, 220);
            if (VassalService.GetRootSuzerain(owner) == mandate) return new Color32(79, 143, 69, 190);
            return new Color32(179, 18, 75, 210);
        }

        private static bool ControlsAnyCurrentCore(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || _coreCityIds.Count == 0) return false;
            try
            {
                foreach (City city in pKingdom.getCities())
                    if (city?.data != null && _coreCityIds.Contains(city.id)) return true;
            }
            catch { }
            return false;
        }

        private static ColorAsset DirectKingdomColor(Kingdom pKingdom, ColorAsset pFallback)
        {
            if (pKingdom?.data == null) return pFallback;
            try
            {
                int colorId = pKingdom.data.color_id;
                if (colorId >= 0)
                    return AssetManager.kingdom_colors_library.getColorByIndex(colorId) ?? pFallback;
            }
            catch { }
            return pFallback;
        }

        private static ColorAsset CoreControlledColor()
        {
            return _coreControlledColor ??= MakeMapColor(MandateCoreMapRules.HexForStatus("controlled"));
        }

        private static ColorAsset CoreVassalColor()
        {
            return _coreVassalColor ??= MakeMapColor(MandateCoreMapRules.HexForStatus("vassal"));
        }

        private static ColorAsset CoreLostColor()
        {
            return _coreLostColor ??= MakeMapColor(MandateCoreMapRules.HexForStatus("lost"));
        }

        private static ColorAsset CoreOrphanColor()
        {
            return MakeMapColor(MandateCoreMapRules.HexForStatus("orphan"));
        }

        private static ColorAsset MakeMapColor(string pHex)
        {
            ColorAsset color = ColorAsset.tryMakeNewColorAsset(pHex);
            color?.initColor();
            return color;
        }

        public static string BuildDynastyTooltip(Kingdom pKingdom)
        {
            if (Date.getCurrentYear() > int.MinValue) return BuildDynastyTooltipClean(pKingdom);
            MandateReport r = ReadReport();
            if (!r.active) return T("aw_hist_mandate_none");
            if (pKingdom?.data == null) return r.dynasty_name;
            bool inSystem = pKingdom.id == r.kingdom_id ||
                            VassalService.GetRootSuzerain(pKingdom)?.id == r.kingdom_id;
            return r.dynasty_name + "\n" + T("aw_hist_mandate_map_realm") + r.kingdom_name +
                   "\n" + T("aw_hist_mandate_value") + r.mandate_value +
                   "\n" + T("aw_hist_mandate_authority") + r.imperial_authority +
                   "\n" + T("aw_hist_mandate_core_control") + Mathf.RoundToInt(r.core_control * 100f) + "%" +
                   "\n" + T("aw_hist_mandate_current_zone") +
                   (inSystem ? T("aw_hist_mandate_inside") : T("aw_hist_mandate_outside"));
        }

        public static string BuildCoreTooltip(Kingdom pKingdom)
        {
            if (Date.getCurrentYear() > int.MinValue) return BuildCoreTooltipClean(pKingdom, null);
            MandateReport r = ReadReport();
            if (r.period_id < 0) return T("aw_hist_mandate_no_core");
            return T("aw_hist_mandate_core_title") + "\n" + T("aw_hist_mandate_map_realm") +
                   (r.kingdom_name == "" ? T("aw_hist_none") : r.kingdom_name) +
                   "\n" + T("aw_hist_mandate_core_city_count") + r.controlled_core_count + "/" + r.core_count +
                   "\n" + T("aw_hist_mandate_control_ratio") + Mathf.RoundToInt(r.core_control * 100f) + "%" +
                   (pKingdom?.data != null ? "\n" + T("aw_hist_mandate_current_kingdom") + pKingdom.name : "");
        }

        public static string BuildCoreTooltip(City pCity, Kingdom pKingdom)
        {
            if (Date.getCurrentYear() > int.MinValue) return BuildCoreTooltipClean(pKingdom, pCity);
            return BuildCoreTooltip(pKingdom);
        }

        private static string BuildDynastyTooltipClean(Kingdom pKingdom)
        {
            MandateReport r = ReadReport();
            if (!r.active) return T("aw_hist_mandate_none");
            bool inSystem = pKingdom?.data != null &&
                            (pKingdom.id == r.kingdom_id ||
                             VassalService.GetRootSuzerain(pKingdom)?.id == r.kingdom_id);
            MandateRebelReport rebels = MandateRebelService.ReadReport();
            ForeignOccupationReport occupation = ForeignOccupationService.ReadReport();
            string text = r.dynasty_name +
                          "\n" + T("aw_hist_mandate_map_realm") + r.kingdom_name +
                          "\n" + T("aw_hist_mandate_value") + r.mandate_value +
                          "\n" + T("aw_hist_mandate_authority") + r.imperial_authority +
                          "\n" + T("aw_hist_mandate_source") + OriginLabel(r.origin_type) +
                          "\n" + T("aw_hist_mandate_marker") + MandateMapMarkerService.MarkerLabel(r.map_marker_kind) +
                          "\n" + T("aw_hist_mandate_core_control") + r.controlled_core_count + "/" + r.core_count +
                          " (" + Mathf.RoundToInt(r.core_control * 100f) + "%)" +
                          "\n" + T("aw_hist_mandate_current_zone") +
                          (inSystem ? T("aw_hist_mandate_inside") : T("aw_hist_mandate_outside"));
            if (rebels.active_count > 0)
                text += "\n" + T("aw_hist_mandate_rebels") + rebels.active_count +
                        T("aw_hist_mandate_uprising_count") +
                        rebels.strongest_name + " " + Mathf.RoundToInt(rebels.strongest_core_control * 100f) + "%";
            if (occupation.active_count > 0)
                text += "\n" + T("aw_hist_mandate_foreign_occupation") + occupation.active_count +
                        T("aw_hist_mandate_city_count_mid") +
                        Mathf.RoundToInt(occupation.max_resentment);
            return text;
        }

        private static string BuildCoreTooltipClean(Kingdom pKingdom, City pCity)
        {
            MandateReport r = ReadReport();
            if (r.period_id < 0) return T("aw_hist_mandate_no_core");
            string owner = "";
            if (pKingdom?.data != null)
            {
                int pointedCount = CountControlledCoreCities(pKingdom, r.period_id);
                owner = "\n" + T("aw_hist_mandate_current_kingdom") + pKingdom.name;
                owner += "\n" + MandateCoreTooltipRules.BuildPointedKingdomCoreCountLine(
                    pKingdom.name, pointedCount, r.core_count, T("aw_hist_mandate_pointed_core_count"));
                owner += "\n" + MandateCoreTooltipRules.BuildPointedKingdomControlLine(
                    pKingdom.name, r.core_count <= 0 ? 1f : pointedCount / (float)r.core_count,
                    T("aw_hist_mandate_pointed_control"));
            }

            string city = "";
            if (pCity?.data != null)
            {
                city = "\n" + MapModeTooltipTextRules.BuildPointedCityStatusBlock(
                    T("aw_map_hover_city"),
                    T("aw_map_city_status"),
                    T("aw_map_progress"),
                    pCity.data.name ?? "",
                    MandateCoreStatusLabel(GetCoreMapStatus(pCity)),
                    0.0,
                    0.0);
            }

            return T("aw_hist_mandate_core_title") +
                   "\n" + T("aw_hist_mandate_map_realm") +
                   (string.IsNullOrEmpty(r.kingdom_name) ? T("aw_hist_none") : r.kingdom_name) +
                   "\n" + T("aw_hist_mandate_core_city_count") + r.controlled_core_count + "/" + r.core_count +
                   "\n" + T("aw_hist_mandate_original_core") + r.original_core_count +
                   "\n" + T("aw_hist_mandate_control_ratio") + Mathf.RoundToInt(r.core_control * 100f) + "%" +
                   owner + city;
        }

        private static string MandateCoreStatusLabel(string pStatus)
        {
            switch (pStatus ?? "")
            {
                case "controlled": return T("aw_hist_mandate_core_status_controlled");
                case "vassal": return T("aw_hist_mandate_core_status_vassal");
                case "lost": return T("aw_hist_mandate_core_status_lost");
                case "orphan": return T("aw_hist_mandate_core_status_orphan");
                default: return T("aw_map_status_none");
            }
        }

        private static string OriginLabel(string pOrigin)
        {
            switch (pOrigin)
            {
                case "rebel": return WarDisplayLabelRules.EventLabel("mandate_declared_rebel");
                case "pseudo_foreign": return WarDisplayLabelRules.EventLabel("mandate_declared_foreign_pseudo");
                case "self_restoration": return WarDisplayLabelRules.EventLabel("mandate_declared_refounder");
                case MandateFeudatoryCompletionRules.RestorationOrigin:
                    return WarDisplayLabelRules.EventLabel(
                        "mandate_declared_dynastic_restoration");
                default: return WarDisplayLabelRules.EventLabel("mandate_declared_orthodox");
            }
        }

        public static List<string> GetRecentEventLines(int pLimit)
        {
            var result = new List<string>();
            if (!Ready) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT YEAR_PREFIX, CONTENT FROM " + MandateEventTableItem.GetTableName() +
                                  " ORDER BY EVENT_ID DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@limit", Mathf.Max(1, pLimit));
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                    result.Add((reader.IsDBNull(0) ? "" : reader.GetString(0)) + " " +
                               (reader.IsDBNull(1) ? "" : reader.GetString(1)));
            }
            catch { }
            return result;
        }

        public static void RecordMandateEvent(string pType, Kingdom pKingdom, Actor pActor, City pCity, int pDelta,
            int pMandate, string pContent)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            RecordEvent(pType, pKingdom, pActor, pCity, pDelta, pMandate, pContent);
        }

        public static void MarkDirty()
        {
            _cacheDirty = true;
        }

        private static void TryAutoDeclareMandate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.id != GetAutoMandateCandidateId()) return;
            TryDeclareMandate(pKingdom, "auto");
        }

        private static long GetAutoMandateCandidateId()
        {
            int year = Date.getCurrentYear();
            int kingdomCount = World.world?.kingdoms?.list?.Count ?? -1;
            if (_autoCandidateYear == year && _autoCandidateKingdomCount == kingdomCount)
                return _autoCandidateKingdomId;

            _autoCandidateYear = year;
            _autoCandidateKingdomCount = kingdomCount;
            _autoCandidateKingdomId = -1L;

            if (World.world?.kingdoms == null) return -1L;
            MandateReport last = ReadReport();
            var kingdoms = new List<Kingdom>();
            var kingdomIndexes = new Dictionary<long, int>();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                int index = kingdoms.Count;
                kingdoms.Add(kingdom);
                if (kingdom?.data != null)
                    kingdomIndexes[kingdom.id] = index;
            }

            var ids = new long[kingdoms.Count];
            var realmPowers = new float[kingdoms.Count];
            var parentIndexes = new int[kingdoms.Count];
            var eligible = new bool[kingdoms.Count];
            for (int index = 0; index < kingdoms.Count; index++)
            {
                Kingdom kingdom = kingdoms[index];
                ids[index] = kingdom?.id ?? -1L;
                parentIndexes[index] = -1;
                if (kingdom?.data == null || kingdom.isRekt() ||
                    !kingdom.isCiv()) continue;

                realmPowers[index] = CalculateMandateRealmPower(kingdom);
                long suzerainId = VassalService.GetSuzerainId(kingdom);
                if (suzerainId >= 0L &&
                    kingdomIndexes.TryGetValue(suzerainId,
                        out int parentIndex))
                    parentIndexes[index] = parentIndex;
                eligible[index] = IsAutoMandateCandidateBaseEligible(
                    kingdom, last);
            }

            float[] powers = MandatePowerRules.
                AggregateCompetitionPowersByRoot(realmPowers,
                    parentIndexes);
            int winner = MandatePowerRules.SelectWinningCandidateIndex(
                powers, eligible);
            if (winner >= 0 && winner < ids.Length)
                _autoCandidateKingdomId = ids[winner];
            return _autoCandidateKingdomId;
        }

        private static bool IsAutoMandateCandidateBaseEligible(Kingdom pKingdom, MandateReport pLast)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral())
                return false;
            if (!pKingdom.hasKing() || pKingdom.king?.data == null) return false;
            if (VassalService.IsVassalKingdom(pKingdom)) return false;
            if (!IsSupportedKingdom(pKingdom)) return false;
            if (!KingdomPolicyService.IsCompleted(
                    pKingdom, PolicyNodeKind.Social, "aw_policy_mandate_rites"))
                return false;

            bool historicalFigure = IsHistoricalFigureKing(pKingdom);
            KingdomTitle title = KingdomTitleService.GetTitle(pKingdom);
            int cityCount = CountCities(pKingdom);
            if (!MandateDeclarationRules.HasEnoughRealmToDeclare(cityCount, (int)title,
                    historicalFigure, 4, (int)KingdomTitle.King))
                return false;

            return !MandateDeclarationRules.NeedsLegalCoreControl(pLast.core_count, pLast.active) ||
                   MandateDeclarationRules.HasEnoughLegalCoreControl(
                       GetCoreControlRatio(pKingdom, pLast.period_id), RESTORE_CORE_THRESHOLD);
        }

        private static int CalculateYearlyDelta(Kingdom pKingdom, MandateReport pReport)
        {
            int delta = 0;
            try { delta += pKingdom.hasEnemies() ? -2 : 1; } catch { delta += 1; }
            delta += CalculateStrongestPowerPenalty(pKingdom);
            if (pReport.core_control >= 0.85f) delta += 2;
            else if (pReport.core_control < 0.5f) delta -= 4;
            if (pReport.vassal_loyalty >= 0.7f) delta += 1;
            else if (pReport.vassal_loyalty < 0.35f) delta -= 2;
            delta += HeirService.GetMandateChildScarcityPenalty(pKingdom);
            pKingdom.data.get(LineageKeys.MANDATE_SACRIFICE_BUFF_UNTIL,
                out int sacrificeBuffUntil, int.MinValue);
            pKingdom.data.get(LineageKeys.MANDATE_SACRIFICE_BUFF_DELTA,
                out int sacrificeBuffDelta, 0);
            delta += MandateSacrificeRules.ActiveAnnualDelta(
                Date.getCurrentYear(), sacrificeBuffUntil, sacrificeBuffDelta);

            Actor king = pKingdom.king;
            if (king?.data != null)
            {
                if (king.hasTrait("first") || king.hasTrait("figure")) delta += 5;
                try { if (king.getAge() <= 24) delta -= 1; } catch { }
                try { if (king.stats["intelligence"] <= 5f) delta -= 1; } catch { }
                try { if (king.stats["diplomacy"] >= 12f) delta += 1; } catch { }
                try { if (king.stats["stewardship"] >= 12f) delta += 1; } catch { }
            }

            try
            {
                string eraId = World.world_era?.id ?? "";
                if (eraId == "age_hope" || eraId == "age_wonders")
                    delta += 2;
                if (eraId == "age_despair" || eraId == "age_ash" || eraId == "age_chaos")
                    delta -= 12;
            }
            catch { }

            return delta;
        }

        private static int CalculateAuthority(Kingdom pKingdom, int pMandate, float pCoreControl, float pVassalLoyalty)
        {
            float score = 25f + pMandate * 0.35f + pCoreControl * 25f + pVassalLoyalty * 15f;
            score += CountCities(pKingdom) * 1.2f;
            return Mathf.Clamp(Mathf.RoundToInt(score), 0, 100);
        }

        private static void ChangeMandate(Kingdom pKingdom, int pDelta,
            string pEventType, string pContent = null, bool pRecordEvent = true)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            MandateReport r = ReadReport();
            if (!r.active || pKingdom?.data == null || pKingdom.id != r.kingdom_id) return;
            MandatePhaseService.AdjustCatalyst(
                MandatePhaseRules.CatalystDeltaForMandateChange(pDelta), pEventType);
            int next = Mathf.Clamp(r.mandate_value + pDelta, MIN_VALUE, MAX_VALUE);
            UpdateState(pKingdom, r.period_id, next, r.imperial_authority, r.dynasty_prestige, r.core_control,
                r.vassal_loyalty, CrisisLevel(next), Date.getCurrentYear());
            SyncMandateRuntimeMirrors(pKingdom, next,
                r.imperial_authority, r.dynasty_prestige);
            if (!pRecordEvent) return;
            if (!string.IsNullOrEmpty(pContent))
            {
                RecordEvent(pEventType, pKingdom, pKingdom.king, null, pDelta, next, pContent);
                return;
            }
            RecordEvent(pEventType, pKingdom, pKingdom.king, null, pDelta, next,
                pKingdom.name + T("aw_hist_mandate_changed_mid") + Signed(pDelta) +
                T("aw_hist_mandate_current") + next);
        }

        private static void SyncMandateRuntimeMirrors(Kingdom pKingdom,
            int pMandateValue, int pAuthority, int pPrestige)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.MANDATE_VALUE, pMandateValue);
            pKingdom.data.set(LineageKeys.MANDATE_AUTHORITY, pAuthority);
            pKingdom.data.set(LineageKeys.MANDATE_PRESTIGE, pPrestige);
        }

        private static MandateReport ReadReportFromDb()
        {
            var report = new MandateReport();
            if (!Ready) return report;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ACTIVE,KINGDOM_ID,KINGDOM_NAME,KINGDOM_COLOR,DYNASTY_NAME,EMPEROR_ACTOR_ID,EMPEROR_NAME,PERIOD_ID," +
                                   "MANDATE_VALUE,IMPERIAL_AUTHORITY,DYNASTY_PRESTIGE,CORE_CONTROL,VASSAL_LOYALTY,CRISIS_LEVEL," +
                                   "ORIGIN_TYPE,ORIGINAL_CORE_COUNT,REBEL_ORIGIN_KINGDOM_ID,REBEL_ORIGIN_KINGDOM_NAME,CLAIMANT_KIND,MAP_MARKER_KIND " +
                                  "FROM " + MandateStateTableItem.GetTableName() + " WHERE STATE_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", STATE_ID);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return report;
                report.active = ToInt(reader, 0) == 1;
                report.kingdom_id = ToLong(reader, 1);
                report.kingdom_name = ToString(reader, 2);
                report.kingdom_color = ToString(reader, 3);
                report.dynasty_name = ToString(reader, 4);
                report.emperor_actor_id = ToLong(reader, 5);
                report.emperor_name = ToString(reader, 6);
                report.period_id = ToLong(reader, 7);
                report.mandate_value = ToInt(reader, 8);
                report.imperial_authority = ToInt(reader, 9);
                report.dynasty_prestige = ToInt(reader, 10);
                report.core_control = (float)ToDouble(reader, 11);
                report.vassal_loyalty = (float)ToDouble(reader, 12);
                report.crisis_level = ToString(reader, 13);
                report.origin_type = ToString(reader, 14);
                report.original_core_count = ToInt(reader, 15);
                report.rebel_origin_kingdom_id = ToLong(reader, 16);
                report.rebel_origin_kingdom_name = ToString(reader, 17);
                report.claimant_kind = ToString(reader, 18);
                report.map_marker_kind = ToString(reader, 19);
            }
            catch { }

            FillDynamicReport(report);
            return report;
        }

        private static void FillDynamicReport(MandateReport pReport)
        {
            if (pReport.period_id < 0 || World.world?.kingdoms == null) return;
            Kingdom mandate = FindKingdom(pReport.kingdom_id);
            pReport.core_count = CountCoreCities(pReport.period_id);
            if (pReport.original_core_count <= 0) pReport.original_core_count = pReport.core_count;
            if (string.IsNullOrEmpty(pReport.origin_type)) pReport.origin_type = "native";
            if (string.IsNullOrEmpty(pReport.claimant_kind)) pReport.claimant_kind = "orthodox";
            if (string.IsNullOrEmpty(pReport.map_marker_kind))
                pReport.map_marker_kind = MarkerKind(pReport.origin_type, pReport.claimant_kind);
            pReport.controlled_core_count = CountControlledCoreCities(mandate, pReport.period_id);
            pReport.core_control = pReport.core_count <= 0 ? 1f : pReport.controlled_core_count / (float)pReport.core_count;
            pReport.vassal_count = mandate?.data == null ? 0 : VassalService.GetVassals(mandate, true).Count;
            pReport.vassal_loyalty = mandate?.data == null ? 0f : CalculateVassalLoyalty(mandate);
        }

        private static void UpsertState(Kingdom pKingdom, long pPeriodId, int pMandate, int pAuthority, int pPrestige,
            float pCoreControl, float pVassalLoyalty, string pCrisis, int pYear, double pStartTime,
            string pOriginType = null, string pClaimantKind = null, Kingdom pRebelOrigin = null,
            string pMapMarkerKind = null)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (!Ready || pKingdom?.data == null) return;
            Actor king = pKingdom.king;
            string table = MandateStateTableItem.GetTableName();
            MandateReport current = _cachedReport;
            string originType = string.IsNullOrEmpty(pOriginType) ? current?.origin_type ?? "native" : pOriginType;
            string claimantKind = string.IsNullOrEmpty(pClaimantKind) ? current?.claimant_kind ?? "orthodox" : pClaimantKind;
            string markerKind = string.IsNullOrEmpty(pMapMarkerKind) ? current?.map_marker_kind ?? MarkerKind(originType, claimantKind) : pMapMarkerKind;
            long rebelOriginId = pRebelOrigin?.id ?? current?.rebel_origin_kingdom_id ?? -1L;
            string rebelOriginName = pRebelOrigin?.name ?? current?.rebel_origin_kingdom_name ?? "";
            var values = new[]
            {
                ColumnVal.Create("ACTIVE", 1),
                ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                ColumnVal.Create("DYNASTY_NAME", MakeDynastyName(pKingdom)),
                ColumnVal.Create("EMPEROR_ACTOR_ID", king?.data?.id ?? -1L),
                ColumnVal.Create("EMPEROR_NAME", king?.getName() ?? ""),
                ColumnVal.Create("PERIOD_ID", pPeriodId),
                ColumnVal.Create("MANDATE_VALUE", pMandate),
                ColumnVal.Create("IMPERIAL_AUTHORITY", pAuthority),
                ColumnVal.Create("DYNASTY_PRESTIGE", pPrestige),
                ColumnVal.Create("CORE_CONTROL", (double)pCoreControl),
                ColumnVal.Create("VASSAL_LOYALTY", (double)pVassalLoyalty),
                ColumnVal.Create("CRISIS_LEVEL", pCrisis ?? ""),
                ColumnVal.Create("START_TIME", pStartTime),
                ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()),
                ColumnVal.Create("LAST_YEAR", pYear),
                ColumnVal.Create("ORIGIN_TYPE", originType),
                ColumnVal.Create("ORIGINAL_CORE_COUNT", CountCoreCities(pPeriodId)),
                ColumnVal.Create("REBEL_ORIGIN_KINGDOM_ID", rebelOriginId),
                ColumnVal.Create("REBEL_ORIGIN_KINGDOM_NAME", rebelOriginName),
                ColumnVal.Create("CLAIMANT_KIND", claimantKind),
                ColumnVal.Create("MAP_MARKER_KIND", markerKind)
            };
            if (DB.CheckKeyExist(table, SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID)))
            {
                DB.UpdateValue(table,
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID) },
                    values);
            }
            else
            {
                var insert = new List<ColumnVal> { ColumnVal.Create("STATE_ID", STATE_ID) };
                insert.AddRange(values);
                DB.Insert(table, insert.ToArray());
            }
            PublishRuntimeMarkerProjection(true, pKingdom.id, markerKind);
            MarkDirty();
        }

        private static void PublishRuntimeMarkerProjection(bool pActive, long pKingdomId,
            string pMarkerKind)
        {
            _runtimeMarkerKingdomId = pActive && pKingdomId >= 0 ? pKingdomId : -1L;
            _runtimeMarkerKind = _runtimeMarkerKingdomId >= 0 ? pMarkerKind ?? "" : "";
        }

        private static void UpdateState(Kingdom pKingdom, long pPeriodId, int pMandate, int pAuthority, int pPrestige,
            float pCoreControl, float pVassalLoyalty, string pCrisis, int pYear)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            UpsertState(pKingdom, pPeriodId, pMandate, pAuthority, pPrestige, pCoreControl, pVassalLoyalty, pCrisis,
                pYear, ReadReport().period_id == pPeriodId ? ReadStartTime() : LineageService.CurTime(),
                ReadReport().origin_type, ReadReport().claimant_kind, null, ReadReport().map_marker_kind);
            DirtyAllMaps();
        }

        private static double ReadStartTime()
        {
            if (!Ready) return LineageService.CurTime();
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT START_TIME FROM " + MandateStateTableItem.GetTableName() +
                                  " WHERE STATE_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", STATE_ID);
                object value = cmd.ExecuteScalar();
                return value == null || value == DBNull.Value ? LineageService.CurTime() : Convert.ToDouble(value);
            }
            catch { return LineageService.CurTime(); }
        }

        private static bool CreateLegalCores(long pPeriodId,
            IReadOnlyList<MandateProjectionOutboxPersistence.
                CoreCitySnapshot> pSnapshots,
            string pProjectionKeyPrefix, bool requireCurrentStateUpdate)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                !Ready || pSnapshots == null)
                return false;
            bool projected = MandateLegalCoreProjectionPersistence.TryProject(
                DB, STATE_ID, pPeriodId, pSnapshots, pProjectionKeyPrefix,
                LineageService.CurTime(), requireCurrentStateUpdate,
                out _, out string error);
            if (!projected)
                ModClass.LogWarning("Mandate legal core completion failed: " +
                                    error);
            else
                MarkDirty();
            return projected;
        }

        private static bool EnsurePendingCoreSnapshots(Kingdom pKingdom,
            MandateProjectionOutboxPersistence.PendingProjection pPending)
        {
            if (!string.IsNullOrEmpty(pPending.CoreSnapshotSource))
                return true;
            if (!TryCaptureLegalCoreSnapshots(pKingdom,
                    pPending.PreviousPeriodId, "legacy",
                    out List<MandateProjectionOutboxPersistence.
                        CoreCitySnapshot> snapshots))
                return false;
            if (!MandateProjectionOutboxPersistence.
                    TryMigrateLegacyCoreSnapshots(DB,
                        pPending.OperationKey, snapshots,
                        out _, out string error))
            {
                ModClass.LogWarning(
                    "Legacy Mandate core snapshot migration failed: " +
                    error);
                return false;
            }
            if (!MandateProjectionOutboxPersistence.TryReadPending(
                    DB, pPending.PeriodId,
                    out MandateProjectionOutboxPersistence.PendingProjection
                        migrated, out error) || migrated == null)
            {
                ModClass.LogWarning(
                    "Legacy Mandate core snapshot reload failed: " + error);
                return false;
            }
            pPending.CoreSnapshotSource = migrated.CoreSnapshotSource;
            pPending.CoreCitySnapshots = migrated.CoreCitySnapshots;
            return true;
        }

        private static bool TryCaptureLegalCoreSnapshots(Kingdom pKingdom,
            long pPreviousPeriodId, string pSnapshotSource,
            out List<MandateProjectionOutboxPersistence.CoreCitySnapshot>
                pSnapshots)
        {
            pSnapshots = new List<MandateProjectionOutboxPersistence.
                CoreCitySnapshot>();
            var captured = new HashSet<long>();
            if (pPreviousPeriodId >= 0L)
            {
                if (!MandateLegalCoreProjectionPersistence.
                        TryReadInheritedSnapshots(DB, pPreviousPeriodId,
                            out List<MandateProjectionOutboxPersistence.
                                CoreCitySnapshot> inherited,
                            out string error))
                {
                    ModClass.LogWarning(
                        "Mandate inherited core snapshot failed: " + error);
                    return false;
                }
                foreach (MandateProjectionOutboxPersistence.CoreCitySnapshot
                         core in inherited)
                {
                    if (!MandateLegalCoreInheritanceRules.
                            ShouldInheritPreviousCore(pPreviousPeriodId,
                                core.CityId,
                                captured.Contains(core.CityId)))
                        continue;
                    core.SnapshotSource = pSnapshotSource ?? "";
                    pSnapshots.Add(core);
                    captured.Add(core.CityId);
                }
            }
            if (pKingdom?.data != null)
            {
                string kingdomName = pKingdom.name ?? "";
                string kingdomColor = HistoryColors.FromKingdom(pKingdom);
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    if (!MandateLegalCoreInheritanceRules.
                            ShouldAddFoundingCore(city.id,
                                captured.Contains(city.id)))
                        continue;
                    pSnapshots.Add(new MandateProjectionOutboxPersistence.
                        CoreCitySnapshot
                    {
                        CityId = city.id,
                        CityName = city.data.name ?? "",
                        OriginalKingdomId = pKingdom.id,
                        OriginalKingdomName = kingdomName,
                        OriginalKingdomColor = kingdomColor,
                        CoreType = "founding",
                        SnapshotSource = pSnapshotSource ?? ""
                    });
                    captured.Add(city.id);
                }
            }
            pSnapshots.Sort((left, right) =>
                left.CityId.CompareTo(right.CityId));
            return true;
        }

        private static void UpdateOriginalCoreCount(long pPeriodId)
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return;
            if (!Ready || pPeriodId < 0) return;
            int count = CountCoreCities(pPeriodId);
            try
            {
                DB.UpdateValue(MandateStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID) },
                    ColumnVal.Create("ORIGINAL_CORE_COUNT", count),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
                DB.UpdateValue(MandatePeriodTableItem.GetTableName(),
                    new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("PERIOD_ID", pPeriodId) },
                    ColumnVal.Create("LEGAL_CORE_COUNT", count));
            }
            catch { }
            MarkDirty();
        }

        private static bool RecordSnapshotEvent(string pType,
            long pPeriodId, long pKingdomId, string pKingdomName,
            string pKingdomColor, long pActorId, string pActorName,
            double pWorldTime, string pYearPrefix, int pDelta,
            int pMandate, int pImperialAuthority, string pContent,
            string pProjectionKey)
        {
            if (!Ready || string.IsNullOrWhiteSpace(pProjectionKey))
                return false;
            try
            {
                long eventId = TableIdAllocator.Next(DB,
                    MandateEventTableItem.GetTableName(), "EVENT_ID");
                return MandateProjectionOutboxPersistence.
                    TryApplyIdempotentRecord(DB,
                        MandateEventTableItem.GetTableName(),
                        pProjectionKey,
                        transaction => InsertMandateEventSnapshotRow(
                            transaction, eventId, pPeriodId, pType,
                            pKingdomId, pKingdomName, pKingdomColor,
                            pActorId, pActorName, pWorldTime, pYearPrefix,
                            pDelta, pMandate, pImperialAuthority, pContent,
                            pProjectionKey), out _);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate snapshot event failed: " +
                                    e.Message);
                return false;
            }
        }

        private static bool InsertMandateEventSnapshotRow(
            SQLiteTransaction pTransaction, long pEventId, long pPeriodId,
            string pType, long pKingdomId, string pKingdomName,
            string pKingdomColor, long pActorId, string pActorName,
            double pWorldTime, string pYearPrefix, int pDelta,
            int pMandate, int pImperialAuthority, string pContent,
            string pProjectionKey)
        {
            using var command = new SQLiteCommand(DB)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " +
                    MandateEventTableItem.GetTableName() +
                    "(EVENT_ID,PERIOD_ID,EVENT_TYPE,KINGDOM_ID," +
                    "KINGDOM_NAME,KINGDOM_COLOR,ACTOR_ID,ACTOR_NAME," +
                    "CITY_ID,CITY_NAME,WORLD_TIME,YEAR_PREFIX,VALUE_DELTA," +
                    "MANDATE_VALUE,IMPERIAL_AUTHORITY,CONTENT," +
                    "PROJECTION_KEY) VALUES(@id,@period,@type,@kingdom," +
                    "@kingdomName,@color,@actor,@actorName,-1,'',@time," +
                    "@year,@delta,@mandate,@authority,@content,@key)"
            };
            command.Parameters.AddWithValue("@id", pEventId);
            command.Parameters.AddWithValue("@period", pPeriodId);
            command.Parameters.AddWithValue("@type", pType ?? "");
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@kingdomName",
                pKingdomName ?? "");
            command.Parameters.AddWithValue("@color",
                HistoryColors.Normalize(pKingdomColor));
            command.Parameters.AddWithValue("@actor", pActorId);
            command.Parameters.AddWithValue("@actorName", pActorName ?? "");
            command.Parameters.AddWithValue("@time", pWorldTime);
            command.Parameters.AddWithValue("@year", pYearPrefix ?? "");
            command.Parameters.AddWithValue("@delta", pDelta);
            command.Parameters.AddWithValue("@mandate", pMandate);
            command.Parameters.AddWithValue("@authority",
                pImperialAuthority);
            command.Parameters.AddWithValue("@content", pContent ?? "");
            command.Parameters.AddWithValue("@key", pProjectionKey);
            return command.ExecuteNonQuery() == 1;
        }

        private static bool RecordEvent(string pType, Kingdom pKingdom, Actor pActor, City pCity, int pDelta,
            int pMandate, string pContent, long pPeriodId = -1L,
            string pProjectionKey = "")
        {
            if (!MandateAuthorityMutationRules.CanMutate(
                    AW3MultiplayerReplicaScope.IsReplicaSession))
                return false;
            if (!Ready) return false;
            MandateReport report = ReadReport();
            try
            {
                long eventId = TableIdAllocator.Next(DB, MandateEventTableItem.GetTableName(), "EVENT_ID");
                double now = LineageService.CurTime();
                long periodId = pPeriodId >= 0L
                    ? pPeriodId
                    : report.period_id;
                string yearPrefix = HistoryWriter.BuildYearPrefix(now,
                    pKingdom);
                if (!string.IsNullOrWhiteSpace(pProjectionKey))
                    return MandateProjectionOutboxPersistence.
                        TryApplyIdempotentRecord(DB,
                            MandateEventTableItem.GetTableName(),
                            pProjectionKey,
                            transaction => InsertMandateEventRow(transaction,
                                eventId, periodId, pType, pKingdom, pActor,
                                pCity, now, yearPrefix, pDelta, pMandate,
                                report.imperial_authority, pContent,
                                pProjectionKey), out _);
                DB.Insert(MandateEventTableItem.GetTableName(),
                    ColumnVal.Create("EVENT_ID", eventId),
                    ColumnVal.Create("PERIOD_ID", periodId),
                    ColumnVal.Create("EVENT_TYPE", pType ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom?.id ?? -1L),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom?.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("ACTOR_ID", pActor?.data?.id ?? -1L),
                    ColumnVal.Create("ACTOR_NAME", pActor?.getName() ?? ""),
                    ColumnVal.Create("CITY_ID", pCity?.id ?? -1L),
                    ColumnVal.Create("CITY_NAME", pCity?.data?.name ?? ""),
                    ColumnVal.Create("WORLD_TIME", now),
                    ColumnVal.Create("YEAR_PREFIX", yearPrefix),
                    ColumnVal.Create("VALUE_DELTA", pDelta),
                    ColumnVal.Create("MANDATE_VALUE", pMandate),
                    ColumnVal.Create("IMPERIAL_AUTHORITY", report.imperial_authority),
                    ColumnVal.Create("CONTENT", pContent ?? ""));
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate event failed: " + e.Message);
                return false;
            }
        }

        private static bool InsertMandateEventRow(
            SQLiteTransaction pTransaction, long pEventId, long pPeriodId,
            string pType, Kingdom pKingdom, Actor pActor, City pCity,
            double pWorldTime, string pYearPrefix, int pDelta,
            int pMandate, int pImperialAuthority, string pContent,
            string pProjectionKey)
        {
            using var command = new SQLiteCommand(DB)
            {
                Transaction = pTransaction,
                CommandText = "INSERT INTO " +
                    MandateEventTableItem.GetTableName() +
                    "(EVENT_ID,PERIOD_ID,EVENT_TYPE,KINGDOM_ID," +
                    "KINGDOM_NAME,KINGDOM_COLOR,ACTOR_ID,ACTOR_NAME," +
                    "CITY_ID,CITY_NAME,WORLD_TIME,YEAR_PREFIX,VALUE_DELTA," +
                    "MANDATE_VALUE,IMPERIAL_AUTHORITY,CONTENT," +
                    "PROJECTION_KEY) VALUES(@id,@period,@type,@kingdom," +
                    "@kingdomName,@color,@actor,@actorName,@city,@cityName," +
                    "@time,@year,@delta,@mandate,@authority,@content,@key)"
            };
            command.Parameters.AddWithValue("@id", pEventId);
            command.Parameters.AddWithValue("@period", pPeriodId);
            command.Parameters.AddWithValue("@type", pType ?? "");
            command.Parameters.AddWithValue("@kingdom",
                pKingdom?.id ?? -1L);
            command.Parameters.AddWithValue("@kingdomName",
                pKingdom?.name ?? "");
            command.Parameters.AddWithValue("@color",
                HistoryColors.FromKingdom(pKingdom));
            command.Parameters.AddWithValue("@actor",
                pActor?.data?.id ?? -1L);
            command.Parameters.AddWithValue("@actorName",
                pActor?.getName() ?? "");
            command.Parameters.AddWithValue("@city", pCity?.id ?? -1L);
            command.Parameters.AddWithValue("@cityName",
                pCity?.data?.name ?? "");
            command.Parameters.AddWithValue("@time", pWorldTime);
            command.Parameters.AddWithValue("@year", pYearPrefix ?? "");
            command.Parameters.AddWithValue("@delta", pDelta);
            command.Parameters.AddWithValue("@mandate", pMandate);
            command.Parameters.AddWithValue("@authority",
                pImperialAuthority);
            command.Parameters.AddWithValue("@content", pContent ?? "");
            command.Parameters.AddWithValue("@key", pProjectionKey ?? "");
            return command.ExecuteNonQuery() == 1;
        }

        private static void RebuildCoreCache(long pPeriodId)
        {
            _coreCityIds = new HashSet<long>();
            if (!Ready || pPeriodId < 0) return;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID FROM " + MandateCoreCityTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND ACTIVE=1";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read()) _coreCityIds.Add(ToLong(reader, 0));
            }
            catch { }
        }

        private static int CountCoreCities(long pPeriodId)
        {
            if (!Ready || pPeriodId < 0) return 0;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT COUNT(*) FROM " + MandateCoreCityTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND ACTIVE=1";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static int CountControlledCoreCities(Kingdom pMandate, long pPeriodId)
        {
            if (pMandate?.data == null || !Ready || pPeriodId < 0) return 0;
            int count = 0;
            foreach (long cityId in ReadCoreCityIds(pPeriodId))
            {
                City city = FindCity(cityId);
                Kingdom owner = city?.kingdom;
                if (owner?.data == null) continue;
                if (owner == pMandate || VassalService.GetRootSuzerain(owner) == pMandate) count++;
            }
            return count;
        }

        private static float GetCoreControlRatio(Kingdom pKingdom, long pPeriodId)
        {
            int total = CountCoreCities(pPeriodId);
            if (total <= 0) return 1f;
            return CountControlledCoreCities(pKingdom, pPeriodId) / (float)total;
        }

        private static IEnumerable<long> ReadCoreCityIds(long pPeriodId)
        {
            var ids = new List<long>();
            if (!Ready || pPeriodId < 0) return ids;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID FROM " + MandateCoreCityTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND ACTIVE=1";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read()) ids.Add(ToLong(reader, 0));
            }
            catch { }
            return ids;
        }

        private static float CalculateVassalLoyalty(Kingdom pMandate)
        {
            List<Kingdom> vassals = VassalService.GetVassals(pMandate, true);
            if (vassals.Count == 0) return 1f;
            float sum = 0f;
            foreach (Kingdom vassal in vassals)
            {
                int opinion = 0;
                try { opinion = World.world.diplomacy.getOpinion(vassal, pMandate).total; } catch { }
                sum += Mathf.InverseLerp(-100f, 100f, opinion);
            }
            return Mathf.Clamp01(sum / vassals.Count);
        }

        private static bool IsMostPowerfulIndependent(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || World.world?.kingdoms == null) return false;
            float own = CalculateMandateCompetitionPower(pKingdom);
            float strongestOther = 0f;
            float weakestOther = float.MaxValue;
            foreach (Kingdom other in World.world.kingdoms)
            {
                if (other == pKingdom) continue;
                if (!MandatePowerRules.IsEligibleCompetitor(IsValidMandatePowerKingdom(other),
                        VassalService.IsVassalKingdom(other), IsSupportedKingdom(other)))
                    continue;
                float otherPower = CalculateMandateCompetitionPower(other);
                if (otherPower > strongestOther) strongestOther = otherPower;
                if (otherPower > 0f && otherPower < weakestOther) weakestOther = otherPower;
            }
            if (weakestOther == float.MaxValue) weakestOther = 0f;
            return MandatePowerRules.HasRequiredLeadForMandate(own, strongestOther, weakestOther);
        }

        private static int CalculateStrongestPowerPenalty(Kingdom pMandate)
        {
            if (pMandate?.data == null || World.world?.kingdoms == null) return 0;
            float mandatePower = CalculateMandateCompetitionPower(pMandate);
            float strongest = mandatePower;
            foreach (Kingdom other in World.world.kingdoms)
            {
                if (other == pMandate) continue;
                if (!MandatePowerRules.IsEligibleCompetitor(IsValidMandatePowerKingdom(other),
                        VassalService.IsVassalKingdom(other), IsSupportedKingdom(other)))
                    continue;
                float power = CalculateMandateCompetitionPower(other);
                if (power > strongest) strongest = power;
            }
            return MandatePowerRules.CalculateStrongestPowerPenalty(mandatePower, strongest);
        }

        private static float CalculateMandateCompetitionPower(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0f;
            float own = CalculateMandateRealmPower(pKingdom);
            float vassalPower = 0f;
            foreach (Kingdom vassal in VassalService.GetVassals(pKingdom, pRecursive: true))
                vassalPower += CalculateMandateRealmPower(vassal);
            return MandatePowerRules.CalculateCompetitionPower(own, vassalPower);
        }

        private static float CalculateMandateRealmPower(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return 0f;
            return MandatePowerRules.CalculateRealmPower(
                pPopulation: CountPopulation(pKingdom),
                pCityCount: CountCities(pKingdom),
                pArmyPower: CountWarriors(pKingdom),
                pKingStewardship: GetKingStewardship(pKingdom),
                pTerritoryZones: CountZones(pKingdom));
        }

        private static int CountPopulation(Kingdom pKingdom)
        {
            try { return pKingdom?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static int CountWarriors(Kingdom pKingdom)
        {
            try { return pKingdom?.countTotalWarriors() ?? 0; }
            catch { return 0; }
        }

        private static float GetKingStewardship(Kingdom pKingdom)
        {
            try { return pKingdom?.king?.stats?["stewardship"] ?? 0f; }
            catch { return 0f; }
        }

        private static bool IsValidMandatePowerKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool IsSupportedKingdom(Kingdom pKingdom)
        {
            return XiaizationService.CanUseMandateSystem(pKingdom);
        }

        private static bool IsHumanKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (pKingdom.asset?.id == "human") return true;
            try
            {
                ActorAsset asset = pKingdom.getActorAsset();
                return asset?.id == "human" || asset?.banner_id == "human";
            }
            catch { return false; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            if (pId < 0 || World.world?.kingdoms == null) return null;
            try
            {
                Kingdom byId = World.world.kingdoms.get(pId);
                if (byId?.data != null) return byId;
            }
            catch { }
            foreach (Kingdom kingdom in World.world.kingdoms)
                if (kingdom?.data != null && kingdom.id == pId) return kingdom;
            return null;
        }

        private static Actor FindActor(long pId)
        {
            if (pId < 0L || World.world?.units == null) return null;
            try
            {
                Actor actor = World.world.units.get(pId);
                if (actor?.data != null) return actor;
            }
            catch { }
            foreach (Actor actor in World.world.units)
                if (actor?.data != null && actor.data.id == pId) return actor;
            return null;
        }

        private static City FindCity(long pId)
        {
            if (pId < 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pId);
                if (city?.data != null) return city;
            }
            catch { }
            foreach (City city in World.world.cities)
                if (city?.data != null && city.id == pId) return city;
            return null;
        }

        private static int CountCities(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }

        private static int CountZones(Kingdom pKingdom)
        {
            try { return pKingdom?.countZones() ?? 0; }
            catch { return 0; }
        }

        private static string MakeDynastyName(Kingdom pKingdom)
        {
            string name = pKingdom?.name ?? "";
            return string.IsNullOrEmpty(name)
                ? T("aw_hist_mandate_dynasty_default")
                : name + T("aw_hist_mandate_dynasty_suffix");
        }

        private static string CrisisLevel(int pValue)
        {
            if (pValue <= MIN_VALUE) return "collapse";
            if (pValue < 20) return "lost";
            if (pValue < 40) return "shaken";
            if (pValue >= 80) return "golden";
            return "stable";
        }

        private static string Signed(int pValue)
        {
            return pValue >= 0 ? "+" + pValue : pValue.ToString();
        }

        private static string EndReasonLabel(string pReason)
        {
            switch (pReason)
            {
                case "low_mandate": return T("aw_hist_mandate_end_low_mandate");
                case "war_lost": return T("aw_hist_mandate_end_war_lost");
                case "kingdom_fell": return T("aw_hist_mandate_end_kingdom_fell");
                case "replaced": return T("aw_hist_mandate_end_replaced");
                case "player_grant_replaced": return T("aw_hist_mandate_end_player_grant");
                default: return string.IsNullOrEmpty(pReason) ? T("aw_hist_mandate_end_generic") : pReason;
            }
        }

        private static string MarkerKind(string pOriginType, string pClaimantKind)
        {
            if (pOriginType == "pseudo_foreign" || pClaimantKind == "foreign_pseudo") return "pseudo_foreign";
            if (pOriginType == "rebel" || pClaimantKind == "rebel") return "rebel_claimant";
            return "moh";
        }

        private static bool IsPseudoForeignClaimant(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.MANDATE_ORIGIN_TYPE, out string origin, "");
            pKingdom.data.get(LineageKeys.MANDATE_CLAIMANT_KIND, out string claimant, "");
            if (origin == "pseudo_foreign" || claimant == "foreign_pseudo") return true;
            return !LineageService.IsXiaKingdom(pKingdom) && GetCoreControlRatioFor(pKingdom) >= RESTORE_CORE_THRESHOLD;
        }

        private static string GetWarType(War pWar)
        {
            try { return pWar?.getAsset()?.id ?? ""; }
            catch { return ""; }
        }

        private static void DirtyAllMaps()
        {
            try { MandateDynastyMapModeService.DirtyMapIfActive(); } catch { }
            try { MandateCoreMapModeService.DirtyMapIfActive(); } catch { }
        }

        private static int ToInt(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0 : Convert.ToInt32(pReader.GetValue(pIndex));
        }

        private static long ToLong(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? -1L : Convert.ToInt64(pReader.GetValue(pIndex));
        }

        private static double ToDouble(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? 0.0 : Convert.ToDouble(pReader.GetValue(pIndex));
        }

        private static string ToString(SQLiteDataReader pReader, int pIndex)
        {
            return pReader.IsDBNull(pIndex) ? "" : Convert.ToString(pReader.GetValue(pIndex));
        }
    }
}
