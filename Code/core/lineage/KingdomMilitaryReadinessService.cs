using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomMilitaryReadinessService
    {
        private sealed class RealmReadiness
        {
            public readonly long KingdomId;
            public readonly KingdomMilitaryReadinessIndex Index =
                new KingdomMilitaryReadinessIndex();
            public int ScanCursor;
            public bool ScanActive;
            public bool ScanQueued;
            public bool RestartRequested;

            public RealmReadiness(long pKingdomId)
            {
                KingdomId = pKingdomId;
            }
        }

        private static readonly Dictionary<long, RealmReadiness> States =
            new Dictionary<long, RealmReadiness>();

        public static bool HasReadyStandingCore(Kingdom pKingdom)
        {
            if (!IsValidKingdom(pKingdom)) return false;
            bool temporaryLeviesActive = TemporaryLevyService.HasActivePool(pKingdom);
            RealmReadiness state = State(pKingdom.id);
            int currentCityCount = pKingdom.cities.Count;
            if (!state.Index.ScanComplete || state.Index.ObservedCityCount != currentCityCount)
                EnsureScan(pKingdom, state,
                    pMembershipChanged: state.Index.ObservedCityCount != currentCityCount);
            return state.Index.IsReady(currentCityCount, temporaryLeviesActive);
        }

        public static void ObserveCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || pCity.isRekt() || !IsValidKingdom(kingdom)) return;
            RealmReadiness state = State(kingdom.id);
            int currentCityCount = kingdom.cities.Count;
            if (!state.ScanActive && (!state.Index.ScanComplete ||
                                      state.Index.ObservedCityCount != currentCityCount))
                BeginScan(kingdom, state);
            else if (state.ScanActive && state.Index.ObservedCityCount != currentCityCount)
                state.RestartRequested = true;
            UpdateCity(state, pCity);
        }

        public static void MarkCityDirty(City pCity)
        {
            if (States.Count == 0) return;
            if (pCity?.data == null) return;
            long cityId = pCity.id;
            long previousKingdomId = pCity.kingdom?.id ?? -1L;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("standing_readiness_city", cityId),
                DeferredWorkClass.Runtime,
                () => RefreshDirtyCity(cityId, previousKingdomId));
        }

        public static void MarkArmyCitiesDirty(Actor pActor, Army pPreviousArmy,
            Army pCurrentArmy)
        {
            City previousCity = ResolveOrdinaryArmyCity(pPreviousArmy);
            City currentCity = ResolveOrdinaryArmyCity(pCurrentArmy);
            MarkCityDirty(previousCity);
            if (currentCity != previousCity) MarkCityDirty(currentCity);
        }

        public static void MarkOrdinaryArmyActorDirty(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isWarrior()) return;
            MarkCityDirty(ResolveOrdinaryArmyCity(pActor.army));
        }

        public static void OnCityKingdomChanged(City pCity, Kingdom pOldKingdom,
            Kingdom pNewKingdom)
        {
            long cityId = pCity?.data?.id ?? -1L;
            if (cityId < 0 || pOldKingdom == pNewKingdom) return;
            if (pOldKingdom?.data != null && States.TryGetValue(
                    pOldKingdom.id, out RealmReadiness oldState))
            {
                RemoveCity(oldState, cityId);
                RequestMembershipRebuild(pOldKingdom, oldState);
            }
            if (IsValidKingdom(pNewKingdom))
            {
                RealmReadiness newState = State(pNewKingdom.id);
                RequestMembershipRebuild(pNewKingdom, newState);
                if (pCity?.kingdom == pNewKingdom && !pCity.isRekt()) UpdateCity(newState, pCity);
            }
        }

        public static void OnCityDestroyed(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null) return;
            Kingdom kingdom = pCity.kingdom;
            RealmReadiness state = State(kingdom.id);
            RemoveCity(state, pCity.id);
            RequestMembershipRebuild(kingdom, state);
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            States.Remove(pKingdom.id);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsValidKingdom(kingdom)) continue;
                RealmReadiness state = State(kingdom.id);
                StartGeneration(state, kingdom.cities.Count);
                for (int i = 0; i < kingdom.cities.Count; i++)
                {
                    City city = kingdom.cities[i];
                    if (city?.data == null || city.isRekt() || city.kingdom != kingdom) continue;
                    UpdateCity(state, city);
                }
                state.ScanCursor = state.Index.ObservedCityCount;
                state.ScanActive = false;
                state.Index.MarkComplete();
            }
        }

        public static void ClearRuntime()
        {
            States.Clear();
        }

        private static void EnsureScan(Kingdom pKingdom, RealmReadiness pState,
            bool pMembershipChanged)
        {
            if (!IsValidKingdom(pKingdom) || pState == null) return;
            if (!pState.ScanActive)
                BeginScan(pKingdom, pState);
            else if (pMembershipChanged)
                pState.RestartRequested = true;
            ScheduleScan(pState);
        }

        private static void RequestMembershipRebuild(Kingdom pKingdom, RealmReadiness pState)
        {
            if (!IsValidKingdom(pKingdom) || pState == null) return;
            pState.Index.MarkIncomplete();
            if (pState.ScanActive)
                pState.RestartRequested = true;
            else
                BeginScan(pKingdom, pState);
            ScheduleScan(pState);
        }

        private static void BeginScan(Kingdom pKingdom, RealmReadiness pState)
        {
            StartGeneration(pState, pKingdom.cities.Count);
            ScheduleScan(pState);
        }

        private static void StartGeneration(RealmReadiness pState, int pCityCount)
        {
            if (pState == null) return;
            pState.Index.StartGeneration(pCityCount);
            pState.ScanCursor = 0;
            pState.ScanActive = true;
            pState.RestartRequested = false;
        }

        private static void ScheduleScan(RealmReadiness pState)
        {
            if (pState == null || pState.ScanQueued) return;
            pState.ScanQueued = true;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "standing_readiness_scan", pState.KingdomId),
                DeferredWorkClass.Runtime,
                () => ProcessScanBatch(pState.KingdomId));
        }

        private static void ProcessScanBatch(long pKingdomId)
        {
            if (!States.TryGetValue(pKingdomId, out RealmReadiness state)) return;
            state.ScanQueued = false;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (!IsValidKingdom(kingdom))
            {
                States.Remove(pKingdomId);
                return;
            }

            int cityCount = kingdom.cities.Count;
            if (state.RestartRequested || cityCount != state.Index.ObservedCityCount)
            {
                BeginScan(kingdom, state);
                return;
            }

            int start = Math.Max(0, Math.Min(state.ScanCursor, cityCount));
            int end = Math.Min(cityCount,
                start + KingdomMilitaryReadinessRules.MaxCitiesPerWorkItem);
            for (int i = start; i < end; i++)
            {
                City city = kingdom.cities[i];
                if (city?.data == null || city.isRekt() || city.kingdom != kingdom) continue;
                UpdateCity(state, city);
            }
            state.ScanCursor = end;
            if (end < cityCount)
            {
                ScheduleScan(state);
                return;
            }
            state.ScanActive = false;
            state.Index.MarkComplete();
        }

        private static void RefreshDirtyCity(long pCityId, long pPreviousKingdomId)
        {
            City city = ResolveCity(pCityId);
            long currentKingdomId = city?.kingdom?.id ?? -1L;
            if (pPreviousKingdomId >= 0 && currentKingdomId != pPreviousKingdomId &&
                States.TryGetValue(pPreviousKingdomId, out RealmReadiness previousState))
                RemoveCity(previousState, pCityId);
            if (city?.data != null && !city.isRekt()) ObserveCity(city);
        }

        private static void UpdateCity(RealmReadiness pState, City pCity)
        {
            if (pState == null || pCity?.data == null || pCity.kingdom?.id != pState.KingdomId) return;
            int effectiveSlots = MandateMilitaryPhaseService.
                EffectiveWarriorSlots(pCity.kingdom,
                    pCity.status.warrior_slots);
            int required = StandingArmyRules.PeacetimeCore(effectiveSlots);
            int filled = StandingArmyService.CountOrdinaryStandingFast(pCity);
            pState.Index.Observe(pCity.id, required > 0, required <= 0 || filled >= required);
        }

        private static void RemoveCity(RealmReadiness pState, long pCityId)
        {
            pState?.Index.Remove(pCityId);
        }

        private static RealmReadiness State(long pKingdomId)
        {
            if (!States.TryGetValue(pKingdomId, out RealmReadiness state))
            {
                state = new RealmReadiness(pKingdomId);
                States[pKingdomId] = state;
            }
            return state;
        }

        private static bool IsValidKingdom(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() && !pKingdom.isNeutral();
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveOrdinaryArmyCity(Army pArmy)
        {
            try
            {
                if (pArmy?.data == null || !pArmy.hasCity() || AWArmyService.IsSpecialArmy(pArmy))
                    return null;
                City city = pArmy.getCity();
                return city?.data != null && city.getArmy() == pArmy ? city : null;
            }
            catch { return null; }
        }
    }
}
