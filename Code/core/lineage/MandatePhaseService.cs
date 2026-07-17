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

        public static MandatePhase CurrentPhase => _phase;
        public static int CatalystScore => _catalystScore;
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

        public static void EvaluateActiveMandateYear(MandateReport pReport, int pYear,
            int pMandateValue, int pAuthority, int pAnnualMandateDelta)
        {
            if (!BeginAnnualEvaluation(pYear)) return;
            ApplyAnnualCatalyst(
                MandatePhaseRules.CatalystDeltaForMandateChange(pAnnualMandateDelta));

            bool stable = pMandateValue >= 70 && pAuthority >= 60 &&
                          _catalystScore <= 20 &&
                          !MandateRebelService.HasActiveRebelClaimants();
            _stableYears = stable ? Math.Min(999, _stableYears + 1) : 0;
            EvaluateAndPersist(pReport, pYear, true,
                pMandateValue, pAuthority, "active_year");
        }

        public static void ForceChaos(string pReason)
        {
            if (!EnsureLoaded()) return;
            int year = SafeCurrentYear();
            SetPhase(MandatePhase.Chaos, year);
            _lastYear = year;
            Persist(pReason ?? "forced_chaos");
        }

        public static void OnMandateEstablished(bool pHadPreviousMandate, int pYear)
        {
            if (!EnsureLoaded()) return;
            SetPhase(pHadPreviousMandate ? MandatePhase.Renewal : MandatePhase.Golden,
                pYear);
            _stableYears = 0;
            _lastYear = pYear;
            Persist(pHadPreviousMandate ? "mandate_renewal" : "first_mandate");
        }

        public static void AdjustCatalyst(int pDelta, string pReason)
        {
            if (pDelta == 0 || !EnsureLoaded()) return;
            int adjusted = MandatePhaseRules.AdjustCatalyst(_catalystScore, pDelta);
            if (adjusted == _catalystScore) return;
            _catalystScore = adjusted;
            Persist(pReason ?? "catalyst_changed");
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
            bool activeClaimants = MandateRebelService.HasActiveRebelClaimants();
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
            MandatePhase next = MandatePhaseRules.Evaluate(facts);
            SetPhase(next, pYear);
            Persist(pReason);
        }

        private static void SetPhase(MandatePhase pPhase, int pYear)
        {
            if (_phase == pPhase && _phaseSinceYear > UNSET_YEAR) return;
            _phase = pPhase;
            _phaseSinceYear = pYear;
            _stableYears = 0;
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
                        ColumnVal.Create("PHASE_LAST_YEAR", UNSET_YEAR));
                }

                using var command = new SQLiteCommand(DB);
                command.CommandText =
                    "SELECT MANDATE_PHASE,PHASE_SINCE_YEAR,PHASE_STABILITY_YEARS," +
                    "CATALYST_SCORE,PHASE_LAST_YEAR FROM " + table +
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
                }

                if (_phaseSinceYear <= UNSET_YEAR)
                    _phaseSinceYear = SafeCurrentYear();
                _loaded = true;
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
