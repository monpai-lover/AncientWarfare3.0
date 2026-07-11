using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class CityOccupationAccelerationService
    {
        private static readonly FieldInfo CaptureTicksField = AccessTools.Field(typeof(City), "_capture_ticks");
        private static readonly Dictionary<string, GoalCache> GoalCacheByCityAndAttacker =
            new Dictionary<string, GoalCache>();

        public static void BeforeUpdateCapture(City pCity, float pElapsed)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return;
            Kingdom capturer = pCity.being_captured_by;
            if (capturer?.data == null || capturer == pCity.kingdom) return;

            bool hasActiveCaptureUnits;
            try { hasActiveCaptureUnits = pCity.isGettingCapturedBy(capturer); }
            catch { hasActiveCaptureUnits = false; }
            if (!hasActiveCaptureUnits) return;

            bool enemyCapture;
            try { enemyCapture = capturer.isEnemy(pCity.kingdom); }
            catch { enemyCapture = false; }
            if (!enemyCapture) return;

            bool hasDefenders = SafeCountWarriors(pCity) > 0;
            bool hasGoal = HasCityControlGoal(pCity, capturer);
            int towers = SafeCountWatchTowers(pCity);
            float extra = CityOccupationAccelerationRules.ExtraCapturePoints(
                enemyCapture,
                hasActiveCaptureUnits,
                hasDefenders,
                hasGoal,
                towers);
            if (extra <= 0f) return;

            AddCaptureTicks(pCity, extra * Mathf.Max(0.25f, pElapsed * 10f));
        }

        private static bool HasCityControlGoal(City pCity, Kingdom pCapturer)
        {
            if (pCity?.data == null || pCapturer?.data == null) return false;
            int year = Date.getCurrentYear();
            string key = pCity.id + ":" + pCapturer.id;
            if (GoalCacheByCityAndAttacker.TryGetValue(key, out GoalCache cache) && cache.year == year)
                return cache.has_goal;

            bool result = false;
            try { result = WarTerritoryService.HasOpenCityControlGoalForAttacker(pCity, pCapturer); }
            catch { }

            if (GoalCacheByCityAndAttacker.Count > 2048)
                GoalCacheByCityAndAttacker.Clear();
            GoalCacheByCityAndAttacker[key] = new GoalCache { year = year, has_goal = result };
            return result;
        }

        private static void AddCaptureTicks(City pCity, float pExtra)
        {
            if (CaptureTicksField == null || pExtra <= 0f) return;
            try
            {
                float current = Convert.ToSingle(CaptureTicksField.GetValue(pCity));
                if (current <= 0f || current >= 99.5f) return;
                CaptureTicksField.SetValue(pCity, Mathf.Min(99.5f, current + pExtra));
            }
            catch { }
        }

        private static int SafeCountWarriors(City pCity)
        {
            try { return pCity.countWarriors(); }
            catch { return 0; }
        }

        private static int SafeCountWatchTowers(City pCity)
        {
            try { return pCity.countBuildingsType("type_watch_tower"); }
            catch { return 0; }
        }

        private struct GoalCache
        {
            public int year;
            public bool has_goal;
        }
    }
}
