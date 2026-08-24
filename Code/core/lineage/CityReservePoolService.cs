using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    // Compatibility estimator for UI and exhaustion checks. Wartime quota,
    // replacement, and synthetic membership belong exclusively to
    // SyntheticMobilizationLedgerService.
    internal static class CityReservePoolService
    {
        private sealed class CityEstimate
        {
            internal int AuthenticPopulation;
            internal int ActiveCitySourcedMilitary;
            internal int Available;
            internal long Epoch;
            internal long ReconciledWorldDay = -1L;
            internal bool Ready;
        }

        private sealed class KingdomEstimate
        {
            internal readonly Dictionary<long, CityEstimate> Cities =
                new Dictionary<long, CityEstimate>();
            internal int CityCursor;
            internal long Epoch;
            internal long PublishedTotalAvailable;
            internal long BuildingTotalAvailable;
            internal int CompletedCityCount;
            internal int ExpectedCityCount;
            internal bool HasPublishedTotal;
            internal bool RebuildActive;
            internal bool RebuildRequested;
        }

        private static readonly Dictionary<long, KingdomEstimate> States =
            new Dictionary<long, KingdomEstimate>();
        private static int _kingdomCursor;

        internal static void BeginWorldLoadRestore()
        {
        }

        internal static void EndWorldLoadRestore()
        {
        }

        internal static void ProcessAuthorityCycle()
        {
            List<Kingdom> kingdoms = World.world?.kingdoms?.list;
            int kingdomCount = kingdoms?.Count ?? 0;
            if (kingdomCount <= 0) return;
            if (_kingdomCursor < 0 || _kingdomCursor >= kingdomCount)
                _kingdomCursor = 0;
            Kingdom kingdom = kingdoms[_kingdomCursor++];
            if (_kingdomCursor >= kingdomCount) _kingdomCursor = 0;
            if (!IsLivingKingdom(kingdom)) return;

            KingdomEstimate state = State(kingdom);
            EnsureGeneration(kingdom, state);
            if (!state.RebuildActive || kingdom.cities == null ||
                kingdom.cities.Count == 0) return;
            if (state.CityCursor < 0 ||
                state.CityCursor >= kingdom.cities.Count)
                state.CityCursor = 0;
            ReconcileEstimate(kingdom.cities[state.CityCursor++], state);
        }

        internal static void OnWarStarted(War war)
        {
            if (!ZhuluWarService.ShouldEnrollInAw3Systems(war)) return;
            foreach (Kingdom kingdom in war.getAttackers())
                Invalidate(kingdom, State(kingdom));
            foreach (Kingdom kingdom in war.getDefenders())
                Invalidate(kingdom, State(kingdom));
        }

        internal static void OnKingdomJoinedWar(War war, Kingdom kingdom)
        {
            if (war?.data == null || war.hasEnded() ||
                !IsLivingKingdom(kingdom)) return;
            Invalidate(kingdom, State(kingdom));
        }

        internal static void OnWarEnded(War war)
        {
            if (war?.data == null) return;
            foreach (Kingdom kingdom in war.getAttackers())
                if (IsLivingKingdom(kingdom))
                    Invalidate(kingdom, State(kingdom));
            foreach (Kingdom kingdom in war.getDefenders())
                if (IsLivingKingdom(kingdom))
                    Invalidate(kingdom, State(kingdom));
        }

        internal static void OnKingdomLeftWar(War war, Kingdom kingdom)
        {
            if (IsLivingKingdom(kingdom))
                Invalidate(kingdom, State(kingdom));
        }

        internal static void OnActorBecameAdult(Actor actor)
        {
        }

        internal static void OnActorReturnedToCivilian(Actor actor)
        {
        }

        internal static void OnActorInvalidated(Actor actor)
        {
        }

        internal static void OnActorCityChanged(Actor actor,
            City previousCity)
        {
        }

        internal static void OnActorKingdomChanged(Actor actor,
            Kingdom previousKingdom)
        {
        }

        internal static void OnActorEnlisted(Actor actor)
        {
        }

        internal static void OnActorProfessionChanged(Actor actor)
        {
        }

        internal static void OnConscriptionLawChanged(Kingdom kingdom,
            CourtConscriptionLaw previousLaw, CourtConscriptionLaw nextLaw)
        {
            if (!IsLivingKingdom(kingdom) || previousLaw == nextLaw ||
                kingdom.cities == null) return;
            Invalidate(kingdom, State(kingdom));
        }

        internal static void OnCityKingdomChanged(City city,
            Kingdom previousKingdom, Kingdom currentKingdom)
        {
            if (city?.data == null || previousKingdom == currentKingdom)
                return;
            if (previousKingdom?.data != null &&
                States.TryGetValue(previousKingdom.id,
                    out KingdomEstimate previousState))
            {
                if (previousState.Cities.TryGetValue(city.id,
                        out CityEstimate previousEstimate))
                    RemoveBuildingContribution(previousState,
                        previousEstimate);
                previousState.Cities.Remove(city.id);
                Invalidate(previousKingdom, previousState);
                if (previousState.Cities.Count == 0)
                    States.Remove(previousKingdom.id);
            }
            if (!IsLivingKingdom(currentKingdom) ||
                city.kingdom != currentKingdom) return;
            KingdomEstimate currentState = State(currentKingdom);
            Invalidate(currentKingdom, currentState);
        }

        internal static void RefreshCapturedCity(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsControlledCity(city, kingdom)) return;
            KingdomEstimate state = State(kingdom);
            EnsureGeneration(kingdom, state);
            ReconcileEstimate(city, state);
        }

        internal static int CountAvailable(Kingdom kingdom)
        {
            TryCountAvailable(kingdom, out int available);
            return available;
        }

        internal static bool TryCountAvailable(Kingdom kingdom,
            out int available)
        {
            available = 0;
            if (!IsLivingKingdom(kingdom)) return true;
            KingdomEstimate state = State(kingdom);
            EnsureGeneration(kingdom, state);
            if (CurrentCityCount(kingdom) == 0) return true;
            if (state.HasPublishedTotal)
            {
                available = (int)Math.Min(int.MaxValue,
                    Math.Max(0L, state.PublishedTotalAvailable));
                return true;
            }
            return false;
        }

        internal static int CountAvailable(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsControlledCity(city, kingdom)) return 0;
            // During formal war the synthetic ledger is authoritative. The
            // compatibility cache is intentionally rebuilt in bounded slices
            // and returns zero until a complete generation is published;
            // exposing that transient value makes synthetic reserves appear
            // exhausted immediately after a levy is created.
            if (AWPerformanceSettings.EnableSyntheticMobilization &&
                ResolveMobilizationPhase(kingdom) == ArmyMobilizationPhase.War)
                return CountWartimeReplacement(city, kingdom);
            KingdomEstimate state = State(kingdom);
            EnsureGeneration(kingdom, state);
            ReconcileEstimateIfNeeded(city, state);
            CityEstimate estimate = Estimate(state, city.id);
            return estimate.Ready ? Math.Max(0, estimate.Available) : 0;
        }

        internal static bool IsFrozen(Kingdom kingdom)
        {
            return CountFormalWars(kingdom) > 0;
        }

        internal static int TryConsumeBatch(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeForMobilization(kingdom, preferredCity,
                requested, targetArmy, false, destination,
                out confirmedExhausted);
        }

        internal static int TryConsumeFromSourceCity(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeForMobilization(kingdom, preferredCity,
                requested, targetArmy, false, destination,
                out confirmedExhausted);
        }

        internal static int TryConsumePreparationBatch(Kingdom kingdom,
            City preferredCity, int approvedShortage, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeForMobilization(kingdom, preferredCity,
                approvedShortage, targetArmy, false, destination,
                out confirmedExhausted);
        }

        internal static int TryConsumeForMobilization(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            bool allowArmyCreation, List<Actor> destination,
            out bool confirmedExhausted)
        {
            if (!AWPerformanceSettings.EnableSyntheticMobilization)
            {
                // This method is only an AW3 synthetic-reserve adapter. A
                // disabled adapter must not claim that vanilla residents are
                // exhausted; native recruitment remains authoritative.
                confirmedExhausted = false;
                return 0;
            }
            // AW3 never leases real residents. Vanilla owns ordinary
            // recruitment and the synthetic ledger owns wartime spawns.
            confirmedExhausted = true;
            return 0;
        }

        internal static ArmyMobilizationPhase ResolveMobilizationPhase(
            Kingdom kingdom)
        {
            return ArmyMobilizationRules.Resolve(IsLivingKingdom(kingdom),
                WarNoticeService.HasActiveNotice(kingdom),
                CountFormalWars(kingdom));
        }

        internal static int RestoreRejectedCandidates(Kingdom kingdom,
            City sourceCity, Army targetArmy,
            IReadOnlyList<Actor> candidates)
        {
            return CountAvailable(sourceCity);
        }

        internal static bool PrepareWarEntry(Kingdom first,
            Kingdom second = null)
        {
            return true;
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
        }

        internal static void FinalizeRuntimeRestore(bool snapshotRestored)
        {
            ClearRuntime();
        }

        internal static void ClearRuntime()
        {
            States.Clear();
            _kingdomCursor = 0;
        }

        internal static void OnAuthenticMobilized(City city, int count)
        {
            if (count <= 0 || city?.kingdom?.data == null) return;
            RequestRebuild(city.kingdom, State(city.kingdom));
        }

        internal static void OnSyntheticMobilized(City city, int count)
        {
            if (count <= 0 || city?.kingdom?.data == null) return;
            RequestRebuild(city.kingdom, State(city.kingdom));
        }

        internal static void OnSyntheticRemoved(City city, int count)
        {
            if (count <= 0 || city?.kingdom?.data == null) return;
            RequestRebuild(city.kingdom, State(city.kingdom));
        }

        internal static void OnSyntheticLedgerChanged(long cityId,
            long kingdomId)
        {
            if (cityId < 0L || kingdomId < 0L ||
                !States.TryGetValue(kingdomId,
                    out KingdomEstimate state)) return;
            Kingdom kingdom = ResolveKingdom(kingdomId);
            if (IsLivingKingdom(kingdom)) RequestRebuild(kingdom, state);
        }

        private static int CountWartimeReplacement(City city,
            Kingdom kingdom)
        {
            long total = 0L;
            try
            {
                foreach (War war in kingdom.getWars())
                {
                    if (!ZhuluWarService.ShouldEnrollInAw3Systems(war) ||
                        war.hasEnded()) continue;
                    total += SyntheticMobilizationLedgerService.
                        AvailableReplacement(war.data.id, city.id);
                    if (total >= int.MaxValue) return int.MaxValue;
                }
            }
            catch { }
            return (int)Math.Max(0L, total);
        }

        private static void ReconcileEstimateIfNeeded(City city,
            KingdomEstimate state)
        {
            CityEstimate estimate = Estimate(state, city.id);
            if (!estimate.Ready || estimate.Epoch != state.Epoch ||
                estimate.ReconciledWorldDay != CurrentWorldDay())
                ReconcileEstimate(city, state);
        }

        private static void ReconcileEstimate(City city,
            KingdomEstimate state)
        {
            if (city?.data == null || state == null || city.isRekt() ||
                !IsLivingKingdom(city.kingdom))
            {
                CommitEstimate(state, Estimate(state, city?.id ?? -1L), 0);
                return;
            }
            CityEstimate estimate = Estimate(state, city.id);
            estimate.AuthenticPopulation = Math.Max(0,
                SafePopulation(city) -
                SyntheticMobilizationLedgerService.LiveSyntheticForCity(
                    city.id));
            estimate.ActiveCitySourcedMilitary = CountCityArmyMembers(city);
            int available = ResolveMobilizationPhase(city.kingdom) ==
                            ArmyMobilizationPhase.War
                ? CountWartimeReplacement(city, city.kingdom)
                : CityManpowerRules.NoticeHeadroom(
                    estimate.AuthenticPopulation,
                    estimate.ActiveCitySourcedMilitary);
            CommitEstimate(state, estimate, available);
        }

        private static void CommitEstimate(KingdomEstimate state,
            CityEstimate estimate, int available)
        {
            if (state == null || estimate == null) return;
            RemoveBuildingContribution(state, estimate);
            estimate.Available = Math.Max(0, available);
            estimate.Epoch = state.Epoch;
            estimate.ReconciledWorldDay = CurrentWorldDay();
            estimate.Ready = true;
            state.BuildingTotalAvailable = Math.Min(int.MaxValue,
                Math.Max(0L, state.BuildingTotalAvailable) +
                estimate.Available);
            state.CompletedCityCount++;
            if (state.CompletedCityCount < state.ExpectedCityCount) return;
            state.PublishedTotalAvailable = state.BuildingTotalAvailable;
            state.HasPublishedTotal = true;
            state.RebuildActive = false;
            state.RebuildRequested = CityReservePoolRules.
                ShouldQueueFollowUpCacheGeneration(state.ExpectedCityCount);
        }

        private static void MarkDirty(City city, KingdomEstimate state)
        {
            if (city?.data == null || state == null) return;
            MarkDirty(state, Estimate(state, city.id));
        }

        private static void MarkDirty(KingdomEstimate state,
            CityEstimate estimate)
        {
            if (state == null || estimate == null) return;
            RemoveBuildingContribution(state, estimate);
            estimate.Ready = false;
            estimate.Available = 0;
        }

        private static void RemoveBuildingContribution(KingdomEstimate state,
            CityEstimate estimate)
        {
            if (state == null || estimate == null || !estimate.Ready ||
                estimate.Epoch != state.Epoch) return;
            state.BuildingTotalAvailable = Math.Max(0L,
                state.BuildingTotalAvailable -
                Math.Max(0, estimate.Available));
            state.CompletedCityCount = Math.Max(0,
                state.CompletedCityCount - 1);
        }

        private static void Invalidate(Kingdom kingdom,
            KingdomEstimate state)
        {
            RequestRebuild(kingdom, state);
        }

        private static void RequestRebuild(Kingdom kingdom,
            KingdomEstimate state)
        {
            if (state == null || kingdom?.data == null) return;
            if (state.RebuildActive)
            {
                state.RebuildRequested = true;
                return;
            }
            StartGeneration(kingdom, state);
        }

        private static void EnsureGeneration(Kingdom kingdom,
            KingdomEstimate state)
        {
            if (state == null || kingdom?.data == null) return;
            int cityCount = CurrentCityCount(kingdom);
            if (CityReservePoolRules.ShouldRestartCacheGeneration(
                    state.RebuildActive, state.RebuildRequested,
                    state.ExpectedCityCount, cityCount))
                StartGeneration(kingdom, state);
        }

        private static void StartGeneration(Kingdom kingdom,
            KingdomEstimate state)
        {
            if (state == null || kingdom?.data == null) return;
            state.Epoch = state.Epoch == long.MaxValue
                ? 0L
                : state.Epoch + 1L;
            state.BuildingTotalAvailable = 0L;
            state.CompletedCityCount = 0;
            state.ExpectedCityCount = CurrentCityCount(kingdom);
            state.CityCursor = 0;
            state.RebuildActive = state.ExpectedCityCount > 0;
            state.RebuildRequested = false;
            if (!state.RebuildActive)
            {
                state.PublishedTotalAvailable = 0L;
                state.HasPublishedTotal = true;
            }
        }

        private static int CountCityArmyMembers(City city)
        {
            if (!ArmyFieldIndexService.TryGetCityArmy(city, out Army army))
                return 0;
            try { return Math.Max(0, army.countUnits()); }
            catch { return 0; }
        }

        private static KingdomEstimate State(Kingdom kingdom)
        {
            if (States.TryGetValue(kingdom.id,
                    out KingdomEstimate state)) return state;
            state = new KingdomEstimate();
            States[kingdom.id] = state;
            StartGeneration(kingdom, state);
            return state;
        }

        private static int CurrentCityCount(Kingdom kingdom)
        {
            try { return Math.Max(0, kingdom?.countCities() ?? 0); }
            catch { return Math.Max(0, kingdom?.cities?.Count ?? 0); }
        }

        private static CityEstimate Estimate(KingdomEstimate state,
            long cityId)
        {
            if (state.Cities.TryGetValue(cityId,
                    out CityEstimate estimate)) return estimate;
            estimate = new CityEstimate();
            state.Cities[cityId] = estimate;
            return estimate;
        }

        private static int SafePopulation(City city)
        {
            try { return Math.Max(0, city.getPopulationPeople()); }
            catch { return 0; }
        }

        private static bool IsLivingKingdom(Kingdom kingdom)
        {
            try
            {
                return kingdom?.data != null && !kingdom.isRekt() &&
                       !kingdom.isNeutral();
            }
            catch { return false; }
        }

        private static Kingdom ResolveKingdom(long kingdomId)
        {
            try { return World.world?.kingdoms?.get(kingdomId); }
            catch { return null; }
        }

        private static bool IsControlledCity(City city, Kingdom kingdom)
        {
            try
            {
                return city?.data != null && !city.isRekt() &&
                       city.kingdom == kingdom &&
                       OccupiedCitySupplyService.CanProvideToRealm(
                           city, kingdom);
            }
            catch { return false; }
        }

        private static int CountFormalWars(Kingdom kingdom)
        {
            if (kingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (War war in kingdom.getWars())
                    if (ZhuluWarService.ShouldEnrollInAw3Systems(war) &&
                        !war.hasEnded())
                        count++;
            }
            catch { }
            return count;
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double days = Math.Floor(time * 6d);
                return days >= long.MaxValue
                    ? long.MaxValue
                    : (long)days;
            }
            catch { return 0L; }
        }
    }
}
