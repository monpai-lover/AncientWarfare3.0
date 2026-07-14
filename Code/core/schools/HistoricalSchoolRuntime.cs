using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRuntime
    {
        private static int _eligibleYear;
        private static int _lastWorldYear = -1;
        private static int _attemptedWorldYear = -1;
        private static int _lastQuarterKey = -1;
        private static bool _loaded;
        private static readonly HistoricalSchoolPendingRuntimeState PendingRuntimeState =
            new HistoricalSchoolPendingRuntimeState();
        private static readonly HistoricalSchoolBootstrapRetryGate BootstrapRetryGate =
            new HistoricalSchoolBootstrapRetryGate();

        public static int EligibleYear => _eligibleYear;

        public static void LoadState()
        {
            HistoricalSchoolStore.LoadRuntimeState(out _eligibleYear, out _lastWorldYear);
            HistoricalAffiliationService.LoadState();
            HistoricalSchoolDescentService.LoadState();
            HistoricalAffiliationService.EnsureMembershipAffiliations();
            SchoolLineageService.LoadState();
            HistoricalSchoolActionService.ClearRuntime();
            HistoricalSchoolActivityQueue.LoadState();
            SchoolGuestOfficeService.LoadState();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.LoadState();
            RebuildLivingXiaCityIndex();
            _lastQuarterKey = -1;
            PendingRuntimeState.Clear();
            BootstrapRetryGate.RecordSuccess();
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            _eligibleYear = 0;
            _lastWorldYear = -1;
            _attemptedWorldYear = -1;
            _lastQuarterKey = -1;
            PendingRuntimeState.Clear();
            BootstrapRetryGate.Clear();
            HistoricalAffiliationService.ClearRuntime();
            SchoolLineageService.ClearRuntime();
            HistoricalSchoolActionService.ClearRuntime();
            HistoricalSchoolActivityQueue.ClearRuntime();
            SchoolGuestOfficeService.ClearRuntime();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.ClearRuntime();
            HistoricalSchoolDescentService.ClearRuntime();
            HistoricalSchoolRuntimeIndex.Instance.ClearLivingXiaCities();
            _loaded = false;
        }

        public static void ProcessFrame()
        {
            long started = Stopwatch.GetTimestamp();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            bool idle = !_loaded || World.world == null;
            try
            {
                BootstrapRetryGate.AdvanceFrame();
                if (idle) return;
                PendingRuntimeState.AdvanceAndTryFlush(
                    HistoricalSchoolStore.SaveRuntimeState);
                HistoricalSchoolDescentService.ProcessPendingDescentReconciliations();
                SchoolGuestOfficeService.ProcessPendingFrame();
                HistoricalSchoolActivityQueue.ProcessFrame();
                int month = Math.Max(1, Math.Min(12, Date.getCurrentMonth()));
                int quarterKey = Date.getCurrentYear() * 4 + (month - 1) / 3;
                if (quarterKey == _lastQuarterKey) return;
                _lastQuarterKey = quarterKey;
                HistoricalSchoolTravelService.ProcessQuarter(quarterKey);
            }
            finally
            {
                HistoricalSchoolDiagnostics.RecordSchedulerFrame(
                    Stopwatch.GetTimestamp() - started,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                    idle);
            }
        }

        internal static bool FlushPendingStateForSave()
        {
            return PendingRuntimeState.FlushForSave(HistoricalSchoolStore.SaveRuntimeState);
        }

        public static void OnWorldYear()
        {
            int worldYear = Date.getCurrentYear();
            if (worldYear == _attemptedWorldYear) return;

            var runner = new HistoricalSchoolAnnualStageRunner(LogAnnualStageFailure);
            if (!_loaded)
            {
                if (!BootstrapRetryGate.CanAttempt()) return;
                if (!runner.TryRun(HistoricalSchoolAnnualStageId.Bootstrap, LoadState))
                {
                    BootstrapRetryGate.RecordFailure();
                    return;
                }
                BootstrapRetryGate.RecordSuccess();
            }
            _attemptedWorldYear = worldYear;
            if (worldYear == _lastWorldYear) return;

            int nextEligibleYear = _eligibleYear;
            List<City> cities = null;
            bool cityScanSucceeded = runner.TryRun(HistoricalSchoolAnnualStageId.XiaCityScan,
                () =>
                {
                    cities = LivingXiaCities();
                    nextEligibleYear = HistoricalSchoolRules.AdvanceEligibleYear(_eligibleYear,
                        cities.Count > 0);
                });
            if (cityScanSucceeded)
            {
                _eligibleYear = nextEligibleYear;
                if (cities.Count > 0)
                    runner.TryRun(HistoricalSchoolAnnualStageId.Descent, () =>
                        HistoricalSchoolDescentService.ProcessDue(nextEligibleYear, cities));
            }

            runner.TryRun(HistoricalSchoolAnnualStageId.Guest, () =>
                SchoolGuestOfficeService.ProcessYear(worldYear));
            runner.TryRun(HistoricalSchoolAnnualStageId.LedgerDecay, () =>
            {
                HistoricalSchoolStore.ApplyLedgerDecay(worldYear,
                    World.world?.getCurWorldTime() ?? 0d, out long[] affectedCityIds);
                foreach (long cityId in affectedCityIds)
                    CitySchoolSnapshotService.MarkDirtyById(cityId);
            });

            bool snapshotSucceeded = runner.TryRun(
                HistoricalSchoolAnnualStageId.AnnualSnapshot,
                HistoricalSchoolAnnualMemberSnapshotBuilder.Build,
                out HistoricalSchoolAnnualMemberSnapshot<Actor> annualMembers);
            if (snapshotSucceeded)
            {
                runner.TryRun(HistoricalSchoolAnnualStageId.Action, () =>
                    HistoricalSchoolActionService.ProcessYear(worldYear, annualMembers));
                runner.TryRun(HistoricalSchoolAnnualStageId.Debate, () =>
                    HistoricalSchoolDebateService.ProcessYear(worldYear, annualMembers));
            }

            PendingRuntimeState.Freeze(nextEligibleYear, worldYear,
                World.world?.getCurWorldTime() ?? 0d);
            runner.TryRun(HistoricalSchoolAnnualStageId.RuntimeSave, () =>
            {
                if (!PendingRuntimeState.FlushForSave(
                        HistoricalSchoolStore.SaveRuntimeState))
                    throw new InvalidOperationException(
                        "Historical school runtime state remains pending");
            });
            _lastWorldYear = worldYear;
        }

        private static List<City> LivingXiaCities()
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

        private static void LogAnnualStageFailure(string pStageId, Exception error)
        {
            ModClass.LogWarning("Historical school annual stage failed [" + pStageId +
                                "]: " + error.ToString());
        }
    }
}
