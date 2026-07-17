using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
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
        public string dynasty_name = "";
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

        public static bool Exists => GetCurrentMandateKingdom() != null;

        private static HistoryText H(string pKey) => HistoryLocalizationRules.H(pKey);
        private static string T(string pKey) => HistoryLocalizationRules.Text(pKey);

        public static bool IsMandateKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && GetCurrentMandateKingdom()?.id == pKingdom.id;
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
            _cacheDirty = true;
            ReadReport();
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

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;

            if (!Exists)
            {
                MandatePhaseService.EvaluateVacantWorldYear(
                    ReadReport(), Date.getCurrentYear());
                if (MandateRebelService.HasActiveRebelClaimants()) return;
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
            MandatePhaseService.EvaluateActiveMandateYear(
                ReadReport(), currentYear, nextValue, authority, delta);

            if (Mathf.Abs(delta) >= 5 || crisis == "collapse" || crisis == "lost")
                RecordEvent("mandate_yearly", pKingdom, pKingdom.king, null, delta, nextValue,
                    pKingdom.name + T("aw_hist_mandate_changed_mid") + Signed(delta) +
                    T("aw_hist_mandate_current") + nextValue);

            if (nextValue <= MIN_VALUE)
            {
                if (HasMandateProtection(pKingdom))
                {
                    RecordEvent("mandate_protected", pKingdom, pKingdom.king, null, 0, nextValue,
                        pKingdom.king.getName() + T("aw_hist_mandate_protected"));
                }
                else
                {
                    CollapseMandate(pKingdom, "low_mandate");
                }
            }
        }

        public static bool TryDeclareMandate(Kingdom pKingdom, string pReason = "decision",
            string pOriginType = "native", string pClaimantKind = "orthodox", Kingdom pRebelOrigin = null)
        {
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
            if (!CanDeclareMandateForOrigin(pKingdom, pReason, pOriginType, pClaimantKind, out _)) return false;
            if (!Ready) return false;

            MandateReport previousReport = ReadReport();
            bool hadPreviousMandate = previousReport.period_id >= 0;
            if (!MandateDeclarationRules.CanCreateNewPeriod(
                    previousReport.active, previousReport.kingdom_id, pKingdom.id))
                return false;
            long previousPeriodId = previousReport.active ? previousReport.period_id : -1L;
            Kingdom old = GetCurrentMandateKingdom();
            if (old?.data != null && old != pKingdom)
                ClearMandate("replaced");

            long periodId = TableIdAllocator.Next(DB, MandatePeriodTableItem.GetTableName(), "PERIOD_ID");
            double now = LineageService.CurTime();
            Actor king = pKingdom.king;
            HeirService.EnsureLegitimateLine(pKingdom, king);
            string dynastyName = MakeDynastyName(pKingdom);

            DB.Insert(MandatePeriodTableItem.GetTableName(),
                ColumnVal.Create("PERIOD_ID", periodId),
                ColumnVal.Create("KINGDOM_ID", pKingdom.id),
                ColumnVal.Create("KINGDOM_NAME", pKingdom.name ?? ""),
                ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                ColumnVal.Create("DYNASTY_NAME", dynastyName),
                ColumnVal.Create("FOUNDER_ACTOR_ID", king?.data?.id ?? -1L),
                ColumnVal.Create("FOUNDER_NAME", king?.getName() ?? ""),
                ColumnVal.Create("START_TIME", now),
                ColumnVal.Create("END_TIME", -1.0),
                ColumnVal.Create("END_REASON", ""),
                ColumnVal.Create("START_MANDATE", START_VALUE),
                ColumnVal.Create("END_MANDATE", START_VALUE),
                ColumnVal.Create("LEGAL_CORE_COUNT", 0),
                ColumnVal.Create("ORIGIN_TYPE", pOriginType ?? "native"),
                ColumnVal.Create("REBEL_ORIGIN_KINGDOM_ID", pRebelOrigin?.id ?? -1L),
                ColumnVal.Create("REBEL_ORIGIN_KINGDOM_NAME", pRebelOrigin?.name ?? ""),
                ColumnVal.Create("CLAIMANT_KIND", pClaimantKind ?? "orthodox"));

            UpsertState(pKingdom, periodId, START_VALUE, 45, 0, 1f, 1f, "stable", Date.getCurrentYear(), now,
                pOriginType, pClaimantKind, pRebelOrigin, MarkerKind(pOriginType, pClaimantKind));
            pKingdom.data.set(LineageKeys.MANDATE_PERIOD_ID, periodId);
            pKingdom.data.set(LineageKeys.MANDATE_VALUE, START_VALUE);
            pKingdom.data.set(LineageKeys.MANDATE_AUTHORITY, 45);
            pKingdom.data.set(LineageKeys.MANDATE_ORIGIN_TYPE, pOriginType ?? "native");
            pKingdom.data.set(LineageKeys.MANDATE_CLAIMANT_KIND, pClaimantKind ?? "orthodox");
            pKingdom.data.set(LineageKeys.MANDATE_MAP_MARKER_KIND, MarkerKind(pOriginType, pClaimantKind));
            MandatePhaseService.OnMandateEstablished(
                hadPreviousMandate, Date.getCurrentYear());
            if (pOriginType == "self_restoration")
            {
                pKingdom.data.set(LineageKeys.RESTORATION_REFUNDER_ELIGIBLE, false);
                RulerTitleRestorationStateService.MarkMandateRegained(pKingdom);
            }
            if (pOriginType == "pseudo_foreign" || pClaimantKind == "foreign_pseudo")
                XiaizationService.OnPseudoMandateDeclared(pKingdom);
            bool wasAlreadyEmperor = KingdomTitleService.IsEmperor(pKingdom);
            KingdomTitleService.SetTitle(pKingdom, KingdomTitle.Emperor);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            if (king != null && !king.hasTrait(TRAIT_TIANMING)) king.addTrait(TRAIT_TIANMING);
            if (wasAlreadyEmperor &&
                (hadPreviousMandate || pOriginType == "self_restoration"))
                EraChangeTriggerService.Mark(pKingdom,
                    EraChangeReason.RestoredMandate, "mandate:" + periodId);
            CreateLegalCores(pKingdom, periodId, previousPeriodId);
            UpdateOriginalCoreCount(periodId);

            string startEventType = MandateStartRecordRules.EventType(pOriginType, pClaimantKind);
            RecordEvent(startEventType, pKingdom, king, null, 0, START_VALUE,
                pKingdom.name + T("aw_hist_mandate_claimed_mid") + dynastyName);
            HistoryWriter.RecordKingdom(pKingdom, startEventType,
                HistoryText.Kingdom(pKingdom) + H("aw_hist_mandate_claimed_mid") + HistoryText.PlainText(dynastyName),
                HistoryTarget.Kingdom(pKingdom));
            if (king?.data != null)
                HistoryWriter.RecordPerson(king.data.id, pKingdom, king.getName(), startEventType,
                    HistoryText.Actor(king) + H("aw_hist_actor_claimed_mandate"), ChronicleCategory.HONOR,
                    HistoryTarget.Kingdom(pKingdom));

            DirtyAllMaps();
            return true;
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
            MandateDeclarationSource source = MandateRitesRules.ResolveSource(
                pDeclarationReason, pOriginType, pClaimantKind);
            bool rebelOrigin = source == MandateDeclarationSource.MandateRebel;
            bool foreignPseudo = source == MandateDeclarationSource.ForeignPseudoDynasty;
            bool successfulOrdinaryWar = source == MandateDeclarationSource.MandateWarVictory;
            if (successfulOrdinaryWar)
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
            return ApplySacrificeOutcome(pKingdom, pEffects, pReason, null);
        }

        internal static bool ApplySacrificeOutcome(Kingdom pKingdom,
            MandateSacrificeEffects pEffects, string pReason, string pContent)
        {
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
            Actor king = pKingdom?.king;
            return king?.data != null && (king.hasTrait("first") || king.hasTrait("figure"));
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

        public static void OnCityTransferred(City pCity)
        {
            if (pCity?.data == null || pCity.isRekt()) return;
            if (_cacheDirty || _cachedReport == null) return;
            if (!MandateCoreTransferRules.ShouldInvalidate(
                    _cachedReport.period_id >= 0, _coreCityIds.Contains(pCity.id))) return;

            MarkDirty();
            MandateCoreMapModeService.DirtyMapIfActive();
        }

        public static void OnKingdomCoreCreated(Kingdom pKingdom, City pCity, string pSourceType)
        {
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
            Kingdom current = GetCurrentMandateKingdom();
            MandateReport report = ReadReport();
            if (!Ready || !report.active) return;

            RulerTitleRestorationStateService.MarkMandateLost(current);

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

            PublishRuntimeMarkerProjection(false, -1L, "");

            if (current?.king != null && current.king.hasTrait(TRAIT_TIANMING))
                current.king.removeTrait(TRAIT_TIANMING);

            if (current?.data != null)
            {
                HistoryWriter.RecordKingdom(current, "mandate_end",
                    HistoryText.Kingdom(current) + H("aw_hist_mandate_lost_prefix") +
                    HistoryText.PlainText(EndReasonLabel(pReason)) + H("aw_hist_paren_close"),
                    HistoryTarget.Kingdom(current));
                RecordEvent("mandate_end", current, current.king, null, 0, report.mandate_value,
                    current.name + T("aw_hist_mandate_lost_prefix") + EndReasonLabel(pReason) +
                    T("aw_hist_paren_close"));
            }

            RulerAppellationService.RefreshLivingProjection(current);

            MarkDirty();
            DirtyAllMaps();
        }

        public static void CollapseMandate(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null) return;
            MandatePhaseService.ForceChaos("mandate_collapse");
            HistoryWriter.RecordKingdom(pKingdom, "mandate_collapse",
                HistoryText.Kingdom(pKingdom) + H("aw_hist_mandate_collapse"),
                HistoryTarget.Kingdom(pKingdom));
            MandateRebelService.OnMandateCollapse(pKingdom, pReason);
            ClearMandate(pReason);
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            if (type != WAR_TIANMING && type != WAR_TIANMING_REBEL) return;

            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;
            RecordEvent("mandate_war_start", defender, defender.king, null, -5, ReadReport().mandate_value,
                attacker.name + T("aw_hist_mandate_war_declared_mid") + defender.name +
                T("aw_hist_mandate_war_declared_suffix"));
            ChangeMandate(defender, -5, "mandate_war_start");
            MandateBorderDefenseService.OnMandateWarStarted(pWar);
        }

        public static void OnWarEnded(War pWar, WarWinner pWinner)
        {
            if (pWar?.data == null) return;
            string type = GetWarType(pWar);
            if (type != WAR_TIANMING && type != WAR_TIANMING_REBEL) return;

            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            Kingdom mandate = GetCurrentMandateKingdom();
            if (attacker?.data == null || defender?.data == null || mandate?.data == null) return;

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

        public static void OnKingdomDestroyed(Kingdom pKingdom)
        {
            Kingdom mandate = GetCurrentMandateKingdom();
            if (mandate != null && pKingdom == mandate)
            {
                RulerTitleRestorationStateService.MarkMandateLost(pKingdom);
                MandatePhaseService.ForceChaos("mandate_kingdom_fell");
                ClearMandate("kingdom_fell");
            }
        }

        public static void NormalizeMapMarkerAfterRebelSettlement(Kingdom pKingdom)
        {
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
            var ids = new List<long>();
            var powers = new List<float>();
            var eligible = new List<bool>();

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                ids.Add(kingdom?.id ?? -1L);
                bool canCompete = IsAutoMandateCandidateBaseEligible(kingdom, last);
                eligible.Add(canCompete);
                powers.Add(canCompete ? CalculateMandateCompetitionPower(kingdom) : 0f);
            }

            int winner = MandatePowerRules.SelectWinningCandidateIndex(powers.ToArray(), eligible.ToArray());
            if (winner >= 0 && winner < ids.Count)
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
            MandateReport r = ReadReport();
            if (!r.active || pKingdom?.data == null || pKingdom.id != r.kingdom_id) return;
            MandatePhaseService.AdjustCatalyst(
                MandatePhaseRules.CatalystDeltaForMandateChange(pDelta), pEventType);
            int next = Mathf.Clamp(r.mandate_value + pDelta, MIN_VALUE, MAX_VALUE);
            UpdateState(pKingdom, r.period_id, next, r.imperial_authority, r.dynasty_prestige, r.core_control,
                r.vassal_loyalty, CrisisLevel(next), Date.getCurrentYear());
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

        private static MandateReport ReadReportFromDb()
        {
            var report = new MandateReport();
            if (!Ready) return report;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT ACTIVE,KINGDOM_ID,KINGDOM_NAME,DYNASTY_NAME,EMPEROR_NAME,PERIOD_ID," +
                                  "MANDATE_VALUE,IMPERIAL_AUTHORITY,DYNASTY_PRESTIGE,CORE_CONTROL,VASSAL_LOYALTY,CRISIS_LEVEL," +
                                  "ORIGIN_TYPE,ORIGINAL_CORE_COUNT,REBEL_ORIGIN_KINGDOM_ID,REBEL_ORIGIN_KINGDOM_NAME,CLAIMANT_KIND,MAP_MARKER_KIND " +
                                  "FROM " + MandateStateTableItem.GetTableName() + " WHERE STATE_ID=@id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", STATE_ID);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return report;
                report.active = ToInt(reader, 0) == 1;
                report.kingdom_id = ToLong(reader, 1);
                report.kingdom_name = ToString(reader, 2);
                report.dynasty_name = ToString(reader, 3);
                report.emperor_name = ToString(reader, 4);
                report.period_id = ToLong(reader, 5);
                report.mandate_value = ToInt(reader, 6);
                report.imperial_authority = ToInt(reader, 7);
                report.dynasty_prestige = ToInt(reader, 8);
                report.core_control = (float)ToDouble(reader, 9);
                report.vassal_loyalty = (float)ToDouble(reader, 10);
                report.crisis_level = ToString(reader, 11);
                report.origin_type = ToString(reader, 12);
                report.original_core_count = ToInt(reader, 13);
                report.rebel_origin_kingdom_id = ToLong(reader, 14);
                report.rebel_origin_kingdom_name = ToString(reader, 15);
                report.claimant_kind = ToString(reader, 16);
                report.map_marker_kind = ToString(reader, 17);
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

        private static void CreateLegalCores(Kingdom pKingdom, long pPeriodId, long pPreviousPeriodId)
        {
            if (!Ready || pKingdom?.data == null) return;
            int count = 0;
            var inserted = new HashSet<long>();

            foreach (CoreCitySnapshot core in ReadCoreCitySnapshots(pPreviousPeriodId))
            {
                if (!MandateLegalCoreInheritanceRules.ShouldInheritPreviousCore(
                        pPreviousPeriodId, core.city_id, inserted.Contains(core.city_id)))
                    continue;
                if (!InsertLegalCore(pPeriodId, core.city_id, core.city_name, core.original_kingdom_id,
                        core.original_kingdom_name, core.original_kingdom_color, "inherited"))
                    continue;
                inserted.Add(core.city_id);
                count++;
            }

            foreach (City city in pKingdom.getCities())
            {
                if (city?.data == null || city.isRekt()) continue;
                if (!MandateLegalCoreInheritanceRules.ShouldAddFoundingCore(city.id, inserted.Contains(city.id)))
                    continue;
                if (!InsertLegalCore(pPeriodId, city.id, city.data.name ?? "", pKingdom.id, pKingdom.name ?? "",
                        HistoryColors.FromKingdom(pKingdom), "founding"))
                    continue;
                inserted.Add(city.id);
                count++;
            }

            DB.UpdateValue(MandatePeriodTableItem.GetTableName(),
                new List<SimpleColumnConstraint> { SimpleColumnConstraint.CreateEq("PERIOD_ID", pPeriodId) },
                ColumnVal.Create("LEGAL_CORE_COUNT", count));
            MarkDirty();
        }

        private static bool InsertLegalCore(long pPeriodId, long pCityId, string pCityName, long pOriginalKingdomId,
            string pOriginalKingdomName, string pOriginalKingdomColor, string pCoreType)
        {
            if (!Ready || pPeriodId < 0 || pCityId < 0) return false;
            try
            {
                long coreId = TableIdAllocator.Next(DB, MandateCoreCityTableItem.GetTableName(), "CORE_ID");
                DB.Insert(MandateCoreCityTableItem.GetTableName(),
                    ColumnVal.Create("CORE_ID", coreId),
                    ColumnVal.Create("PERIOD_ID", pPeriodId),
                    ColumnVal.Create("CITY_ID", pCityId),
                    ColumnVal.Create("CITY_NAME", pCityName ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_ID", pOriginalKingdomId),
                    ColumnVal.Create("ORIGINAL_KINGDOM_NAME", pOriginalKingdomName ?? ""),
                    ColumnVal.Create("ORIGINAL_KINGDOM_COLOR", HistoryColors.Normalize(pOriginalKingdomColor)),
                    ColumnVal.Create("CORE_TYPE", string.IsNullOrEmpty(pCoreType) ? "founding" : pCoreType),
                    ColumnVal.Create("ADDED_TIME", LineageService.CurTime()),
                    ColumnVal.Create("ACTIVE", 1));
                return true;
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate legal core insert failed: " + e.Message);
                return false;
            }
        }

        private static List<CoreCitySnapshot> ReadCoreCitySnapshots(long pPeriodId)
        {
            var result = new List<CoreCitySnapshot>();
            if (!Ready || pPeriodId < 0) return result;
            try
            {
                using var cmd = new SQLiteCommand(DB);
                cmd.CommandText = "SELECT CITY_ID, CITY_NAME, ORIGINAL_KINGDOM_ID, ORIGINAL_KINGDOM_NAME, " +
                                  "ORIGINAL_KINGDOM_COLOR FROM " + MandateCoreCityTableItem.GetTableName() +
                                  " WHERE PERIOD_ID=@p AND ACTIVE=1 ORDER BY CORE_ID ASC";
                cmd.Parameters.AddWithValue("@p", pPeriodId);
                using SQLiteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new CoreCitySnapshot
                    {
                        city_id = ToLong(reader, 0),
                        city_name = ToString(reader, 1),
                        original_kingdom_id = ToLong(reader, 2),
                        original_kingdom_name = ToString(reader, 3),
                        original_kingdom_color = ToString(reader, 4)
                    });
                }
            }
            catch { }
            return result;
        }

        private struct CoreCitySnapshot
        {
            public long city_id;
            public string city_name;
            public long original_kingdom_id;
            public string original_kingdom_name;
            public string original_kingdom_color;
        }

        private static void UpdateOriginalCoreCount(long pPeriodId)
        {
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

        private static void RecordEvent(string pType, Kingdom pKingdom, Actor pActor, City pCity, int pDelta,
            int pMandate, string pContent)
        {
            if (!Ready) return;
            MandateReport report = ReadReport();
            try
            {
                long eventId = TableIdAllocator.Next(DB, MandateEventTableItem.GetTableName(), "EVENT_ID");
                double now = LineageService.CurTime();
                DB.Insert(MandateEventTableItem.GetTableName(),
                    ColumnVal.Create("EVENT_ID", eventId),
                    ColumnVal.Create("PERIOD_ID", report.period_id),
                    ColumnVal.Create("EVENT_TYPE", pType ?? ""),
                    ColumnVal.Create("KINGDOM_ID", pKingdom?.id ?? -1L),
                    ColumnVal.Create("KINGDOM_NAME", pKingdom?.name ?? ""),
                    ColumnVal.Create("KINGDOM_COLOR", HistoryColors.FromKingdom(pKingdom)),
                    ColumnVal.Create("ACTOR_ID", pActor?.data?.id ?? -1L),
                    ColumnVal.Create("ACTOR_NAME", pActor?.getName() ?? ""),
                    ColumnVal.Create("CITY_ID", pCity?.id ?? -1L),
                    ColumnVal.Create("CITY_NAME", pCity?.data?.name ?? ""),
                    ColumnVal.Create("WORLD_TIME", now),
                    ColumnVal.Create("YEAR_PREFIX", HistoryWriter.BuildYearPrefix(now, pKingdom)),
                    ColumnVal.Create("VALUE_DELTA", pDelta),
                    ColumnVal.Create("MANDATE_VALUE", pMandate),
                    ColumnVal.Create("IMPERIAL_AUTHORITY", report.imperial_authority),
                    ColumnVal.Create("CONTENT", pContent ?? ""));
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Mandate event failed: " + e.Message);
            }
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
