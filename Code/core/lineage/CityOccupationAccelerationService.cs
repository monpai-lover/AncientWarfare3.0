using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace AncientWarfare3.core.lineage
{
    internal static class CityOccupationAccelerationService
    {
        private static readonly FieldInfo CaptureTicksField = AccessTools.Field(typeof(City), "_capture_ticks");
        private static readonly FieldInfo CapturingUnitsField = AccessTools.Field(typeof(City), "_capturing_units");
        private static readonly MethodInfo ClearCaptureMethod =
            AccessTools.Method(typeof(City), "clearCapture");
        private static readonly Dictionary<long, HashSet<long>> ActiveMilitaryKingdomsByCity =
            new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, PendingSettlement> PendingSettlementByCity =
            new Dictionary<long, PendingSettlement>();

        public static void ClearRuntime()
        {
            ActiveMilitaryKingdomsByCity.Clear();
            PendingSettlementByCity.Clear();
        }

        internal static bool TrySetCaptureProgress(City pCity,
            float pProgress)
        {
            if (pCity == null || CaptureTicksField == null) return false;
            try
            {
                CaptureTicksField.SetValue(pCity,
                    Math.Max(0f, Math.Min(100f, pProgress)));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void ClearActiveMilitaryPresence(City pCity)
        {
            if (pCity?.data == null) return;
            ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms);
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
        }

        public static bool TryQueueNonTerritorialSettlementAtCaptureLimit(
            City pCity, Kingdom pCapturer)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null ||
                pCapturer?.data == null || pCapturer == pCity.kingdom) return false;
            if (PendingSettlementByCity.ContainsKey(pCity.id)) return true;

            bool enemy;
            try { enemy = pCapturer.isEnemy(pCity.kingdom); }
            catch { enemy = false; }
            bool nonTerritorialSettlement = enemy && WarTerritoryService.
                HasOpenNonTerritorialSettlementGoal(pCity, pCapturer);
            if (!nonTerritorialSettlement) return false;

            bool cityManagerLocked = true;
            try
            {
                cityManagerLocked = World.world?.cities == null ||
                                    World.world.cities.isLocked();
            }
            catch { }
            if (CityOccupationAccelerationRules.
                    ShouldAttemptControlledSettlementImmediately(
                        nonTerritorialSettlement, cityManagerLocked) &&
                WarTerritoryService.TryResolveControlledSettlementGoal(
                    pCity, pCapturer))
            {
                ClearCaptureProgress(pCity);
                return true;
            }
            QueueSettlement(pCity, pCity.kingdom, pCapturer);
            return true;
        }

        private static void QueueSettlement(City pCity, Kingdom pOldOwner,
            Kingdom pCapturer)
        {
            if (pCity?.data == null || pOldOwner?.data == null ||
                pCapturer?.data == null) return;
            if (!PendingSettlementByCity.TryGetValue(pCity.id,
                    out PendingSettlement pending))
            {
                pending = new PendingSettlement
                {
                    CityId = pCity.id,
                    OldOwnerId = pOldOwner.id,
                    CapturerId = pCapturer.id
                };
                PendingSettlementByCity[pCity.id] = pending;
            }
            pending.Authorized = true;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "occupation_complete", pCity.id),
                DeferredWorkClass.Runtime,
                () => ProcessPendingSettlement(pCity.id));
        }

        private static void ProcessPendingSettlement(long pCityId)
        {
            if (!PendingSettlementByCity.TryGetValue(pCityId,
                    out PendingSettlement pending)) return;
            City city = FindCity(pCityId);
            Kingdom oldOwner = FindKingdom(pending.OldOwnerId);
            Kingdom capturer = FindKingdom(pending.CapturerId);
            if (city?.data == null || city.isRekt() || oldOwner?.data == null ||
                capturer?.data == null || city.kingdom != oldOwner)
            {
                ClearCityRuntimeState(pCityId);
                return;
            }

            bool enemyCapturer;
            try
            {
                enemyCapturer = capturer.isEnemy(oldOwner);
            }
            catch
            {
                PendingSettlementByCity.Remove(pCityId);
                return;
            }
            bool goalOpen = WarTerritoryService.
                HasOpenNonTerritorialSettlementGoal(city, capturer);
            if (!goalOpen)
            {
                ClearCaptureProgress(city);
                return;
            }
            if (!CityOccupationAccelerationRules.
                    ShouldHonorQueuedCompletion(pending.Authorized,
                        city.kingdom == oldOwner, enemyCapturer))
            {
                PendingSettlementByCity.Remove(pCityId);
                return;
            }
            bool settled = WarTerritoryService.
                TryResolveControlledSettlementGoal(city, capturer);
            if (settled)
            {
                ClearCaptureProgress(city);
                return;
            }
            if (CityOccupationAccelerationRules.
                ShouldRetryQueuedSettlement(settled, goalOpen))
            {
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    DeferredRuntimeWorkRules.CoalescingKey(
                        "occupation_complete", pCityId),
                    DeferredWorkClass.Runtime,
                    () => ProcessPendingSettlement(pCityId));
                return;
            }
            PendingSettlementByCity.Remove(pCityId);
        }

        private static void ClearCaptureProgress(City pCity)
        {
            if (pCity?.data == null) return;
            try { ClearCaptureMethod?.Invoke(pCity, null); }
            catch { pCity.being_captured_by = null; }
            ClearCityRuntimeState(pCity.id);
        }

        private static float ReadCaptureProgress(City pCity)
        {
            if (pCity == null || CaptureTicksField == null) return 0f;
            try { return Convert.ToSingle(CaptureTicksField.GetValue(pCity)); }
            catch { return 0f; }
        }

        public static bool HasReachedNaturalCaptureLimit(City pCity)
        {
            return CityOccupationAccelerationRules.HasReachedNaturalCaptureLimit(
                ReadCaptureProgress(pCity));
        }

        internal static bool HasActiveDefenders(City pCity)
        {
            if (pCity?.kingdom?.data == null) return false;
            return CityOccupationAccelerationRules.HasActiveDefenderSignal(
                HasActiveMilitaryPresence(pCity, pCity.kingdom),
                WartimeGarrisonService.HasIndexedDefender(pCity,
                    pCity.kingdom));
        }

        private static bool HasActiveMilitaryPresence(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null &&
                   pKingdom?.data != null &&
                   ActiveMilitaryKingdomsByCity.TryGetValue(pCity.id, out HashSet<long> kingdoms) &&
                   kingdoms.Contains(pKingdom.id);
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static void ClearCityRuntimeState(long pCityId)
        {
            ActiveMilitaryKingdomsByCity.Remove(pCityId);
            PendingSettlementByCity.Remove(pCityId);
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

        private sealed class PendingSettlement
        {
            public long CityId;
            public long OldOwnerId;
            public long CapturerId;
            public bool Authorized;
        }
    }
}
