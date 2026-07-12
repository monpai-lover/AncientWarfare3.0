using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRuntime
    {
        private static int _eligibleYear;
        private static int _lastWorldYear = -1;
        private static int _attemptedWorldYear = -1;
        private static int _lastQuarterKey = -1;
        private static bool _loaded;

        public static int EligibleYear => _eligibleYear;

        public static void LoadState()
        {
            HistoricalSchoolStore.LoadRuntimeState(out _eligibleYear, out _lastWorldYear);
            HistoricalSchoolDescentService.LoadState();
            HistoricalAffiliationService.LoadState();
            HistoricalAffiliationService.EnsureMembershipAffiliations();
            SchoolLineageService.LoadState();
            HistoricalSchoolActionService.ClearRuntime();
            SchoolGuestOfficeService.LoadState();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.LoadState();
            _attemptedWorldYear = -1;
            _lastQuarterKey = -1;
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            _eligibleYear = 0;
            _lastWorldYear = -1;
            _attemptedWorldYear = -1;
            _lastQuarterKey = -1;
            HistoricalAffiliationService.ClearRuntime();
            SchoolLineageService.ClearRuntime();
            HistoricalSchoolActionService.ClearRuntime();
            SchoolGuestOfficeService.ClearRuntime();
            SchoolLandmarkService.Clear();
            HistoricalSchoolTravelService.ClearRuntime();
            HistoricalSchoolDebateService.ClearRuntime();
            _loaded = false;
        }

        public static void ProcessFrame()
        {
            if (!_loaded || World.world == null) return;
            int month = Math.Max(1, Math.Min(12, Date.getCurrentMonth()));
            int quarterKey = Date.getCurrentYear() * 4 + (month - 1) / 3;
            if (quarterKey == _lastQuarterKey) return;
            _lastQuarterKey = quarterKey;
            HistoricalSchoolTravelService.ProcessQuarter(quarterKey);
        }

        public static void OnWorldYear()
        {
            if (!_loaded) LoadState();
            int worldYear = Date.getCurrentYear();
            if (worldYear == _lastWorldYear || worldYear == _attemptedWorldYear) return;
            _attemptedWorldYear = worldYear;

            try
            {
                List<City> cities = LivingXiaCities();
                int nextEligibleYear = HistoricalSchoolRules.AdvanceEligibleYear(_eligibleYear,
                    cities.Count > 0);
                if (cities.Count > 0)
                    HistoricalSchoolDescentService.ProcessDue(nextEligibleYear, cities);
                SchoolGuestOfficeService.ProcessYear(worldYear);
                int decayedLedgers = HistoricalSchoolStore.ApplyLedgerDecay(worldYear,
                    World.world?.getCurWorldTime() ?? 0d);
                if (decayedLedgers > 0)
                {
                    CitySchoolSnapshotService.Clear();
                    SchoolMapModeService.DirtyMapIfActive();
                }
                HistoricalSchoolActionService.ProcessYear(worldYear);
                HistoricalSchoolDebateService.ProcessYear(worldYear);
                HistoricalSchoolStore.SaveRuntimeState(nextEligibleYear, worldYear,
                    World.world?.getCurWorldTime() ?? 0d);
                _eligibleYear = nextEligibleYear;
                _lastWorldYear = worldYear;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school annual tick failed: " +
                                    error.ToString());
            }
        }

        private static List<City> LivingXiaCities()
        {
            var result = new List<City>();
            try
            {
                if (World.world?.kingdoms == null) return result;
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom?.data == null || kingdom.isRekt() || kingdom.isNeutral() ||
                        !LineageService.IsXiaKingdom(kingdom)) continue;
                    foreach (City city in kingdom.getCities())
                        if (city?.data != null && !city.isRekt()) result.Add(city);
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school Xia city scan failed: " + error.Message);
            }
            return result;
        }
    }
}
