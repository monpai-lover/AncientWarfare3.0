using System;
using System.Collections.Generic;
using System.IO;
using AncientWarfare3.core.court;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal static class CityReservePoolService
    {
        private sealed class CityPool
        {
            internal readonly SortedSet<long> EligibleActorIds =
                new SortedSet<long>();
            internal readonly SortedSet<long> ActorIds =
                new SortedSet<long>();
            internal int AuthenticPopulation;
            internal int ActiveCitySourcedMilitary;
            internal int SyntheticMobilized;
            internal int WarReserveCapacity;
            internal int WarReserveConsumed;
            internal long WarEmergencyId = -1L;
            internal long ReconciledWorldDay = -1L;
            internal bool Ready;
        }

        private sealed class KingdomPoolState
        {
            internal readonly Dictionary<long, CityPool> Cities =
                new Dictionary<long, CityPool>();
            internal readonly Dictionary<long, int> ActorCursors =
                new Dictionary<long, int>();
            internal readonly Dictionary<long, long> ValidationAfterActorIds =
                new Dictionary<long, long>();
            internal readonly SortedSet<long> LawReconciliationCityIds =
                new SortedSet<long>();
            internal long Generation;
            internal bool Frozen;
            internal long EmergencyId = -1L;
            internal int CityCursor;
            internal long LawReconciliationAfterCityId = -1L;
        }

        private static readonly Dictionary<long, KingdomPoolState> States =
            new Dictionary<long, KingdomPoolState>();
        private static long LastMaintenanceWorldDay = -1L;
        private static int KingdomCursor;
        private static bool RestoreValidationPending;
        private static long RestoreValidationDueWorldDay = -1L;
        // Vanilla may finish its loading flag before replaying actor
        // profession callbacks. Keep a mod-owned gate open for that window so
        // persisted reserve membership is not mistaken for a normal enlistment.
        private static bool WorldLoadRestorePending;

        internal static void BeginWorldLoadRestore()
        {
            WorldLoadRestorePending = true;
        }

        internal static void EndWorldLoadRestore()
        {
            WorldLoadRestorePending = false;
        }

        private sealed class PersistedSnapshot
        {
            public int version = CityReservePoolPersistenceRules.
                CurrentVersion;
            public List<PersistedKingdom> kingdoms =
                new List<PersistedKingdom>();
        }

        private sealed class PersistedKingdom
        {
            public long kingdom_id = -1L;
            public long generation;
            public bool frozen;
            public long emergency_id = -1L;
            public List<PersistedCity> cities = new List<PersistedCity>();
        }

        private sealed class PersistedCity
        {
            public long city_id = -1L;
            public int authentic_population;
            public int active_city_military;
            public int synthetic_mobilized;
            public int war_reserve_capacity;
            public int war_reserve_consumed;
            public long war_emergency_id = -1L;
            public long reconciled_world_day = -1L;
            public bool ready;
        }

        internal static void PrepareForSave()
        {
            if (World.world?.kingdoms == null) return;
            if (States.Count == 0 && World.world.units != null)
                RebuildRuntime();

            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_GENERATION,
                    state.Generation);
                kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN,
                    state.Frozen);
                if (kingdom.cities == null) continue;
                for (int i = 0; i < kingdom.cities.Count; i++)
                    ReconcileLedger(kingdom.cities[i], state);
            }
        }

        internal static bool TryWriteSnapshot(string directory,
            out string error)
        {
            error = string.Empty;
            bool worldReady = World.world?.kingdoms != null &&
                              World.world.units != null;
            bool directoryValid = !string.IsNullOrWhiteSpace(directory);
            if (!CityReservePoolPersistenceRules.ShouldWriteSnapshot(
                    worldReady, directoryValid)) return false;

            string path;
            string temporary = string.Empty;
            try
            {
                PrepareForSave();
                path = CityReservePoolPersistenceRules.ResolveSnapshotPath(
                    directory);
                temporary = path + ".tmp";
                var snapshot = new PersistedSnapshot();
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (!IsLivingKingdom(kingdom)) continue;
                    KingdomPoolState state = State(kingdom);
                    var persistedKingdom = new PersistedKingdom
                    {
                        kingdom_id = kingdom.id,
                        generation = state.Generation,
                        frozen = state.Frozen,
                        emergency_id = state.EmergencyId
                    };
                    foreach (KeyValuePair<long, CityPool> entry in state.Cities)
                    {
                        CityPool pool = entry.Value;
                        if (pool == null) continue;
                        var persistedCity = new PersistedCity
                        {
                            city_id = entry.Key,
                            authentic_population = pool.AuthenticPopulation,
                            active_city_military =
                                pool.ActiveCitySourcedMilitary,
                            synthetic_mobilized = pool.SyntheticMobilized,
                            war_reserve_capacity = pool.WarReserveCapacity,
                            war_reserve_consumed = pool.WarReserveConsumed,
                            war_emergency_id = pool.WarEmergencyId,
                            reconciled_world_day = pool.ReconciledWorldDay,
                            ready = pool.Ready
                        };
                        persistedKingdom.cities.Add(persistedCity);
                    }
                    snapshot.kingdoms.Add(persistedKingdom);
                }

                string payload = JsonConvert.SerializeObject(snapshot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, payload);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                try
                {
                    if (!string.IsNullOrEmpty(temporary) &&
                        File.Exists(temporary)) File.Delete(temporary);
                }
                catch { }
                return false;
            }
        }

        internal static bool TryRestoreSnapshot(string directory,
            out string error)
        {
            error = string.Empty;
            string path;
            try
            {
                path = CityReservePoolPersistenceRules.ResolveSnapshotPath(
                    directory);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            if (!CityReservePoolPersistenceRules.ShouldRestoreSnapshot(
                    File.Exists(path), World.world != null)) return false;

            try
            {
                PersistedSnapshot snapshot = JsonConvert.DeserializeObject<
                    PersistedSnapshot>(File.ReadAllText(path));
                if (snapshot?.kingdoms == null ||
                    !CityReservePoolPersistenceRules.CanUseSnapshotVersion(
                        snapshot.version)) return false;
                for (int i = 0; i < snapshot.kingdoms.Count; i++)
                {
                    PersistedKingdom persisted = snapshot.kingdoms[i];
                    Kingdom kingdom = ResolveKingdom(persisted.kingdom_id);
                    if (!IsLivingKingdom(kingdom)) continue;
                    kingdom.data.set(
                        LineageKeys.CITY_RESERVE_KINGDOM_GENERATION,
                        Math.Max(0L, persisted.generation));
                    kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN,
                        persisted.frozen);
                    KingdomPoolState state = State(kingdom);
                    state.Generation = Math.Max(0L, persisted.generation);
                    state.Frozen = persisted.frozen;
                    state.EmergencyId = persisted.emergency_id;
                    if (persisted.cities == null) continue;
                    for (int c = 0; c < persisted.cities.Count; c++)
                    {
                        PersistedCity persistedCity = persisted.cities[c];
                        City city = ResolveCity(persistedCity.city_id);
                        if (city?.data == null || city.kingdom != kingdom)
                            continue;
                        CityPool pool = Pool(state, city.id);
                        pool.AuthenticPopulation = Math.Max(0,
                            persistedCity.authentic_population);
                        pool.ActiveCitySourcedMilitary = Math.Max(0,
                            persistedCity.active_city_military);
                        pool.SyntheticMobilized = Math.Max(0,
                            persistedCity.synthetic_mobilized);
                        pool.WarReserveCapacity = Math.Max(0,
                            persistedCity.war_reserve_capacity);
                        pool.WarReserveConsumed = Math.Max(0,
                            Math.Min(pool.WarReserveCapacity,
                                persistedCity.war_reserve_consumed));
                        pool.WarEmergencyId = persistedCity.war_emergency_id;
                        pool.ReconciledWorldDay =
                            persistedCity.reconciled_world_day;
                        pool.Ready = persistedCity.ready;
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static void ProcessAuthorityCycle()
        {
            long worldDay = CurrentWorldDay();
            bool worldDayChanged = worldDay != LastMaintenanceWorldDay;
            if (!worldDayChanged) return;
            LastMaintenanceWorldDay = worldDay;

            if (RestoreValidationPending &&
                worldDay >= RestoreValidationDueWorldDay)
            {
                RebuildRuntime(validatePersistedMembers: true);
                return;
            }

            List<Kingdom> kingdoms = World.world?.kingdoms?.list;
            int kingdomCount = kingdoms?.Count ?? 0;
            if (kingdomCount <= 0) return;
            if (KingdomCursor < 0 || KingdomCursor >= kingdomCount)
                KingdomCursor = 0;
            Kingdom kingdom = kingdoms[KingdomCursor++];
            if (KingdomCursor >= kingdomCount) KingdomCursor = 0;
            if (!IsLivingKingdom(kingdom) || kingdom.cities == null ||
                kingdom.cities.Count == 0) return;
            KingdomPoolState state = State(kingdom);
            bool lawReconciliation =
                state.LawReconciliationCityIds.Count > 0;
            if (!CityReservePoolRules.CanMaintain(state.Frozen,
                    worldDayChanged) && !lawReconciliation) return;
            bool preparation = WarNoticeService.HasActiveNotice(kingdom) &&
                               !state.Frozen;
            int cityBudget = CityReservePoolRules.CityBudget(preparation);
            int actorBudget = CityReservePoolRules.ActorBudget(preparation);
            for (int i = 0; i < cityBudget && kingdom.cities.Count > 0; i++)
            {
                bool explicitLawWork = TryNextLawReconciliationCity(state,
                    out long cityId);
                City city;
                if (explicitLawWork)
                    city = ResolveCity(cityId);
                else
                {
                    if (state.CityCursor < 0 ||
                        state.CityCursor >= kingdom.cities.Count)
                        state.CityCursor = 0;
                    city = kingdom.cities[state.CityCursor++];
                }
                bool complete = MaintainCity(kingdom, city, state,
                    actorBudget, explicitLawWork && state.Frozen);
                ReconcileLedger(city, state);
                if (explicitLawWork && complete)
                    state.LawReconciliationCityIds.Remove(cityId);
            }
        }

        internal static void OnWarStarted(War war)
        {
            if (war?.data == null || war.hasEnded()) return;
            foreach (Kingdom kingdom in war.getAttackers())
                OpenWarEmergency(kingdom, war.data.id);
            foreach (Kingdom kingdom in war.getDefenders())
                OpenWarEmergency(kingdom, war.data.id);
        }

        internal static void OnKingdomJoinedWar(War war, Kingdom kingdom)
        {
            bool warActive = war?.data != null && !war.hasEnded();
            bool liveKingdom = IsLivingKingdom(kingdom);
            if (!CityReservePoolRules.ShouldReconcileJoiningKingdom(
                    warActive, liveKingdom)) return;
            if (!EnsureRestoreValidationForWarEntry()) return;
            CompletePreWarReconciliation(kingdom,
                new HashSet<long>());
            OpenWarEmergency(kingdom, war.data.id);
        }

        internal static void OnWarEnded(War war)
        {
            if (war?.data == null) return;
            foreach (Kingdom kingdom in war.getAttackers())
                ReevaluateFreeze(kingdom);
            foreach (Kingdom kingdom in war.getDefenders())
                ReevaluateFreeze(kingdom);
        }

        internal static void OnKingdomLeftWar(War war, Kingdom kingdom)
        {
            ReevaluateFreeze(kingdom);
        }

        internal static void OnActorBecameAdult(Actor actor)
        {
            City city = actor?.city;
            Kingdom kingdom = actor?.kingdom;
            if (actor?.data == null || city?.data == null ||
                kingdom?.data == null || city.kingdom != kingdom) return;

            KingdomPoolState state = State(kingdom);
            CityPool pool = Pool(state, city.id);
            if (!IndexEligibleActor(actor, kingdom, city, pool)) return;
            ReconcilePool(kingdom, city, state, pool,
                allowFrozenAddition: false, additionBudget: 1);
            MarkCityDirty(city);
        }

        internal static void OnActorReturnedToCivilian(Actor actor)
        {
            OnActorBecameAdult(actor);
        }

        internal static void OnActorInvalidated(Actor actor)
        {
            City city = actor?.city;
            RemoveActorFromIndexes(actor, actor?.kingdom, city);
            MarkCityDirty(city);
        }

        internal static void OnActorCityChanged(Actor actor,
            City previousCity)
        {
            if (actor?.data == null || actor.city == previousCity) return;
            if (ShouldDeferPersistedInvalidation(actor)) return;
            RemoveActorFromIndexes(actor, previousCity?.kingdom,
                previousCity);
            MarkCityDirty(previousCity);
            OnActorReturnedToCivilian(actor);
        }

        internal static void OnActorKingdomChanged(Actor actor,
            Kingdom previousKingdom)
        {
            if (actor?.data == null || actor.kingdom == previousKingdom)
                return;
            if (ShouldDeferPersistedInvalidation(actor)) return;
            RemoveActorFromIndexes(actor, previousKingdom, actor.city);
            OnActorReturnedToCivilian(actor);
        }

        internal static void OnActorEnlisted(Actor actor)
        {
            if (ShouldDeferPersistedInvalidation(actor)) return;
            City city = actor?.city;
            RemoveActorFromIndexes(actor, actor?.kingdom, city);
            MarkCityDirty(city);
        }

        internal static void OnActorProfessionChanged(Actor actor)
        {
            City city = actor?.city;
            Kingdom kingdom = actor?.kingdom;
            if (ShouldDeferPersistedInvalidation(actor)) return;
            if (actor?.data != null && city?.data != null &&
                kingdom?.data != null && city.kingdom == kingdom &&
                TemporaryLevyService.CanRegisterReserve(kingdom, city,
                    actor))
            {
                OnActorReturnedToCivilian(actor);
                return;
            }
            RemoveActorFromIndexes(actor, kingdom, city);
            MarkCityDirty(city);
        }

        internal static void OnConscriptionLawChanged(Kingdom kingdom,
            CourtConscriptionLaw previousLaw, CourtConscriptionLaw nextLaw)
        {
            if (kingdom?.data == null || previousLaw == nextLaw) return;
            KingdomPoolState state = State(kingdom);
            state.CityCursor = 0;
            state.ActorCursors.Clear();
            state.ValidationAfterActorIds.Clear();
            int previousPercent = CourtConscriptionLawRules.ReservePercent(
                previousLaw);
            int nextPercent = CourtConscriptionLawRules.ReservePercent(
                nextLaw);
            foreach (KeyValuePair<long, CityPool> entry in state.Cities)
            {
                City city = ResolveCity(entry.Key);
                ReconcilePool(kingdom, city, state, entry.Value,
                    allowFrozenAddition: false, additionBudget: 0);
            }
            if (nextPercent > previousPercent && kingdom.cities != null)
                for (int i = 0; i < kingdom.cities.Count; i++)
                {
                    City city = kingdom.cities[i];
                    if (city?.data != null && city.kingdom == kingdom)
                        state.LawReconciliationCityIds.Add(city.id);
                }
            if (!CityReservePoolRules.ShouldAddForLawChange(state.Frozen,
                    previousPercent, nextPercent) && state.Frozen)
                state.LawReconciliationCityIds.Clear();
            state.LawReconciliationAfterCityId = -1L;
            LastMaintenanceWorldDay = -1L;
        }

        internal static void OnCityKingdomChanged(City city,
            Kingdom previousKingdom, Kingdom currentKingdom)
        {
            if (city?.data == null || previousKingdom?.data == null ||
                previousKingdom == currentKingdom ||
                !States.TryGetValue(previousKingdom.id,
                    out KingdomPoolState state) ||
                !state.Cities.TryGetValue(city.id, out CityPool pool))
                return;
            long[] actorIds = new long[pool.ActorIds.Count];
            pool.ActorIds.CopyTo(actorIds);
            for (int i = 0; i < actorIds.Length; i++)
            {
                Actor actor = ResolveActor(actorIds[i]);
                if (actor?.data != null) ClearFields(actor);
            }
            state.Cities.Remove(city.id);
            state.LawReconciliationCityIds.Remove(city.id);
            RemoveEmptyState(previousKingdom.id, state);
        }

        internal static void RefreshCapturedCity(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || city.isRekt() ||
                !IsLivingKingdom(kingdom)) return;
            KingdomPoolState state = State(kingdom);
            CityPool pool = Pool(state, city.id);
            int budget = CityReservePoolRules.FullReconciliationBudget(
                city.units?.Count ?? 0, pool.ActorIds.Count);
            MaintainCity(kingdom, city, state, budget,
                allowFrozenAddition: true);
            ReconcileLedger(city, state);
            if (state.Frozen)
                OpenCityWarReserve(city, state, state.EmergencyId);
        }

        internal static int CountAvailable(Kingdom kingdom)
        {
            if (kingdom?.data == null ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state)) return 0;
            bool formalWar = ResolveMobilizationPhase(kingdom) ==
                             ArmyMobilizationPhase.War;
            long count = 0L;
            foreach (KeyValuePair<long, CityPool> entry in state.Cities)
            {
                if (formalWar)
                {
                    City city = ResolveCity(entry.Key);
                    ReconcileLedger(city, state);
                    count += CountAvailable(city, state);
                }
                else
                    count += entry.Value.ActorIds.Count;
                if (count >= int.MaxValue) return int.MaxValue;
            }
            return (int)count;
        }

        internal static int CountAvailable(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || kingdom?.data == null ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state) ||
                !state.Cities.TryGetValue(city.id, out CityPool pool))
                return 0;
            if (ResolveMobilizationPhase(kingdom) !=
                ArmyMobilizationPhase.War) return pool.ActorIds.Count;
            ReconcileLedger(city, state);
            return CountAvailable(city, state);
        }

        internal static bool IsFrozen(Kingdom kingdom)
        {
            return kingdom?.data != null &&
                   States.TryGetValue(kingdom.id,
                       out KingdomPoolState state) && state.Frozen;
        }

        internal static int TryConsumeBatch(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeFromSourceCity(kingdom, preferredCity,
                requested, targetArmy, destination,
                out confirmedExhausted);
        }

        // An ordinary RTS army is supplied by the city that raised it.  This
        // deliberately does not select a nearby donor from another city.
        internal static int TryConsumeFromSourceCity(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeForMobilization(kingdom, preferredCity,
                requested, targetArmy, allowArmyCreation: false,
                destination, out confirmedExhausted);
        }

        internal static int TryConsumePreparationBatch(Kingdom kingdom,
            City preferredCity, int approvedShortage, Army targetArmy,
            List<Actor> destination, out bool confirmedExhausted)
        {
            return TryConsumeForMobilization(kingdom, preferredCity,
                approvedShortage, targetArmy,
                allowArmyCreation: targetArmy?.data == null,
                destination, out confirmedExhausted);
        }

        internal static int TryConsumeForMobilization(Kingdom kingdom,
            City preferredCity, int requested, Army targetArmy,
            bool allowArmyCreation, List<Actor> destination,
            out bool confirmedExhausted)
        {
            confirmedExhausted = false;
            int requestedCount = Math.Max(0, requested);
            if (requestedCount <= 0 || destination == null ||
                preferredCity?.data == null || !IsLivingKingdom(kingdom))
                return 0;

            ArmyMobilizationPhase phase = ResolveMobilizationPhase(kingdom);
            if (phase != ArmyMobilizationPhase.Notice) return 0;
            bool realmControlled = IsControlledCity(preferredCity, kingdom) &&
                                   OccupiedCitySupplyService.CanProvideToRealm(
                                       preferredCity, kingdom);
            if (!CityReservePoolRules.CanConsumeForMobilization(phase,
                    realmControlled, SafePopulation(preferredCity))) return 0;

            bool creating = targetArmy?.data == null;
            if (creating && (!allowArmyCreation ||
                             !ArmyMobilizationRules.
                                 CanCreateOrdinaryArmy(phase)) ||
                !creating &&
                (!IsLiveOrdinaryTargetArmy(targetArmy, kingdom) ||
                 !CityReservePoolRules.MatchesSourceCity(
                     preferredCity.id,
                     AWArmyService.GetAnchorCityId(targetArmy)))) return 0;

            KingdomPoolState state = State(kingdom);
            CityPool pool = Pool(state, preferredCity.id);
            int reconciliationBudget = CityReservePoolRules.
                FullReconciliationBudget(
                    preferredCity.units?.Count ?? 0,
                    pool.ActorIds.Count);
            bool reconciliationComplete = MaintainCity(kingdom,
                preferredCity, state, reconciliationBudget,
                allowFrozenAddition: false);
            ReconcileLedger(preferredCity, state);
            int added = 0;
            while (added < requestedCount &&
                   CityReservePoolRules.TryTakeNextActorId(pool.ActorIds,
                       out long actorId))
            {
                Actor actor = ResolveActor(actorId);
                pool.EligibleActorIds.Remove(actorId);
                if (!IsValidMember(actor, kingdom, preferredCity,
                        state.Generation))
                {
                    if (actor?.data != null) ClearFields(actor);
                    continue;
                }
                ClearFields(actor);
                destination.Add(actor);
                added++;
            }
            confirmedExhausted = CityReservePoolRules.CanConfirmExhausted(
                reconciliationComplete, pool.ActorIds.Count);
            return added;
        }

        internal static ArmyMobilizationPhase ResolveMobilizationPhase(
            Kingdom kingdom)
        {
            return ArmyMobilizationRules.Resolve(
                IsLivingKingdom(kingdom),
                WarNoticeService.HasActiveNotice(kingdom),
                CountFormalWars(kingdom));
        }

        internal static void RestoreRejectedCandidates(Kingdom kingdom,
            City sourceCity, Army targetArmy,
            IReadOnlyList<Actor> candidates)
        {
            if (kingdom?.data == null || sourceCity?.data == null ||
                candidates == null ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state)) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                Actor actor = candidates[i];
                bool alive;
                bool enlistedIntoTargetArmy;
                try
                {
                    alive = actor?.data != null && actor.isAlive() &&
                            !actor.isRekt();
                    enlistedIntoTargetArmy = alive && actor.isWarrior() &&
                                              actor.army == targetArmy;
                }
                catch
                {
                    alive = false;
                    enlistedIntoTargetArmy = false;
                }
                bool reserveEligible = alive &&
                    TemporaryLevyService.CanRegisterReserve(kingdom,
                        sourceCity, actor);
                CityPool pool = Pool(state, sourceCity.id);
                if (!CityReservePoolRules.RestoreRejectedActorId(
                        pool.EligibleActorIds, pool.ActorIds,
                        actor?.data?.id ?? -1L,
                        sameKingdom: alive && actor.kingdom == kingdom &&
                            sourceCity.kingdom == kingdom,
                        sameCity: alive && actor.city == sourceCity,
                        alive: alive, reserveEligible: reserveEligible,
                        enlistedIntoTargetArmy: enlistedIntoTargetArmy))
                    continue;

                SetMemberFields(actor, kingdom, sourceCity,
                    state.Generation);
            }
            CityPool sourcePool = Pool(state, sourceCity.id);
            ReconcilePool(kingdom, sourceCity, state, sourcePool,
                allowFrozenAddition: false, additionBudget: 0);
            MarkCityDirty(sourceCity);
        }

        internal static void CompletePreWarReconciliation(War war)
        {
            if (war?.data == null || war.hasEnded()) return;
            if (!EnsureRestoreValidationForWarEntry()) return;
            var participantIds = new HashSet<long>();
            foreach (Kingdom kingdom in war.getAttackers())
                CompletePreWarReconciliation(kingdom, participantIds);
            foreach (Kingdom kingdom in war.getDefenders())
                CompletePreWarReconciliation(kingdom, participantIds);
        }

        internal static bool PrepareWarEntry(Kingdom first,
            Kingdom second = null)
        {
            if (!EnsureRestoreValidationForWarEntry()) return false;
            var participantIds = new HashSet<long>();
            CompletePreWarReconciliation(first, participantIds);
            CompletePreWarReconciliation(second, participantIds);
            return true;
        }

        private static bool EnsureRestoreValidationForWarEntry()
        {
            bool validationPending = CityReservePoolRules.
                RequiresImmediateWarEntryValidation(
                    RestoreValidationPending);
            if (!validationPending) return true;
            bool validationCompleted = ValidatePendingRestoreRuntime();
            return CityReservePoolRules.CanAdvanceWarEntry(
                validationPending, validationCompleted);
        }

        private static bool ValidatePendingRestoreRuntime()
        {
            if (World.world?.units == null ||
                World.world.kingdoms == null) return false;

            foreach (Actor actor in World.world.units)
            {
                if (actor?.data == null) continue;
                actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                    out bool member, false);
                if (!member) continue;
                actor.data.get(LineageKeys.CITY_RESERVE_CITY_ID,
                    out long cityId, -1L);
                actor.data.get(LineageKeys.CITY_RESERVE_KINGDOM_ID,
                    out long kingdomId, -1L);
                actor.data.get(LineageKeys.CITY_RESERVE_GENERATION,
                    out long generation, -1L);
                City city = ResolveCity(cityId);
                Kingdom kingdom = ResolveKingdom(kingdomId);
                KingdomPoolState state = kingdom?.data != null
                    ? State(kingdom)
                    : null;
                bool valid = city?.data != null && state != null &&
                    actor.city == city && actor.kingdom == kingdom &&
                    city.kingdom == kingdom &&
                    generation == state.Generation &&
                    TemporaryLevyService.CanRegisterReserve(kingdom, city,
                        actor);
                if (!valid)
                {
                    RemoveActorFromIndexes(actor, kingdom, city);
                    continue;
                }
                CityPool pool = Pool(state, city.id);
                pool.EligibleActorIds.Add(actor.data.id);
                pool.ActorIds.Add(actor.data.id);
            }

            RestoreValidationPending = false;
            RestoreValidationDueWorldDay = -1L;
            var reconciledKingdomIds = new HashSet<long>();
            foreach (Kingdom kingdom in World.world.kingdoms)
                CompletePreWarReconciliation(kingdom,
                    reconciledKingdomIds);
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (CountFormalWars(kingdom) > 0)
                    OpenWarEmergency(kingdom,
                        ResolveFirstWarId(kingdom));
                else
                    ReevaluateFreeze(kingdom);
            }
            return true;
        }

        private static void CompletePreWarReconciliation(Kingdom kingdom,
            HashSet<long> participantIds)
        {
            if (!IsLivingKingdom(kingdom) ||
                !participantIds.Add(kingdom.id) ||
                kingdom.cities == null) return;
            KingdomPoolState state = State(kingdom);
            for (int i = 0; i < kingdom.cities.Count; i++)
            {
                City city = kingdom.cities[i];
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != kingdom) continue;
                CityPool pool = Pool(state, city.id);
                int budget = CityReservePoolRules.FullReconciliationBudget(
                    city.units?.Count ?? 0, pool.ActorIds.Count);
                MaintainCity(kingdom, city, state, budget,
                    allowFrozenAddition: true);
                ReconcileLedger(city, state);
            }
        }

        internal static void RebuildRuntime()
        {
            RebuildRuntime(validatePersistedMembers: false);
        }

        internal static void FinalizeRuntimeRestore(bool snapshotRestored)
        {
            bool formalWarActive = false;
            if (World.world?.kingdoms != null)
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (CountFormalWars(kingdom) > 0)
                    {
                        formalWarActive = true;
                        break;
                    }
            CityReservePoolFinalRestoreActions actions =
                CityReservePoolPersistenceRules.ResolveFinalRestoreActions(
                    snapshotRestored, actorCallbacksComplete: true,
                    formalWarActive);
            if ((actions & CityReservePoolFinalRestoreActions.
                    ValidateMembers) == 0) return;

            RebuildRuntime(validatePersistedMembers: true);
            if (World.world?.kingdoms == null) return;
            var reconciledKingdomIds = new HashSet<long>();
            foreach (Kingdom kingdom in World.world.kingdoms)
                CompletePreWarReconciliation(kingdom,
                    reconciledKingdomIds);

            if ((actions & CityReservePoolFinalRestoreActions.
                    RestoreWarEmergency) == 0) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (CountFormalWars(kingdom) <= 0) continue;
                OpenWarEmergency(kingdom, ResolveFirstWarId(kingdom));
            }
        }

        private static void RebuildRuntime(bool validatePersistedMembers)
        {
            ClearRuntime();
            if (World.world?.units == null) return;
            foreach (Actor actor in World.world.units)
            {
                if (actor?.data == null) continue;
                City currentCity = actor.city;
                Kingdom currentKingdom = actor.kingdom;
                bool eligible = currentCity?.data != null &&
                    currentKingdom?.data != null &&
                    currentCity.kingdom == currentKingdom &&
                    TemporaryLevyService.CanRegisterReserve(currentKingdom,
                        currentCity, actor);

                actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                    out bool member, false);
                if (!member)
                {
                    if (eligible)
                    {
                        CityPool currentPool = Pool(State(currentKingdom),
                            currentCity.id);
                        currentPool.EligibleActorIds.Add(actor.data.id);
                    }
                    continue;
                }

                if (CityReservePoolRules.ShouldDeferPersistedMemberValidation(
                        true, validatePersistedMembers))
                    RestoreValidationPending = true;

                actor.data.get(LineageKeys.CITY_RESERVE_CITY_ID,
                    out long cityId, -1L);
                actor.data.get(LineageKeys.CITY_RESERVE_KINGDOM_ID,
                    out long kingdomId, -1L);
                actor.data.get(LineageKeys.CITY_RESERVE_GENERATION,
                    out long generation, -1L);
                City city = ResolveCity(cityId);
                Kingdom kingdom = ResolveKingdom(kingdomId);
                bool sourceResolved = city?.data != null &&
                    kingdom?.data != null && actor.city == city &&
                    actor.kingdom == kingdom && city.kingdom == kingdom;
                if (!sourceResolved)
                {
                    if (validatePersistedMembers) ClearFields(actor);
                    continue;
                }
                bool currentlyEligible = TemporaryLevyService.
                    CanRegisterReserve(kingdom, city, actor);
                if (validatePersistedMembers && !currentlyEligible)
                {
                    ClearFields(actor);
                    continue;
                }
                KingdomPoolState state = State(kingdom);
                if (generation != state.Generation)
                {
                    if (validatePersistedMembers) ClearFields(actor);
                    continue;
                }
                CityPool pool = Pool(state, city.id);
                pool.EligibleActorIds.Add(actor.data.id);
                pool.ActorIds.Add(actor.data.id);
            }

            if (RestoreValidationPending)
            {
                long worldDay = CurrentWorldDay();
                RestoreValidationDueWorldDay = worldDay >= long.MaxValue
                    ? long.MaxValue
                    : worldDay + 1L;
            }

            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                if (RestoreValidationPending && !validatePersistedMembers)
                    continue;
                if (kingdom.cities != null)
                    for (int i = 0; i < kingdom.cities.Count; i++)
                    {
                        City city = kingdom.cities[i];
                        CityPool pool = Pool(state, city.id);
                        ReconcilePool(kingdom, city, state, pool,
                            allowFrozenAddition: false,
                            additionBudget: 0);
                        ReconcileLedger(kingdom.cities[i], state);
                    }
                int activeWarCount = CountFormalWars(kingdom);
                if (activeWarCount > 0)
                {
                    long emergencyId = ResolveFirstWarId(kingdom);
                    OpenWarEmergency(kingdom, emergencyId);
                    continue;
                }
                state.Frozen = false;
                state.EmergencyId = -1L;
            }
        }

        internal static void ClearRuntime()
        {
            States.Clear();
            LastMaintenanceWorldDay = -1L;
            KingdomCursor = 0;
            RestoreValidationPending = false;
            RestoreValidationDueWorldDay = -1L;
        }

        private static bool MaintainCity(Kingdom kingdom, City city,
            KingdomPoolState state, int actorBudget,
            bool allowFrozenAddition)
        {
            if (city?.data == null || city.isRekt() ||
                city.kingdom != kingdom || actorBudget <= 0) return true;
            CityPool pool = Pool(state, city.id);
            int remainingBudget = ValidateMembers(kingdom, city, state, pool,
                actorBudget);
            if (remainingBudget <= 0)
            {
                ReconcilePool(kingdom, city, state, pool,
                    allowFrozenAddition, actorBudget);
                return false;
            }
            if (city.units == null || city.units.Count == 0)
            {
                ReconcilePool(kingdom, city, state, pool,
                    allowFrozenAddition, actorBudget);
                return true;
            }

            state.ActorCursors.TryGetValue(city.id, out int cursor);
            int residentCount = city.units.Count;
            if (cursor < 0 || cursor >= residentCount) cursor = 0;
            int inspected = 0;
            bool completePass = false;
            while (inspected < remainingBudget && inspected < residentCount)
            {
                Actor actor = city.units[cursor];
                cursor++;
                if (cursor >= residentCount)
                {
                    cursor = 0;
                    completePass = true;
                }
                inspected++;
                IndexEligibleActor(actor, kingdom, city, pool);
            }
            state.ActorCursors[city.id] = cursor;
            ReconcilePool(kingdom, city, state, pool,
                allowFrozenAddition, actorBudget);
            return completePass;
        }

        private static int ValidateMembers(Kingdom kingdom, City city,
            KingdomPoolState state, CityPool pool, int actorBudget)
        {
            if (pool.ActorIds.Count == 0) return actorBudget;
            state.ValidationAfterActorIds.TryGetValue(city.id,
                out long afterActorId);
            // Keep the scan logically unbounded when the caller requests a
            // full pass, but never use that value as an allocation size.
            var inspectedIds = new List<long>(Math.Min(actorBudget, 256));
            foreach (long actorId in pool.ActorIds)
            {
                if (actorId <= afterActorId) continue;
                inspectedIds.Add(actorId);
                if (inspectedIds.Count >= actorBudget) break;
            }
            if (inspectedIds.Count == 0 && afterActorId >= 0L)
            {
                afterActorId = -1L;
                foreach (long actorId in pool.ActorIds)
                {
                    inspectedIds.Add(actorId);
                    if (inspectedIds.Count >= actorBudget) break;
                }
            }

            for (int i = 0; i < inspectedIds.Count; i++)
            {
                long actorId = inspectedIds[i];
                Actor actor = ResolveActor(actorId);
                if (IsValidMember(actor, kingdom, city, state.Generation))
                    continue;
                pool.ActorIds.Remove(actorId);
                pool.EligibleActorIds.Remove(actorId);
                if (actor?.data != null) ClearFields(actor);
            }
            if (inspectedIds.Count > 0)
                state.ValidationAfterActorIds[city.id] =
                    inspectedIds[inspectedIds.Count - 1];
            return Math.Max(0, actorBudget - inspectedIds.Count);
        }

        private static bool IsValidMember(Actor actor, Kingdom kingdom,
            City city, long generation)
        {
            if (actor?.data == null || actor.city != city ||
                actor.kingdom != kingdom || !actor.isAlive() ||
                actor.isRekt() ||
                !TemporaryLevyService.CanRegisterReserve(kingdom, city,
                    actor)) return false;
            actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                out bool member, false);
            actor.data.get(LineageKeys.CITY_RESERVE_CITY_ID,
                out long cityId, -1L);
            actor.data.get(LineageKeys.CITY_RESERVE_KINGDOM_ID,
                out long kingdomId, -1L);
            actor.data.get(LineageKeys.CITY_RESERVE_GENERATION,
                out long actorGeneration, -1L);
            return member && cityId == city.id && kingdomId == kingdom.id &&
                   actorGeneration == generation;
        }

        private static bool IndexEligibleActor(Actor actor, Kingdom kingdom,
            City city, CityPool pool)
        {
            if (actor?.data == null ||
                !TemporaryLevyService.CanRegisterReserve(kingdom, city,
                    actor))
            {
                if (actor?.data != null)
                {
                    pool.EligibleActorIds.Remove(actor.data.id);
                    if (pool.ActorIds.Remove(actor.data.id))
                        ClearFields(actor);
                }
                return false;
            }
            pool.EligibleActorIds.Add(actor.data.id);
            return true;
        }

        private static void ReconcilePool(Kingdom kingdom, City city,
            KingdomPoolState state, CityPool pool,
            bool allowFrozenAddition, int additionBudget)
        {
            if (kingdom?.data == null || city?.data == null ||
                city.kingdom != kingdom || pool == null) return;
            int percent = CourtConscriptionLawRules.ReservePercent(
                CourtAuxiliaryLawService.GetConscriptionLaw(kingdom));
            pool.ActiveCitySourcedMilitary = CountCityArmyMembers(city);
            int capacity = CityReservePoolRules.AvailablePoolCapacity(
                pool.EligibleActorIds.Count,
                pool.ActiveCitySourcedMilitary, percent);
            ShrinkPoolToCapacity(pool, capacity);
            if (additionBudget <= 0 ||
                state.Frozen && !allowFrozenAddition ||
                pool.ActorIds.Count >= capacity) return;

            var invalidIds = new List<long>();
            int inspected = 0;
            foreach (long actorId in pool.EligibleActorIds)
            {
                if (pool.ActorIds.Contains(actorId)) continue;
                if (inspected >= additionBudget ||
                    pool.ActorIds.Count >= capacity) break;
                inspected++;
                Actor actor = ResolveActor(actorId);
                if (actor?.data == null ||
                    !TemporaryLevyService.CanRegisterReserve(kingdom, city,
                        actor))
                {
                    invalidIds.Add(actorId);
                    continue;
                }
                SetMemberFields(actor, kingdom, city, state.Generation);
                pool.ActorIds.Add(actorId);
            }
            for (int i = 0; i < invalidIds.Count; i++)
                pool.EligibleActorIds.Remove(invalidIds[i]);
            capacity = CityReservePoolRules.AvailablePoolCapacity(
                pool.EligibleActorIds.Count,
                pool.ActiveCitySourcedMilitary, percent);
            ShrinkPoolToCapacity(pool, capacity);
        }

        private static void ShrinkPoolToCapacity(CityPool pool, int capacity)
        {
            int removals = CityReservePoolRules.RequiredRemovalCount(
                pool.ActorIds.Count, capacity);
            while (removals-- > 0 && pool.ActorIds.Count > 0)
            {
                long actorId = pool.ActorIds.Max;
                pool.ActorIds.Remove(actorId);
                Actor actor = ResolveActor(actorId);
                if (actor?.data != null) ClearFields(actor);
            }
        }

        private static void SetMemberFields(Actor actor, Kingdom kingdom,
            City city, long generation)
        {
            actor.data.set(LineageKeys.CITY_RESERVE_MEMBER, true);
            actor.data.set(LineageKeys.CITY_RESERVE_CITY_ID, city.id);
            actor.data.set(LineageKeys.CITY_RESERVE_KINGDOM_ID, kingdom.id);
            actor.data.set(LineageKeys.CITY_RESERVE_GENERATION, generation);
        }

        private static bool TryNextLawReconciliationCity(
            KingdomPoolState state, out long cityId)
        {
            cityId = -1L;
            if (state?.LawReconciliationCityIds == null ||
                state.LawReconciliationCityIds.Count == 0) return false;
            foreach (long candidate in state.LawReconciliationCityIds)
            {
                if (candidate <= state.LawReconciliationAfterCityId) continue;
                cityId = candidate;
                break;
            }
            if (cityId < 0L) cityId = state.LawReconciliationCityIds.Min;
            state.LawReconciliationAfterCityId = cityId;
            return true;
        }

        internal static int OpenOrReadWarReserve(City city,
            long emergencyId)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsLivingKingdom(kingdom) || city?.data == null) return -1;
            KingdomPoolState state = State(kingdom);
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            if (emergencyId < 0L) return -1;
            ReconcileLedger(city, state);
            OpenCityWarReserve(city, state, emergencyId);
            CityPool pool = Pool(state, city.id);
            return CityManpowerRules.WarReserveAvailable(
                pool.WarReserveCapacity, pool.WarReserveConsumed);
        }

        internal static int TryReserveWarManpower(City city,
            long emergencyId, int requested)
        {
            KingdomPoolState state = city?.kingdom?.data != null
                ? State(city.kingdom)
                : null;
            if (state == null) return 0;
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            int available = OpenOrReadWarReserve(city, emergencyId);
            if (available <= 0) return 0;
            CityPool pool = Pool(state, city.id);
            int reserved = Math.Min(Math.Max(0, requested), available);
            pool.WarReserveConsumed += reserved;
            return reserved;
        }

        internal static void ReleaseUnmaterializedWarReservation(City city,
            long emergencyId, int count)
        {
            if (city?.data == null || count <= 0 ||
                !States.TryGetValue(city.kingdom?.id ?? -1L,
                    out KingdomPoolState state) ||
                !state.Cities.TryGetValue(city.id, out CityPool pool)) return;
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            if (
                pool.WarEmergencyId != emergencyId) return;
            pool.WarReserveConsumed = Math.Max(0,
                pool.WarReserveConsumed - count);
        }

        internal static void OnAuthenticMobilized(City city, int count)
        {
            if (count > 0) MarkCityDirty(city);
        }

        internal static void OnSyntheticMobilized(City city, int count)
        {
            if (city?.data == null || count <= 0) return;
            KingdomPoolState state = State(city.kingdom);
            CityPool pool = Pool(state, city.id);
            pool.SyntheticMobilized = SaturatingAdd(
                pool.SyntheticMobilized, count);
            pool.ActiveCitySourcedMilitary = SaturatingAdd(
                pool.ActiveCitySourcedMilitary, count);
            pool.Ready = true;
        }

        internal static void OnSyntheticRemoved(City city, int count)
        {
            if (city?.data == null || count <= 0) return;
            KingdomPoolState state = State(city.kingdom);
            CityPool pool = Pool(state, city.id);
            pool.SyntheticMobilized = Math.Max(0,
                pool.SyntheticMobilized - count);
            pool.Ready = false;
        }

        private static void OpenWarEmergency(Kingdom kingdom,
            long emergencyId)
        {
            if (!IsLivingKingdom(kingdom) || emergencyId < 0L) return;
            KingdomPoolState state = State(kingdom);
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            if (emergencyId < 0L) return;
            if (state.Frozen)
            {
                state.EmergencyId = emergencyId;
                if (kingdom.cities == null) return;
                for (int i = 0; i < kingdom.cities.Count; i++)
                {
                    City city = kingdom.cities[i];
                    ReconcileLedger(city, state);
                    OpenCityWarReserve(city, state, emergencyId);
                }
                return;
            }
            state.Generation = state.Generation >= long.MaxValue
                ? long.MaxValue
                : state.Generation + 1L;
            state.Frozen = true;
            state.EmergencyId = emergencyId;
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_GENERATION,
                state.Generation);
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN, true);
            foreach (CityPool pool in state.Cities.Values)
            {
                long[] actorIds = new long[pool.ActorIds.Count];
                pool.ActorIds.CopyTo(actorIds);
                for (int i = 0; i < actorIds.Length; i++)
                {
                    Actor actor = ResolveActor(actorIds[i]);
                    if (actor?.data == null)
                    {
                        pool.ActorIds.Remove(actorIds[i]);
                        continue;
                    }
                    actor.data.set(LineageKeys.CITY_RESERVE_GENERATION,
                        state.Generation);
                }
            }
            if (kingdom.cities == null) return;
            for (int i = 0; i < kingdom.cities.Count; i++)
            {
                City city = kingdom.cities[i];
                ReconcileLedger(city, state);
                OpenCityWarReserve(city, state, emergencyId);
            }
        }

        private static void OpenCityWarReserve(City city,
            KingdomPoolState state, long emergencyId)
        {
            if (city?.data == null || state == null || emergencyId < 0L)
                return;
            CityPool pool = Pool(state, city.id);
            if (pool.WarEmergencyId == emergencyId) return;
            pool.WarReserveCapacity = CityManpowerRules.OpenWarReserve(
                pool.AuthenticPopulation,
                pool.ActiveCitySourcedMilitary);
            pool.WarReserveConsumed = 0;
            pool.WarEmergencyId = emergencyId;
        }

        private static void ReevaluateFreeze(Kingdom kingdom)
        {
            if (kingdom?.data == null) return;
            int activeWarCount = CountFormalWars(kingdom);
            if (!CityReservePoolRules.ShouldUnfreeze(activeWarCount)) return;
            KingdomPoolState state = State(kingdom);
            state.Frozen = false;
            state.EmergencyId = -1L;
            foreach (CityPool pool in state.Cities.Values)
            {
                pool.WarReserveCapacity = 0;
                pool.WarReserveConsumed = 0;
                pool.WarEmergencyId = -1L;
            }
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN, false);
        }

        private static void ReconcileLedger(City city,
            KingdomPoolState state)
        {
            if (city?.data == null || state == null || city.isRekt() ||
                !IsLivingKingdom(city.kingdom)) return;
            CityPool pool = Pool(state, city.id);
            pool.AuthenticPopulation = Math.Max(0,
                SafePopulation(city) - pool.SyntheticMobilized);
            pool.ActiveCitySourcedMilitary = CountCityArmyMembers(city);
            pool.ReconciledWorldDay = CurrentWorldDay();
            pool.Ready = true;
        }

        private static int CountAvailable(City city,
            KingdomPoolState state)
        {
            CityPool pool = Pool(state, city.id);
            if (!pool.Ready) return -1;
            if (state.Frozen)
            {
                OpenCityWarReserve(city, state, state.EmergencyId);
                return CityManpowerRules.WarReserveAvailable(
                    pool.WarReserveCapacity, pool.WarReserveConsumed);
            }
            return CityManpowerRules.NoticeHeadroom(
                pool.AuthenticPopulation,
                pool.ActiveCitySourcedMilitary);
        }

        private static bool SelectAuthenticResidents(Kingdom kingdom,
            City city, KingdomPoolState state, int requested,
            List<Actor> destination)
        {
            if (requested <= 0 || city?.units == null ||
                city.units.Count == 0) return true;
            int residentCount = city.units.Count;
            state.ActorCursors.TryGetValue(city.id, out int cursor);
            if (cursor < 0 || cursor >= residentCount) cursor = 0;
            int inspected = 0;
            int selected = 0;
            while (inspected < residentCount && selected < requested)
            {
                Actor actor = city.units[cursor++];
                if (cursor >= residentCount) cursor = 0;
                inspected++;
                if (actor?.data == null ||
                    destination.Contains(actor) ||
                    !TemporaryLevyService.CanRegisterReserve(
                        kingdom, city, actor)) continue;
                destination.Add(actor);
                selected++;
            }
            state.ActorCursors[city.id] = cursor;
            return inspected >= residentCount;
        }

        private static void MarkCityDirty(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || !IsLivingKingdom(kingdom)) return;
            Pool(State(kingdom), city.id).Ready = false;
        }

        private static int CountCityArmyMembers(City city)
        {
            if (!ArmyFieldIndexService.TryGetCityArmy(city, out Army army))
                return 0;
            try { return Math.Max(0, army.countUnits()); }
            catch { return 0; }
        }

        private static int SaturatingAdd(int value, int addition)
        {
            long result = (long)Math.Max(0, value) + Math.Max(0, addition);
            return (int)Math.Min(int.MaxValue, result);
        }

        private static long ResolveFirstWarId(Kingdom kingdom)
        {
            try
            {
                foreach (War war in kingdom.getWars())
                    if (war?.data != null && !war.hasEnded())
                        return war.data.id;
            }
            catch { }
            return -1L;
        }

        private static int CountFormalWars(Kingdom kingdom)
        {
            if (kingdom?.data == null) return 0;
            int count = 0;
            try
            {
                foreach (War war in kingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) count++;
            }
            catch { }
            return count;
        }

        private static KingdomPoolState State(Kingdom kingdom)
        {
            if (States.TryGetValue(kingdom.id,
                    out KingdomPoolState state)) return state;
            kingdom.data.get(LineageKeys.CITY_RESERVE_KINGDOM_GENERATION,
                out long generation, 0L);
            kingdom.data.get(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN,
                out bool frozen, false);
            state = new KingdomPoolState
            {
                Generation = Math.Max(0L, generation),
                Frozen = frozen
            };
            States[kingdom.id] = state;
            return state;
        }

        private static CityPool Pool(KingdomPoolState state, long cityId)
        {
            if (state.Cities.TryGetValue(cityId, out CityPool pool))
                return pool;
            pool = new CityPool();
            state.Cities[cityId] = pool;
            return pool;
        }

        private static void RemoveActorFromIndexes(Actor actor,
            Kingdom kingdomHint, City cityHint)
        {
            if (actor?.data == null) return;
            RemoveFromPool(kingdomHint?.id ?? -1L,
                cityHint?.id ?? -1L, actor.data.id);
            actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                out bool member, false);
            actor.data.get(LineageKeys.CITY_RESERVE_CITY_ID,
                out long cityId, -1L);
            actor.data.get(LineageKeys.CITY_RESERVE_KINGDOM_ID,
                out long kingdomId, -1L);
            if (member)
                RemoveFromPool(kingdomId, cityId, actor.data.id);
            ClearFields(actor);
        }

        private static void RemoveFromPool(long kingdomId, long cityId,
            long actorId)
        {
            if (kingdomId < 0L || cityId < 0L || actorId < 0L ||
                !States.TryGetValue(kingdomId,
                    out KingdomPoolState state) ||
                !state.Cities.TryGetValue(cityId, out CityPool pool)) return;
            pool.ActorIds.Remove(actorId);
            pool.EligibleActorIds.Remove(actorId);
            if (pool.ActorIds.Count == 0 &&
                pool.EligibleActorIds.Count == 0)
            {
                state.Cities.Remove(cityId);
                state.ActorCursors.Remove(cityId);
                state.ValidationAfterActorIds.Remove(cityId);
                state.LawReconciliationCityIds.Remove(cityId);
            }
            RemoveEmptyState(kingdomId, state);
        }

        private static void RemoveEmptyState(long kingdomId,
            KingdomPoolState state)
        {
            if (!state.Frozen && state.Cities.Count == 0 &&
                state.LawReconciliationCityIds.Count == 0)
                States.Remove(kingdomId);
        }

        private static void ClearFields(Actor actor)
        {
            actor.data.set(LineageKeys.CITY_RESERVE_MEMBER, false);
            actor.data.set(LineageKeys.CITY_RESERVE_CITY_ID, -1L);
            actor.data.set(LineageKeys.CITY_RESERVE_KINGDOM_ID, -1L);
            actor.data.set(LineageKeys.CITY_RESERVE_GENERATION, -1L);
        }

        private static bool IsRestoreInFlight()
        {
            if (WorldLoadRestorePending || RestoreValidationPending) return true;
            try
            {
                return !Config.game_loaded || SmoothLoader.isLoading();
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldDeferPersistedInvalidation(Actor actor)
        {
            bool persistedMember = false;
            if (actor?.data != null)
                actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                    out persistedMember, false);
            return CityReservePoolRules.ShouldDeferCallbackInvalidation(
                persistedMember, IsRestoreInFlight());
        }

        private static int SafePopulation(City city)
        {
            try { return Math.Max(0, city.getPopulationPeople()); }
            catch { return 0; }
        }

        private static WorldTile SafeCityTile(City city)
        {
            try { return city?.getTile(); }
            catch { return null; }
        }

        private static long DistanceSquared(WorldTile first,
            WorldTile second)
        {
            if (first?.data == null || second?.data == null)
                return long.MaxValue;
            long x = (long)first.x - second.x;
            long y = (long)first.y - second.y;
            long distance;
            try { distance = checked(x * x + y * y); }
            catch { return long.MaxValue; }
            return Math.Max(0L, distance);
        }

        private static Actor ResolveActor(long actorId)
        {
            if (actorId < 0L) return null;
            try { return World.world?.units?.get(actorId); }
            catch { return null; }
        }

        private static City ResolveCity(long cityId)
        {
            if (cityId < 0L) return null;
            try { return World.world?.cities?.get(cityId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long kingdomId)
        {
            if (kingdomId < 0L) return null;
            try { return World.world?.kingdoms?.get(kingdomId); }
            catch { return null; }
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

        private static bool IsLiveOrdinaryTargetArmy(Army army,
            Kingdom kingdom)
        {
            try
            {
                return army?.data != null && army.isAlive() &&
                       !AWArmyService.IsSpecialArmy(army) &&
                       AWArmyService.GetIntendedKingdom(army) == kingdom;
            }
            catch { return false; }
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double days = Math.Floor(time * 6d);
                return days >= long.MaxValue ? long.MaxValue : (long)days;
            }
            catch { return 0L; }
        }
    }
}
