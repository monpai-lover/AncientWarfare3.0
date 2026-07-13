using System;
using System.Collections.Generic;
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

        public static int EligibleYear => _eligibleYear;

        public static void LoadState()
        {
            HistoricalSchoolStore.LoadRuntimeState(out _eligibleYear, out _lastWorldYear);
            HistoricalAffiliationService.LoadState();
            HistoricalSchoolDescentService.LoadState();
            HistoricalAffiliationService.EnsureMembershipAffiliations();
            SchoolLineageService.LoadState();
            HistoricalSchoolActionService.ClearRuntime();
            SchoolGuestOfficeService.LoadState();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.LoadState();
            _lastQuarterKey = -1;
            PendingRuntimeState.Clear();
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            _eligibleYear = 0;
            _lastWorldYear = -1;
            _attemptedWorldYear = -1;
            _lastQuarterKey = -1;
            PendingRuntimeState.Clear();
            HistoricalAffiliationService.ClearRuntime();
            SchoolLineageService.ClearRuntime();
            HistoricalSchoolActionService.ClearRuntime();
            SchoolGuestOfficeService.ClearRuntime();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.ClearRuntime();
            HistoricalSchoolDescentService.ClearRuntime();
            _loaded = false;
        }

        public static void ProcessFrame()
        {
            if (!_loaded || World.world == null) return;
            PendingRuntimeState.AdvanceAndTryFlush(HistoricalSchoolStore.SaveRuntimeState);
            HistoricalSchoolDescentService.ProcessPendingDescentReconciliations();
            int month = Math.Max(1, Math.Min(12, Date.getCurrentMonth()));
            int quarterKey = Date.getCurrentYear() * 4 + (month - 1) / 3;
            if (quarterKey == _lastQuarterKey) return;
            _lastQuarterKey = quarterKey;
            HistoricalSchoolTravelService.ProcessQuarter(quarterKey);
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
            if (!_loaded && !runner.TryRun(HistoricalSchoolAnnualStageId.Bootstrap,
                    LoadState)) return;
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
            if (World.world?.kingdoms == null) return result;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                    !LineageService.IsXiaKingdom(kingdom)) continue;
                foreach (City city in kingdom.getCities())
                    if (city?.data != null && !city.isRekt()) result.Add(city);
            }
            return result;
        }

        private static void LogAnnualStageFailure(string pStageId, Exception error)
        {
            ModClass.LogWarning("Historical school annual stage failed [" + pStageId +
                                "]: " + error.ToString());
        }
    }
}
