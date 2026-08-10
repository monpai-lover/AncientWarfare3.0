using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.db;
using AncientWarfare3.utils;

namespace AncientWarfare3.core.lineage
{
    internal static class MandatePhaseService
    {
        private const long STATE_ID = 1L;
        private const int UNSET_YEAR = -999999;

        private static SQLiteConnection DB => LineageArchiveManager.Instance?.OperatingDB;
        private static bool Ready => DB != null &&
                                     LineageArchiveManager.Instance.InitializeSuccessful;

        private static bool _loaded;
        private static bool _warningLogged;
        private static MandatePhase _phase = MandatePhase.Golden;
        private static int _phaseSinceYear = UNSET_YEAR;
        private static int _stableYears;
        private static int _catalystScore;
        private static int _lastYear = UNSET_YEAR;
        private static int _chaosUnresolvedYears;
        private static int _chaosRecoveryYears;
        private static bool _mandateProtectionUsed;
        private static int _mandateProtectionUntilYear = UNSET_YEAR;

        public static MandatePhase CurrentPhase => _phase;
        public static int CatalystScore => _catalystScore;
        public static int ChaosUnresolvedYears => _chaosUnresolvedYears;
        public static int ChaosRecoveryYears => _chaosRecoveryYears;
        public static bool MandateProtectionUsed => _mandateProtectionUsed;
        public static int MandateProtectionUntilYear => _mandateProtectionUntilYear;
        public static int PhaseSinceYear => _phaseSinceYear <= UNSET_YEAR
            ? SafeCurrentYear()
            : _phaseSinceYear;
        public static float OccupationMultiplier =>
            MandatePhaseRules.OccupationMultiplier(_phase);
        public static bool CanContestMandate =>
            MandatePhaseRules.CanContestMandate(_phase);
        public static bool CanLaunchAutonomousRestoration =>
            MandatePhaseRules.CanLaunchAutonomousRestoration(_phase);

        public static void EvaluateVacantWorldYear(MandateReport pReport, int pYear)
        {
            if (!BeginAnnualEvaluation(pYear)) return;
            ApplyAnnualCatalyst(0);
            _stableYears = 0;
            EvaluateAndPersist(pReport, pYear, false,
                pReport?.mandate_value ?? 0, pReport?.imperial_authority ?? 0,
                "vacant_year");
        }

        public static bool EvaluateActiveMandateYear(MandateReport pReport, int pYear,
            int pMandateValue, int pAuthority, int pAnnualMandateDelta)
        {
            if (!BeginAnnualEvaluation(pYear)) return false;
            int courtDelta = MandatePoliticalCatalystService.CourtDelta(
                MandateService.GetCurrentMandateKingdom());
            ApplyAnnualCatalyst(
                MandatePhaseRules.CatalystDeltaForMandateChange(
                    pAnnualMandateDelta) + courtDelta);

            bool activeClaimants = ZhuluWarRules.HasActiveClaimants(
                MandateRebelService.HasActiveRebelClaimants(),
                ZhuluWarService.HasActivePrincipalWars());
            bool stable = pMandateValue >= 70 && pAuthority >= 60 &&
                          _catalystScore <= 20 &&
                          !activeClaimants;
            _stableYears = stable ? Math.Min(999, _stableYears + 1) : 0;
            EvaluateAndPersist(pReport, pYear, true,
                pMandateValue, pAuthority, "active_year");
            return EvaluateChaosLifecycle(pReport, pYear, pMandateValue,
                pAuthority);
        }

        public static void ForceChaos(string pReason)
        {
            if (!EnsureLoaded()) return;
            MandateReport report = MandateService.ReadReport();
            if (!MandatePhaseRules.CanForceChaos(
                    report?.period_id >= 0)) return;
            int year = SafeCurrentYear();
            SetPhase(MandatePhase.Chaos, year);
            _lastYear = year;
            Persist(pReason ?? "forced_chaos");
        }

        public static void OnMandateEstablished(bool pHadPreviousMandate, int pYear)
        {
            if (!EnsureLoaded()) return;
            SetPhase(MandatePhaseRules.PhaseAfterMandateEstablished(
                pHadPreviousMandate), pYear);
            _stableYears = 0;
            _chaosUnresolvedYears = 0;
            _chaosRecoveryYears = 0;
            _mandateProtectionUsed = false;
            _mandateProtectionUntilYear = UNSET_YEAR;
            _lastYear = pYear;
            Persist(pHadPreviousMandate ? "mandate_renewal" : "first_mandate");
        }

        public static void EnterRenewal(string pReason)
        {
            if (!EnsureLoaded()) return;
            int year = SafeCurrentYear();
            SetPhase(MandatePhase.Renewal, year);
            _catalystScore = MandatePhaseRules.AdjustCatalyst(
                _catalystScore, -10);
            _lastYear = year;
            Persist(pReason ?? "dynastic_renewal");
        }

        public static void AdjustCatalyst(int pDelta, string pReason)
        {
            if (pDelta == 0 || !EnsureLoaded()) return;
            int adjusted = MandatePhaseRules.AdjustCatalyst(_catalystScore, pDelta);
            if (adjusted == _catalystScore) return;
            _catalystScore = adjusted;
            int year = SafeCurrentYear();
            if (MandatePhaseRules.ShouldEnterChaosAfterCatalyst(
                    _phase, year, PhaseSinceYear, _catalystScore) &&
                MandatePhaseRules.CanForceChaos(
                    MandateService.ReadReport()?.period_id >= 0))
                SetPhase(MandatePhase.Chaos, year);
            Persist(pReason ?? "catalyst_changed");
        }

        public static MandateProtectionResolution ResolveCollapseProtection(
            bool pEligible, int pYear)
        {
            if (!EnsureLoaded())
                return MandateProtectionResolution.Collapse;
            MandateProtectionResolution resolution =
                MandateDeclineRules.ResolveProtection(pEligible,
                    _mandateProtectionUsed, pYear,
                    _mandateProtectionUntilYear);
            if (resolution == MandateProtectionResolution.StartGrace)
            {
                _mandateProtectionUsed = true;
                _mandateProtectionUntilYear = pYear + 4;
                Persist("mandate_protection_started");
            }
            return resolution;
        }

        public static bool IsCollapseProtectionActive(int pYear)
        {
            return EnsureLoaded() && _mandateProtectionUsed &&
                   pYear < _mandateProtectionUntilYear;
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            EnsureLoaded();
        }

        public static void ClearRuntime()
        {
            _loaded = false;
            _warningLogged = false;
            _phase = MandatePhase.Golden;
            _phaseSinceYear = UNSET_YEAR;
            _stableYears = 0;
            _catalystScore = 0;
            _lastYear = UNSET_YEAR;
            _chaosUnresolvedYears = 0;
            _chaosRecoveryYears = 0;
            _mandateProtectionUsed = false;
            _mandateProtectionUntilYear = UNSET_YEAR;
        }

        private static bool BeginAnnualEvaluation(int pYear)
        {
            if (!EnsureLoaded() || _lastYear == pYear) return false;
            _lastYear = pYear;
            return true;
        }

        private static void ApplyAnnualCatalyst(int pDelta)
        {
            int decay = MandatePhaseRules.AnnualCatalystDecay(_phase);
            _catalystScore = MandatePhaseRules.AdjustCatalyst(
                _catalystScore, pDelta - decay);
        }

        private static void EvaluateAndPersist(MandateReport pReport, int pYear,
            bool pMandateActive, int pMandateValue, int pAuthority, string pReason)
        {
            bool activeClaimants = ZhuluWarRules.HasActiveClaimants(
                MandateRebelService.HasActiveRebelClaimants(),
                ZhuluWarService.HasActivePrincipalWars());
            var facts = new MandatePhaseFacts(
                _phase,
                pYear,
                PhaseSinceYear,
                pReport?.period_id >= 0,
                pMandateActive,
                pMandateValue,
                pAuthority,
                activeClaimants,
                _catalystScore,
                _stableYears);
            MandatePhase previous = _phase;
            MandatePhase next = MandatePhaseRules.Evaluate(facts);
            SetPhase(next, pYear);
            if (MandatePhaseRules.IsRevivalTransition(previous, next))
            {
                Kingdom mandate = MandateService.GetCurrentMandateKingdom();
                EraChangeTriggerService.Mark(mandate,
                    EraChangeReason.EnteredRevival, "phase:revival:" + pYear);
            }
            Persist(pReason);
        }

        private static bool EvaluateChaosLifecycle(MandateReport pReport,
            int pYear, int pMandateValue, int pAuthority)
        {
            if (_phase != MandatePhase.Chaos)
            {
                if (_chaosUnresolvedYears == 0 && _chaosRecoveryYears == 0)
                    return false;
                _chaosUnresolvedYears = 0;
                _chaosRecoveryYears = 0;
                Persist("chaos_counters_reset");
                return false;
            }

            bool rebelClaimants = MandateRebelService.HasActiveRebelClaimants();
            bool activeZhuluWars = ZhuluWarService.HasActivePrincipalWars();
            bool unresolved = MandateDeclineRules.IsChaosUnresolved(
                pMandateValue, pReport?.core_control ?? 0f,
                rebelClaimants, activeZhuluWars, _catalystScore);
            bool recoveryYear = MandateDeclineRules.IsChaosRecoveryYear(
                pMandateValue, pAuthority, pReport?.core_control ?? 0f,
                rebelClaimants, activeZhuluWars, _catalystScore);

            _chaosUnresolvedYears = MandateDeclineRules.
                NextChaosUnresolvedYears(_chaosUnresolvedYears, unresolved);
            _chaosRecoveryYears = recoveryYear
                ? Math.Min(999, _chaosRecoveryYears + 1)
                : 0;

            if (MandateDeclineRules.ShouldRecoverChaos(pMandateValue,
                    pAuthority, pReport?.core_control ?? 0f,
                    rebelClaimants, activeZhuluWars, _catalystScore,
                    _chaosRecoveryYears))
            {
                SetPhase(MandatePhase.Decline, pYear);
                _chaosUnresolvedYears = 0;
                _chaosRecoveryYears = 0;
                Persist("chaos_recovered");
                return false;
            }

            Persist("chaos_lifecycle");
            return MandateDeclineRules.ShouldCollapseChaos(
                _chaosUnresolvedYears, unresolved);
        }

        private static void SetPhase(MandatePhase pPhase, int pYear)
        {
            if (_phase == pPhase && _phaseSinceYear > UNSET_YEAR) return;
            MandatePhase previous = _phase;
            _phase = pPhase;
            _phaseSinceYear = pYear;
            _stableYears = 0;
            if (previous != pPhase)
            {
                _chaosUnresolvedYears = 0;
                _chaosRecoveryYears = 0;
            }
            CentralizationService.OnPhaseChanged(previous, pPhase, pYear);
            MandateMilitaryPhaseService.OnPhaseChanged(previous, pPhase);
        }

        private static bool EnsureLoaded()
        {
            if (_loaded) return true;
            if (!Ready) return false;

            try
            {
                string table = MandateStateTableItem.GetTableName();
                if (!DB.CheckKeyExist(table,
                        SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID)))
                {
                    int year = SafeCurrentYear();
                    DB.Insert(table,
                        ColumnVal.Create("STATE_ID", STATE_ID),
                        ColumnVal.Create("MANDATE_PHASE", PhaseId(MandatePhase.Golden)),
                        ColumnVal.Create("PHASE_SINCE_YEAR", year),
                        ColumnVal.Create("PHASE_STABILITY_YEARS", 0),
                        ColumnVal.Create("CATALYST_SCORE", 0),
                        ColumnVal.Create("PHASE_LAST_YEAR", UNSET_YEAR),
                        ColumnVal.Create("CHAOS_UNRESOLVED_YEARS", 0),
                        ColumnVal.Create("CHAOS_RECOVERY_YEARS", 0),
                        ColumnVal.Create("MANDATE_PROTECTION_USED", 0),
                        ColumnVal.Create("MANDATE_PROTECTION_UNTIL_YEAR", UNSET_YEAR));
                }

                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT MANDATE_PHASE,PHASE_SINCE_YEAR,PHASE_STABILITY_YEARS," +
                    "CATALYST_SCORE,PHASE_LAST_YEAR,CHAOS_UNRESOLVED_YEARS," +
                    "CHAOS_RECOVERY_YEARS,MANDATE_PROTECTION_USED," +
                    "MANDATE_PROTECTION_UNTIL_YEAR FROM " + table +
                    " WHERE STATE_ID=@id LIMIT 1";
                command.Parameters.AddWithValue("@id", STATE_ID);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    _phase = ParsePhase(ReadString(reader, 0));
                    _phaseSinceYear = ReadInt(reader, 1, SafeCurrentYear());
                    _stableYears = Math.Max(0, ReadInt(reader, 2, 0));
                    _catalystScore = MandatePhaseRules.AdjustCatalyst(
                        ReadInt(reader, 3, 0), 0);
                    _lastYear = ReadInt(reader, 4, UNSET_YEAR);
                    _chaosUnresolvedYears = Math.Max(0, ReadInt(reader, 5, 0));
                    _chaosRecoveryYears = Math.Max(0, ReadInt(reader, 6, 0));
                    _mandateProtectionUsed = ReadInt(reader, 7, 0) != 0;
                    _mandateProtectionUntilYear = ReadInt(reader, 8, UNSET_YEAR);
                }

                if (_phaseSinceYear <= UNSET_YEAR)
                    _phaseSinceYear = SafeCurrentYear();
                _loaded = true;
                MandateReport report = MandateService.ReadReport();
                MandatePhase normalized = MandatePhaseRules.
                    NormalizeLoadedPhase(_phase, report?.period_id >= 0);
                if (normalized != _phase)
                {
                    SetPhase(normalized, SafeCurrentYear());
                    Persist("normalize_pre_mandate_chaos");
                }
                return true;
            }
            catch (Exception exception)
            {
                LogWarningOnce("Mandate phase load failed: " + exception.Message);
                return false;
            }
        }

        private static void Persist(string pReason)
        {
            if (!Ready || !_loaded) return;
            try
            {
                DB.UpdateValue(MandateStateTableItem.GetTableName(),
                    new List<SimpleColumnConstraint>
                    {
                        SimpleColumnConstraint.CreateEq("STATE_ID", STATE_ID)
                    },
                    ColumnVal.Create("MANDATE_PHASE", PhaseId(_phase)),
                    ColumnVal.Create("PHASE_SINCE_YEAR", _phaseSinceYear),
                    ColumnVal.Create("PHASE_STABILITY_YEARS", _stableYears),
                    ColumnVal.Create("CATALYST_SCORE", _catalystScore),
                    ColumnVal.Create("PHASE_LAST_YEAR", _lastYear),
                    ColumnVal.Create("CHAOS_UNRESOLVED_YEARS", _chaosUnresolvedYears),
                    ColumnVal.Create("CHAOS_RECOVERY_YEARS", _chaosRecoveryYears),
                    ColumnVal.Create("MANDATE_PROTECTION_USED", _mandateProtectionUsed ? 1 : 0),
                    ColumnVal.Create("MANDATE_PROTECTION_UNTIL_YEAR",
                        _mandateProtectionUntilYear),
                    ColumnVal.Create("UPDATED_TIME", LineageService.CurTime()));
            }
            catch (Exception exception)
            {
                LogWarningOnce("Mandate phase persist failed (" + pReason + "): " +
                               exception.Message);
            }
        }

        private static MandatePhase ParsePhase(string pValue)
        {
            return pValue switch
            {
                "decline" => MandatePhase.Decline,
                "chaos" => MandatePhase.Chaos,
                "renewal" => MandatePhase.Renewal,
                _ => MandatePhase.Golden
            };
        }

        private static string PhaseId(MandatePhase pPhase)
        {
            return pPhase switch
            {
                MandatePhase.Decline => "decline",
                MandatePhase.Chaos => "chaos",
                MandatePhase.Renewal => "renewal",
                _ => "golden"
            };
        }

        private static int ReadInt(SQLiteDataReader pReader, int pIndex, int pFallback)
        {
            try
            {
                return pReader.IsDBNull(pIndex)
                    ? pFallback
                    : Convert.ToInt32(pReader.GetValue(pIndex));
            }
            catch
            {
                return pFallback;
            }
        }

        private static string ReadString(SQLiteDataReader pReader, int pIndex)
        {
            try
            {
                return pReader.IsDBNull(pIndex) ? "" : pReader.GetString(pIndex);
            }
            catch
            {
                return "";
            }
        }

        private static int SafeCurrentYear()
        {
            try
            {
                return Date.getCurrentYear();
            }
            catch
            {
                return 0;
            }
        }

        private static void LogWarningOnce(string pMessage)
        {
            if (_warningLogged) return;
            _warningLogged = true;
            ModClass.LogWarning(pMessage);
        }
    }
}
