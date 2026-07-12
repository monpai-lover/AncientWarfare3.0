using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolRuntime
    {
        private static int _eligibleYear;
        private static int _lastWorldYear = -1;
        private static int _lastQuarterKey = -1;
        private static bool _loaded;

        public static int EligibleYear => _eligibleYear;

        public static void LoadState()
        {
            HistoricalSchoolStore.LoadRuntimeState(out _eligibleYear, out _lastWorldYear);
            HistoricalSchoolDescentService.LoadState();
            HistoricalAffiliationService.LoadState();
            HistoricalSchoolTravelService.ClearRuntime();
            _lastQuarterKey = -1;
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            _eligibleYear = 0;
            _lastWorldYear = -1;
            _lastQuarterKey = -1;
            HistoricalAffiliationService.ClearRuntime();
            HistoricalSchoolTravelService.ClearRuntime();
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
            if (worldYear == _lastWorldYear) return;
            _lastWorldYear = worldYear;

            List<City> cities = LivingXiaCities();
            _eligibleYear = HistoricalSchoolRules.AdvanceEligibleYear(_eligibleYear,
                cities.Count > 0);
            HistoricalSchoolStore.SaveRuntimeState(_eligibleYear, _lastWorldYear,
                World.world?.getCurWorldTime() ?? 0d);
            if (cities.Count > 0)
                HistoricalSchoolDescentService.ProcessDue(_eligibleYear, cities);
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
