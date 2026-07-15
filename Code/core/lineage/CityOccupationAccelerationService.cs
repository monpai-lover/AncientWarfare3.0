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
        private static readonly FieldInfo CapturingUnitsField = AccessTools.Field(typeof(City), "_capturing_units");
        private static readonly Dictionary<string, GoalCache> GoalCacheByCityAndAttacker =
            new Dictionary<string, GoalCache>();
        private static readonly Dictionary<long, HashSet<long>> ActiveMilitaryKingdomsByCity =
            new Dictionary<long, HashSet<long>>();

        public static void ClearActiveMilitaryPresence(City pCity)
        {
            if (pCity?.data == null) return;
            if (ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms))
                kingdoms.Clear();
        }

        public static void RecordActiveMilitaryPresence(City pCity, BaseSimObject pObject)
        {
            Actor actor = pObject as Actor;
            bool actorAlive = actor?.data != null && actor.isAlive() && !actor.isRekt();
            bool actorIsWarrior = false;
            try { actorIsWarrior = actorAlive && actor.isWarrior(); }
            catch { }
            bool actorHasKingdom = actor?.kingdom?.data != null;
            if (pCity?.data == null ||
                !CityOccupationAccelerationRules.ShouldRecordActiveMilitaryPresence(
                    actor != null, actorAlive, actorIsWarrior, actorHasKingdom))
                return;

            if (!ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms))
            {
                if (ActiveMilitaryKingdomsByCity.Count > 4096)
                    ActiveMilitaryKingdomsByCity.Clear();
                kingdoms = new HashSet<long>();
                ActiveMilitaryKingdomsByCity[pCity.id] = kingdoms;
            }
            kingdoms.Add(actor.kingdom.id);
        }

        public static bool TryCompleteAfterDefenderDefeat(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return false;
            Kingdom oldOwner = pCity.kingdom;
            Kingdom capturer = ResolveDominantCapturer(pCity);
            if (capturer?.data == null || capturer == oldOwner || capturer.isRekt()) return false;

            bool enemyCapturer;
            bool activeCaptureUnits;
            try
            {
                enemyCapturer = capturer.isEnemy(oldOwner);
                activeCaptureUnits = pCity.isGettingCapturedBy(capturer);
            }
            catch { return false; }

            bool activeDefenders = HasActiveDefenders(pCity);
            DescribeCaptureFor(pCity, capturer,
                out bool capturerIsDominant, out bool hostileRivalActive);
            activeCaptureUnits &= capturerIsDominant;

            bool cityManagerLocked = true;
            try { cityManagerLocked = World.world?.cities == null || World.world.cities.isLocked(); }
            catch { }
            bool ownershipChanged = pCity.kingdom == capturer;
            if (!CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
                    enemyCapturer,
                    activeCaptureUnits,
                    activeDefenders,
                    hostileRivalActive,
                    ownershipChanged,
                    cityManagerLocked))
                return false;

            try { pCity.finishCapture(capturer); }
            catch (Exception e)
            {
                ModClass.LogWarning("Immediate city capture failed city=" + pCity.id +
                                    " capturer=" + capturer.id + ": " + e.Message);
                return false;
            }
            return pCity.kingdom != oldOwner;
        }

        public static void BeforeUpdateCapture(City pCity, float pElapsed)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return;
            Kingdom capturer = ResolveDominantCapturer(pCity);
            if (capturer?.data == null || capturer == pCity.kingdom) return;

            bool hasActiveCaptureUnits;
            try { hasActiveCaptureUnits = pCity.isGettingCapturedBy(capturer); }
            catch { hasActiveCaptureUnits = false; }
            if (!hasActiveCaptureUnits) return;

            Kingdom currentCaptureOwner = pCity.being_captured_by;
            bool dominantEnemy;
            try { dominantEnemy = capturer.isEnemy(pCity.kingdom); }
            catch { dominantEnemy = false; }
            if (!dominantEnemy) return;

            bool hasCaptureOwner = currentCaptureOwner?.data != null;
            bool captureOwnerAlive = false;
            bool captureOwnerStillEnemyOfCity = false;
            if (hasCaptureOwner)
            {
                try { captureOwnerAlive = currentCaptureOwner.isAlive(); }
                catch { }
                if (captureOwnerAlive)
                {
                    try { captureOwnerStillEnemyOfCity = currentCaptureOwner.isEnemy(pCity.kingdom); }
                    catch { }
                }
            }

            if (CityOccupationAccelerationRules.ShouldAdoptDominantCapturer(
                    dominantEnemy, hasCaptureOwner, captureOwnerAlive, captureOwnerStillEnemyOfCity))
            {
                pCity.being_captured_by = capturer;
                currentCaptureOwner = capturer;
            }

            bool canAdvanceCurrentCapture = currentCaptureOwner == null || currentCaptureOwner == capturer;
            if (!canAdvanceCurrentCapture && currentCaptureOwner?.data != null)
            {
                try { canAdvanceCurrentCapture = !capturer.isEnemy(currentCaptureOwner); }
                catch { canAdvanceCurrentCapture = false; }
            }
            Kingdom captureOwner = currentCaptureOwner ?? capturer;

            bool enemyCapture;
            try { enemyCapture = captureOwner.isEnemy(pCity.kingdom); }
            catch { enemyCapture = false; }
            if (!enemyCapture) return;

            bool hasDefenders = HasActiveDefenders(pCity);
            bool hasGoal = HasCityControlGoal(pCity, captureOwner);
            int towers = SafeCountWatchTowers(pCity);
            float extra = CityOccupationAccelerationRules.ExtraCapturePoints(
                enemyCapture,
                hasActiveCaptureUnits,
                canAdvanceCurrentCapture,
                hasDefenders,
                hasGoal,
                towers);
            if (extra <= 0f) return;

            AddCaptureTicks(pCity, extra * Mathf.Max(0.25f, pElapsed * 10f));
        }

        internal static bool HasActiveDefenders(City pCity)
        {
            return pCity?.kingdom?.data != null &&
                   HasActiveMilitaryPresence(pCity, pCity.kingdom);
        }

        private static bool HasActiveMilitaryPresence(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null &&
                   pKingdom?.data != null &&
                   ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms) &&
                   kingdoms.Contains(pKingdom.id);
        }

        internal static void DescribeCaptureFor(City pCity, Kingdom pAttacker,
            out bool pAttackerIsDominant, out bool pHostileRivalActive)
        {
            pAttackerIsDominant = ResolveDominantCapturer(pCity) == pAttacker;
            pHostileRivalActive = false;
            if (pCity?.data == null || pAttacker?.data == null) return;

            try
            {
                var capturing = CapturingUnitsField?.GetValue(pCity) as IDictionary<Kingdom, int>;
                if (capturing == null) return;
                bool ownerHasActiveDefenders = HasActiveDefenders(pCity);
                foreach (KeyValuePair<Kingdom, int> item in capturing)
                {
                    Kingdom rival = item.Key;
                    if (rival?.data == null || rival == pAttacker || item.Value <= 0) continue;
                    if (!CityOccupationAccelerationRules.ShouldCountMilitaryCapturePresence(
                            rival == pCity.kingdom, ownerHasActiveDefenders))
                        continue;
                    if (!rival.isEnemy(pAttacker)) continue;
                    pHostileRivalActive = true;
                    return;
                }
            }
            catch { }
        }

        private static Kingdom ResolveDominantCapturer(City pCity)
        {
            try
            {
                var capturing = CapturingUnitsField?.GetValue(pCity) as IDictionary<Kingdom, int>;
                Kingdom best = null;
                int bestCount = 0;
                bool ownerHasActiveDefenders = HasActiveDefenders(pCity);
                if (capturing != null)
                    foreach (KeyValuePair<Kingdom, int> item in capturing)
                    {
                        if (!CityOccupationAccelerationRules.ShouldCountMilitaryCapturePresence(
                                item.Key == pCity.kingdom, ownerHasActiveDefenders))
                            continue;
                        if (item.Key?.data == null || item.Value <= bestCount) continue;
                        best = item.Key;
                        bestCount = item.Value;
                    }
                if (best?.data != null) return best;
            }
            catch { }
            return pCity?.being_captured_by;
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
