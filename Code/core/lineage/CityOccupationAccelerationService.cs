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
        private static readonly Dictionary<long, EngagementState> EngagementByCity =
            new Dictionary<long, EngagementState>();

        public static void ClearRuntime()
        {
            GoalCacheByCityAndAttacker.Clear();
            ActiveMilitaryKingdomsByCity.Clear();
            EngagementByCity.Clear();
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null || EngagementByCity.Count == 0) return;
            var attackers = new HashSet<long>();
            var defenders = new HashSet<long>();
            try
            {
                foreach (Kingdom kingdom in pWar.getAttackers())
                    if (kingdom?.data != null) attackers.Add(kingdom.id);
                foreach (Kingdom kingdom in pWar.getDefenders())
                    if (kingdom?.data != null) defenders.Add(kingdom.id);
            }
            catch { return; }

            var emptyCities = new List<long>();
            foreach (KeyValuePair<long, EngagementState> item in EngagementByCity)
            {
                EngagementState state = item.Value;
                state.AttackerKingdomIds.RemoveWhere(attackerId =>
                    (attackers.Contains(state.OwnerKingdomId) && defenders.Contains(attackerId)) ||
                    (defenders.Contains(state.OwnerKingdomId) && attackers.Contains(attackerId)));
                if (state.AttackerKingdomIds.Count == 0) emptyCities.Add(item.Key);
            }
            for (int i = 0; i < emptyCities.Count; i++)
                EngagementByCity.Remove(emptyCities[i]);
        }

        public static void ClearActiveMilitaryPresence(City pCity)
        {
            if (pCity?.data == null) return;
            ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms);
            ReconcileCompletedPresenceCycle(pCity, kingdoms);
            kingdoms?.Clear();
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
            TryLatchDefenderEngagement(pCity, actor.kingdom, kingdoms);
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
            activeCaptureUnits &= capturerIsDominant &&
                                  HasActiveMilitaryPresence(pCity, capturer);

            bool cityManagerLocked = true;
            try { cityManagerLocked = World.world?.cities == null || World.world.cities.isLocked(); }
            catch { }
            bool ownershipChanged = pCity.kingdom == capturer;
            bool defenderEngagementObserved =
                HasDefenderEngagement(pCity, oldOwner, capturer);
            if (!CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
                    enemyCapturer,
                    activeCaptureUnits,
                    activeDefenders,
                    hostileRivalActive,
                    ownershipChanged,
                    cityManagerLocked,
                    defenderEngagementObserved))
                return false;

            try { pCity.finishCapture(capturer); }
            catch (Exception e)
            {
                ModClass.LogWarning("Immediate city capture failed city=" + pCity.id +
                                    " capturer=" + capturer.id + ": " + e.Message);
                return false;
            }
            ClearCityRuntimeState(pCity.id);
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
                towers,
                MandatePhaseService.OccupationMultiplier);
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

        private static void TryLatchDefenderEngagement(City pCity, Kingdom pObservedKingdom,
            HashSet<long> pActiveKingdoms)
        {
            Kingdom owner = pCity?.kingdom;
            if (pCity?.data == null || owner?.data == null ||
                pObservedKingdom?.data == null || pActiveKingdoms == null ||
                !pActiveKingdoms.Contains(owner.id)) return;

            if (pObservedKingdom != owner)
            {
                TryAddEngagedAttacker(pCity, owner, pObservedKingdom, pActiveKingdoms);
                return;
            }

            foreach (long kingdomId in pActiveKingdoms)
            {
                if (kingdomId == owner.id) continue;
                Kingdom attacker = FindKingdom(kingdomId);
                TryAddEngagedAttacker(pCity, owner, attacker, pActiveKingdoms);
            }
        }

        private static void TryAddEngagedAttacker(City pCity, Kingdom pOwner,
            Kingdom pAttacker, HashSet<long> pActiveKingdoms)
        {
            if (pAttacker?.data == null || pAttacker == pOwner) return;
            bool attackerIsEnemy;
            try { attackerIsEnemy = pAttacker.isEnemy(pOwner); }
            catch { attackerIsEnemy = false; }
            if (!CityOccupationAccelerationRules.ShouldLatchDefenderEngagement(
                    pActiveKingdoms.Contains(pOwner.id),
                    pActiveKingdoms.Contains(pAttacker.id),
                    attackerIsEnemy)) return;

            if (!EngagementByCity.TryGetValue(pCity.id, out EngagementState state) ||
                state.OwnerKingdomId != pOwner.id)
            {
                if (EngagementByCity.Count > 4096) EngagementByCity.Clear();
                state = new EngagementState
                {
                    OwnerKingdomId = pOwner.id,
                    AttackerKingdomIds = new HashSet<long>()
                };
                EngagementByCity[pCity.id] = state;
            }
            state.AttackerKingdomIds.Add(pAttacker.id);
        }

        private static bool HasDefenderEngagement(City pCity, Kingdom pOwner,
            Kingdom pAttacker)
        {
            if (pCity?.data == null || pOwner?.data == null || pAttacker?.data == null ||
                !EngagementByCity.TryGetValue(pCity.id, out EngagementState state))
                return false;

            bool attackerStillEnemy;
            try { attackerStillEnemy = pAttacker.isEnemy(pOwner); }
            catch { attackerStillEnemy = false; }
            return CityOccupationAccelerationRules.ShouldRetainDefenderEngagement(
                state.OwnerKingdomId == pOwner.id,
                state.AttackerKingdomIds.Contains(pAttacker.id),
                attackerStillEnemy,
                HasActiveMilitaryPresence(pCity, pAttacker));
        }

        private static void ReconcileCompletedPresenceCycle(City pCity,
            HashSet<long> pCompletedActiveKingdoms)
        {
            if (pCity?.data == null || !EngagementByCity.TryGetValue(pCity.id,
                    out EngagementState state)) return;
            Kingdom owner = pCity.kingdom;
            if (owner?.data == null || state.OwnerKingdomId != owner.id)
            {
                EngagementByCity.Remove(pCity.id);
                return;
            }

            state.AttackerKingdomIds.RemoveWhere(attackerId =>
            {
                Kingdom attacker = FindKingdom(attackerId);
                bool attackerStillEnemy;
                try { attackerStillEnemy = attacker?.data != null && attacker.isEnemy(owner); }
                catch { attackerStillEnemy = false; }
                return !CityOccupationAccelerationRules.ShouldRetainDefenderEngagement(
                    ownerMatches: state.OwnerKingdomId == owner.id,
                    attackerMatches: attacker?.data != null && attacker.id == attackerId,
                    attackerStillEnemy: attackerStillEnemy,
                    attackerPresentInCompletedCycle:
                        pCompletedActiveKingdoms?.Contains(attackerId) == true);
            });
            if (state.AttackerKingdomIds.Count == 0) EngagementByCity.Remove(pCity.id);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static void ClearCityRuntimeState(long pCityId)
        {
            ActiveMilitaryKingdomsByCity.Remove(pCityId);
            EngagementByCity.Remove(pCityId);
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

        private sealed class EngagementState
        {
            public long OwnerKingdomId;
            public HashSet<long> AttackerKingdomIds;
        }
    }
}
