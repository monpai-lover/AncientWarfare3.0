using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRuntime
    {
        private static int _lastQuarterKey = -1;
        private static bool _loaded;

        // 学派是 authority 唯一的大头(实测 32.5 / 89.9 / 79.3ms,第二名才 15ms),
        // 但 ProcessFrameCore 里十来个服务背靠背跑,只有总数没有分布。已有的
        // EndDetail 是按帧取「最慢项」,发日志的采样帧上通常什么都没在跑,所以
        // 看不出账。这里按子步骤跨帧累计、发日志时取走 —— 和 authority_steps
        // 同一套做法。纯观测:不改顺序、不改分支。
        private static readonly Dictionary<string, long[]> StepCost =
            new Dictionary<string, long[]>(StringComparer.Ordinal);

        private static void Step(string pId, Action pAction)
        {
            long started = Stopwatch.GetTimestamp();
            try { pAction(); }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                if (!StepCost.TryGetValue(pId, out long[] entry))
                {
                    entry = new long[2];
                    StepCost[pId] = entry;
                }

                entry[0] += elapsed;
                entry[1]++;
            }
        }

        internal static string TakeStepDiagnostics()
        {
            if (StepCost.Count == 0) return "none";
            var ranked = new List<KeyValuePair<string, long[]>>(StepCost);
            StepCost.Clear();
            ranked.Sort((left, right) =>
            {
                int byTicks = right.Value[0].CompareTo(left.Value[0]);
                return byTicks != 0
                    ? byTicks
                    : string.CompareOrdinal(left.Key, right.Key);
            });
            int limit = Math.Min(14, ranked.Count);
            var parts = new string[limit];
            for (int i = 0; i < limit; i++)
                parts[i] = ranked[i].Key + ":" +
                    (ranked[i].Value[0] * 1000.0 / Stopwatch.Frequency)
                        .ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture) +
                    "/" + ranked[i].Value[1];
            return string.Join(",", parts);
        }

        public static int EligibleYear => HistoricalSchoolScheduler.EligibleYear;
        internal static bool IsLoaded => _loaded;

        public static void LoadState()
        {
            SchoolMembershipService.LoadIndexes();
            HistoricalSchoolStore.ClearLedgerReadCache();
            HistoricalSchoolAcademyConstructionService.ClearRuntime();
            HistoricalSchoolAcademyLifecycleService.ClearRuntime();
            HistoricalSchoolStore.LoadRuntimeState(
                out int eligibleYear, out int lastWorldYear);
            HistoricalSchoolScheduler.RestorePersistentState(
                eligibleYear, lastWorldYear);
            HistoricalSchoolAcademyRepairService.LoadState();
            HistoricalAffiliationService.LoadState();
            HistoricalAffiliationService.RepairShortServingTerms();
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
            HistoricalSchoolAcademyLifecycleService.ClearRuntime();
            HistoricalSchoolAcademyRepairService.ClearRuntime();
            HistoricalSchoolRuntimeIndex.Instance.ClearLivingXiaCities();
            _loaded = false;
        }

        public static void ProcessFrame()
        {
            if (World.world == null) return;
            if (AWAsyncRuntime.State != AWAsyncLifecycleState.Running) return;
            if (!_loaded) return;
            ProcessFrameCore();
        }

        // Native simulation mode does not enter AWAuthorityCycleService. Keep
        // the school lifecycle alive from the vanilla MapBox.Update hook.
        public static void ProcessVanillaFrame()
        {
            if (World.world == null) return;
            if (!_loaded)
            {
                try { LoadState(); }
                catch (Exception error)
                {
                    LogAnnualStageFailure("native_bootstrap", error);
                    return;
                }
            }
            ProcessFrameCore();
        }

        private static void ProcessFrameCore()
        {
            // AW3's cooperative runner advances world time without calling
            // MapBox.updateObjectAge. Enqueue here as a path-independent
            // fallback; the scheduler state coalesces duplicate years.
            Step("world_year", EnqueueWorldYear);
            Step("academy_rebuild", () =>
                HistoricalSchoolAcademyConstructionService
                    .ProcessPendingRebuilds());
            Step("scheduler", () => HistoricalSchoolScheduler.ProcessFrame());
            Step("load_recovery", () =>
                HistoricalSchoolEducationJourneyService
                    .ProcessLoadRecoveryFrame());
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Step("descent", () => HistoricalSchoolDescentService
                    .ProcessPendingDescentReconciliations());
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_descent",
                    diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Step("guest_office",
                    () => SchoolGuestOfficeService.ProcessPendingFrame());
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail("school_guest_office",
                    diagnostic);
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Step("deferred_actions",
                    () => HistoricalSchoolActionService.ProcessDeferredFrame());
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "school_deferred_actions", diagnostic);
            }
            Step("activity_queue",
                () => HistoricalSchoolActivityQueue.ProcessFrame());
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Step("write_buffer",
                    () => HistoricalSchoolWriteBufferService.ProcessFrame());
            }
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
                    Step("travel_quarter", () =>
                        HistoricalSchoolTravelService.ProcessQuarter(
                            quarterKey));
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "school_travel_quarter", diagnostic);
                }
            }
            diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                Step("travel_frame",
                    () => HistoricalSchoolTravelService.ProcessFrame());
            }
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
            if (HistoricalSchoolScheduler.EnqueueYear(Date.getCurrentYear()))
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
