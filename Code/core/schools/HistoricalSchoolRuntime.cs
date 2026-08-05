using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRuntime
    {
        private static int _lastQuarterKey = -1;
        private static bool _loaded;

        public static int EligibleYear => HistoricalSchoolScheduler.EligibleYear;
        internal static bool IsLoaded => _loaded;

        public static void LoadState()
        {
            SchoolMembershipService.LoadIndexes();
            HistoricalSchoolStore.ClearLedgerReadCache();
            HistoricalSchoolAcademyConstructionService.ClearRuntime();
            HistoricalSchoolStore.LoadRuntimeState(
                out int eligibleYear, out int lastWorldYear);
            HistoricalSchoolScheduler.RestorePersistentState(
                eligibleYear, lastWorldYear);
            HistoricalAffiliationService.LoadState();
            HistoricalSchoolDescentService.LoadState();
            HistoricalAffiliationService.EnsureMembershipAffiliations();
            SchoolLineageService.LoadState();
            HistoricalSchoolEliteEnrollmentService.ClearRuntime();
            HistoricalSchoolEducationJourneyService.ClearRuntime();
            HistoricalSchoolPopulationRecoveryService.ClearRuntime();
            HistoricalSchoolActionService.ClearRuntime();
            HistoricalSchoolActivityQueue.LoadState();
            SchoolGuestOfficeService.LoadState();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolEducationJourneyService.BeginLoadRecovery();
            HistoricalSchoolDebateService.LoadState();
            RebuildLivingXiaCityIndex();
            _lastQuarterKey = -1;
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            HistoricalSchoolDiagnostics.ClearRuntime();
            HistoricalSchoolStore.ClearLedgerReadCache();
            _lastQuarterKey = -1;
            HistoricalSchoolScheduler.Clear();
            SchoolMembershipService.ClearRuntime();
            HistoricalAffiliationService.ClearRuntime();
            SchoolLineageService.ClearRuntime();
            HistoricalSchoolEliteEnrollmentService.ClearRuntime();
            HistoricalSchoolEducationJourneyService.ClearRuntime();
            HistoricalSchoolPopulationRecoveryService.ClearRuntime();
            HistoricalSchoolActionService.ClearRuntime();
            HistoricalSchoolActivityQueue.ClearRuntime();
            HistoricalSchoolWriteBufferService.Clear();
            SchoolGuestOfficeService.ClearRuntime();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.ClearRuntime();
            HistoricalSchoolDescentService.ClearRuntime();
            HistoricalSchoolAcademyConstructionService.ClearRuntime();
            HistoricalSchoolRuntimeIndex.Instance.ClearLivingXiaCities();
            _loaded = false;
        }

        public static void ProcessFrame()
        {
            if (World.world == null) return;
            HistoricalSchoolAcademyConstructionService.ProcessPendingRebuilds();
            HistoricalSchoolScheduler.ProcessFrame();
            if (!_loaded) return;
            HistoricalSchoolEducationJourneyService.
                ProcessLoadRecoveryFrame();
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                HistoricalSchoolDescentService.
                    ProcessPendingDescentReconciliations();
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_descent",
                    diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { SchoolGuestOfficeService.ProcessPendingFrame(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_guest_office",
                    diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { HistoricalSchoolActionService.ProcessDeferredFrame(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "school_deferred_actions", diagnostic);
            }
            HistoricalSchoolActivityQueue.ProcessFrame();
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { HistoricalSchoolWriteBufferService.ProcessFrame(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_write_buffer",
                    diagnostic);
            }
            int month = Math.Max(1, Math.Min(12, Date.getCurrentMonth()));
            int quarterKey = Date.getCurrentYear() * 4 + (month - 1) / 3;
            if (quarterKey != _lastQuarterKey)
            {
                _lastQuarterKey = quarterKey;
                diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try
                {
                    HistoricalSchoolTravelService.ProcessQuarter(quarterKey);
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "school_travel_quarter", diagnostic);
                }
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { HistoricalSchoolTravelService.ProcessFrame(); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_travel_frame",
                    diagnostic);
            }
        }

        internal static bool FlushPendingStateForSave()
        {
            return HistoricalSchoolScheduler.FlushPendingStateForSave();
        }

        public static void EnqueueWorldYear()
        {
            long started = Stopwatch.GetTimestamp();
            HistoricalSchoolScheduler.EnqueueYear(Date.getCurrentYear());
            HistoricalSchoolDiagnostics.RecordYearEnqueue(
                Stopwatch.GetTimestamp() - started);
        }

        internal static List<City> LivingXiaCities()
        {
            var result = new List<City>();
            if (World.world?.cities == null) return result;
            foreach (long cityId in
                     HistoricalSchoolRuntimeIndex.Instance.LivingXiaCityIds())
            {
                City city = null;
                try { city = World.world.cities.get(cityId); }
                catch { }
                if (IsLivingCity(city)) result.Add(city);
                else HistoricalSchoolRuntimeIndex.Instance.SetLivingXiaCity(cityId, false);
            }
            return result;
        }

        internal static void RefreshLivingXiaCity(City pCity)
        {
            long cityId = pCity?.data?.id ?? -1L;
            if (cityId < 0) return;
            HistoricalSchoolRuntimeIndex.Instance.SetLivingXiaCity(
                cityId, IsLivingXiaCity(pCity));
        }

        private static void RebuildLivingXiaCityIndex()
        {
            HistoricalSchoolRuntimeIndex.Instance.ClearLivingXiaCities();
            if (World.world?.cities == null) return;
            foreach (City city in World.world.cities) RefreshLivingXiaCity(city);
        }

        private static bool IsLivingXiaCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return IsLivingCity(pCity) &&
                   (LineageService.IsXiaKingdom(kingdom) ||
                    XiaizationService.IsFullyXiaizedCity(pCity));
        }

        private static bool IsLivingCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return pCity?.data != null && !pCity.isRekt() &&
                   kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral();
        }

        internal static void LogAnnualStageFailure(string pStageId, Exception error)
        {
            ModClass.LogWarning("Historical school annual stage failed [" + pStageId +
                                "]: " + error.ToString());
        }
    }
}
