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
            internal long Generation;
            internal bool Frozen;
            internal long EmergencyId = -1L;
            internal int CityCursor;
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

        private static readonly Dictionary<long, KingdomPoolState> States =
            new Dictionary<long, KingdomPoolState>();
        private static long LastMaintenanceWorldDay = -1L;
        private static int KingdomCursor;
        private static bool WorldLoadRestorePending;

        internal static void BeginWorldLoadRestore()
        {
            WorldLoadRestorePending = true;
        }

        internal static void EndWorldLoadRestore()
        {
            WorldLoadRestorePending = false;
        }

        internal static void PrepareForSave()
        {
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                PersistKingdomState(kingdom, state);
                if (kingdom.cities == null) continue;
                for (int i = 0; i < kingdom.cities.Count; i++)
                    ReconcileLedger(kingdom.cities[i], state);
            }
        }

        internal static bool TryWriteSnapshot(string directory,
            out string error)
        {
            error = string.Empty;
            bool worldReady = World.world?.kingdoms != null;
            bool directoryValid = !string.IsNullOrWhiteSpace(directory);
            if (!CityReservePoolPersistenceRules.ShouldWriteSnapshot(
                    worldReady, directoryValid)) return false;

            string temporary = string.Empty;
            try
            {
                PrepareForSave();
                string path = CityReservePoolPersistenceRules.
                    ResolveSnapshotPath(directory);
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
                    foreach (KeyValuePair<long, CityPool> entry in
                             state.Cities)
                    {
                        CityPool pool = entry.Value;
                        if (pool == null) continue;
                        persistedKingdom.cities.Add(new PersistedCity
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
                        });
                    }
                    snapshot.kingdoms.Add(persistedKingdom);
                }

                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                File.WriteAllText(temporary,
                    JsonConvert.SerializeObject(snapshot));
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
                    KingdomPoolState state = State(kingdom);
                    state.Generation = Math.Max(0L, persisted.generation);
                    state.Frozen = persisted.frozen;
                    state.EmergencyId = persisted.emergency_id;
                    PersistKingdomState(kingdom, state);
                    if (persisted.cities == null) continue;
                    for (int c = 0; c < persisted.cities.Count; c++)
                    {
                        PersistedCity source = persisted.cities[c];
                        City city = ResolveCity(source.city_id);
                        if (city?.data == null || city.kingdom != kingdom)
                            continue;
                        CityPool pool = Pool(state, city.id);
                        pool.AuthenticPopulation = Math.Max(0,
                            source.authentic_population);
                        pool.ActiveCitySourcedMilitary = Math.Max(0,
                            source.active_city_military);
                        pool.SyntheticMobilized = Math.Max(0,
                            source.synthetic_mobilized);
                        pool.WarReserveCapacity = Math.Max(0,
                            source.war_reserve_capacity);
                        pool.WarReserveConsumed = Math.Max(0,
                            Math.Min(pool.WarReserveCapacity,
                                source.war_reserve_consumed));
                        pool.WarEmergencyId = source.war_emergency_id;
                        pool.ReconciledWorldDay =
                            source.reconciled_world_day;
                        pool.Ready = source.ready;
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
            if (worldDay == LastMaintenanceWorldDay) return;
            LastMaintenanceWorldDay = worldDay;
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
            if (state.CityCursor < 0 ||
                state.CityCursor >= kingdom.cities.Count)
                state.CityCursor = 0;
            ReconcileLedger(kingdom.cities[state.CityCursor++], state);
        }

        internal static void OnWarStarted(War war)
        {
            if (!ZhuluWarService.ShouldEnrollInAw3Systems(war)) return;
            foreach (Kingdom kingdom in war.getAttackers())
                OpenWarEmergency(kingdom, war.data.id);
            foreach (Kingdom kingdom in war.getDefenders())
                OpenWarEmergency(kingdom, war.data.id);
        }

        internal static void OnKingdomJoinedWar(War war, Kingdom kingdom)
        {
            bool warActive = war?.data != null && !war.hasEnded();
            if (!CityReservePoolRules.ShouldReconcileJoiningKingdom(
                    warActive, IsLivingKingdom(kingdom))) return;
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

        // Reserve manpower is an integer ledger. Actor lifecycle events do
        // not register or remove individual reserve members.
        internal static void OnActorBecameAdult(Actor actor) { }
        internal static void OnActorReturnedToCivilian(Actor actor) { }
        internal static void OnActorInvalidated(Actor actor) { }
        internal static void OnActorCityChanged(Actor actor,
            City previousCity) { }
        internal static void OnActorKingdomChanged(Actor actor,
            Kingdom previousKingdom) { }
        internal static void OnActorEnlisted(Actor actor) { }
        internal static void OnActorProfessionChanged(Actor actor) { }

        internal static void OnConscriptionLawChanged(Kingdom kingdom,
            CourtConscriptionLaw previousLaw, CourtConscriptionLaw nextLaw)
        {
            if (!IsLivingKingdom(kingdom) || previousLaw == nextLaw ||
                kingdom.cities == null) return;
            KingdomPoolState state = State(kingdom);
            for (int i = 0; i < kingdom.cities.Count; i++)
                MarkCityDirty(kingdom.cities[i], state);
        }

        internal static void OnCityKingdomChanged(City city,
            Kingdom previousKingdom, Kingdom currentKingdom)
        {
            if (city?.data == null || previousKingdom == currentKingdom)
                return;
            if (previousKingdom?.data != null &&
                States.TryGetValue(previousKingdom.id,
                    out KingdomPoolState previousState))
            {
                previousState.Cities.Remove(city.id);
                RemoveEmptyState(previousKingdom.id, previousState);
            }
            if (!IsLivingKingdom(currentKingdom) || city.kingdom !=
                currentKingdom) return;
            KingdomPoolState currentState = State(currentKingdom);
            CityPool pool = Pool(currentState, city.id);
            ResetWarReserve(pool);
            ReconcileLedger(city, currentState);
            if (currentState.Frozen)
                OpenCityWarReserve(city, currentState,
                    currentState.EmergencyId);
        }

        internal static void RefreshCapturedCity(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || city.isRekt() ||
                !IsLivingKingdom(kingdom)) return;
            KingdomPoolState state = State(kingdom);
            CityPool pool = Pool(state, city.id);
            ReconcileLedger(city, state);
            if (state.Frozen)
                OpenCityWarReserve(city, state, state.EmergencyId);
        }

        internal static int CountAvailable(Kingdom kingdom)
        {
            if (!IsLivingKingdom(kingdom) || kingdom.cities == null)
                return 0;
            KingdomPoolState state = State(kingdom);
            long total = 0L;
            for (int i = 0; i < kingdom.cities.Count; i++)
            {
                City city = kingdom.cities[i];
                if (!IsControlledCity(city, kingdom)) continue;
                ReconcileLedgerIfNeeded(city, state);
                total += CountAvailable(city, state);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)Math.Max(0L, total);
        }

        internal static int CountAvailable(City city)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsControlledCity(city, kingdom)) return 0;
            KingdomPoolState state = State(kingdom);
            ReconcileLedgerIfNeeded(city, state);
            return CountAvailable(city, state);
        }

        internal static bool IsFrozen(Kingdom kingdom)
        {
            return kingdom?.data != null &&
                   States.TryGetValue(kingdom.id,
                       out KingdomPoolState state) && state.Frozen;
        }

        // Legacy mobilization callers no longer lease civilians. Vanilla owns
        // ordinary enlistment; AW3 only materializes synthetic replenishment.
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
            int available = CountAvailable(preferredCity);
            confirmedExhausted = available <= 0;
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
            if (WorldLoadRestorePending) EndWorldLoadRestore();
            var participantIds = new HashSet<long>();
            ReconcileBeforeWar(first, participantIds);
            ReconcileBeforeWar(second, participantIds);
            return true;
        }

        private static void ReconcileBeforeWar(Kingdom kingdom,
            HashSet<long> participantIds)
        {
            if (!IsLivingKingdom(kingdom) ||
                !participantIds.Add(kingdom.id) || kingdom.cities == null)
                return;
            KingdomPoolState state = State(kingdom);
            for (int i = 0; i < kingdom.cities.Count; i++)
                ReconcileLedger(kingdom.cities[i], state);
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                if (kingdom.cities != null)
                    for (int i = 0; i < kingdom.cities.Count; i++)
                    {
                        City city = kingdom.cities[i];
                        if (city?.data != null) Pool(state, city.id);
                    }
            }
            RebuildSyntheticCounts();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                if (kingdom.cities != null)
                    for (int i = 0; i < kingdom.cities.Count; i++)
                        ReconcileLedger(kingdom.cities[i], state);
                if (CountFormalWars(kingdom) > 0)
                    OpenWarEmergency(kingdom,
                        ResolveFirstWarId(kingdom));
            }
        }

        private static void RebuildSyntheticCounts()
        {
            if (World.world?.units == null) return;
            foreach (Actor actor in World.world.units)
            {
                bool living;
                try
                {
                    living = actor?.data != null &&
                             SyntheticLevyService.IsSynthetic(actor) &&
                             actor.isAlive() && !actor.isRekt();
                }
                catch { living = false; }
                if (!living) continue;
                actor.data.get(LineageKeys.SYNTHETIC_LEVY_LEDGER_RELEASED,
                    out bool released, false);
                if (released) continue;
                actor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                    out long cityId, -1L);
                actor.data.get(
                    LineageKeys.SYNTHETIC_LEVY_SOURCE_KINGDOM_ID,
                    out long kingdomId, -1L);
                City city = ResolveCity(cityId);
                Kingdom kingdom = ResolveKingdom(kingdomId);
                if (city?.data == null || !IsLivingKingdom(kingdom) ||
                    city.kingdom != kingdom) continue;
                CityPool pool = Pool(State(kingdom), city.id);
                pool.SyntheticMobilized = SaturatingAdd(
                    pool.SyntheticMobilized, 1);
            }
        }

        internal static void FinalizeRuntimeRestore(bool snapshotRestored)
        {
            WorldLoadRestorePending = false;
            if (!snapshotRestored)
            {
                RebuildRuntime();
                return;
            }
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                KingdomPoolState state = State(kingdom);
                if (kingdom.cities == null) continue;
                for (int i = 0; i < kingdom.cities.Count; i++)
                {
                    City city = kingdom.cities[i];
                    if (!state.Cities.ContainsKey(city.id))
                        ReconcileLedger(city, state);
                }
            }
        }

        internal static void ClearRuntime()
        {
            States.Clear();
            LastMaintenanceWorldDay = -1L;
            KingdomCursor = 0;
            WorldLoadRestorePending = false;
        }

        internal static int OpenOrReadWarReserve(City city,
            long emergencyId)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsControlledCity(city, kingdom)) return -1;
            KingdomPoolState state = State(kingdom);
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            if (emergencyId < 0L) return -1;
            ReconcileLedgerIfNeeded(city, state);
            OpenCityWarReserve(city, state, emergencyId);
            CityPool pool = Pool(state, city.id);
            return CityManpowerRules.WarReserveAvailable(
                pool.WarReserveCapacity, pool.WarReserveConsumed);
        }

        internal static int TryReserveWarManpower(City city,
            long emergencyId, int requested)
        {
            Kingdom kingdom = city?.kingdom;
            if (!IsControlledCity(city, kingdom)) return 0;
            KingdomPoolState state = State(kingdom);
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            int available = OpenOrReadWarReserve(city, emergencyId);
            if (available <= 0) return 0;
            CityPool pool = Pool(state, city.id);
            int reserved = Math.Min(Math.Max(0, requested), available);
            pool.WarReserveConsumed = SaturatingAdd(
                pool.WarReserveConsumed, reserved);
            return reserved;
        }

        internal static void ReleaseUnmaterializedWarReservation(City city,
            long emergencyId, int count)
        {
            Kingdom kingdom = city?.kingdom;
            if (city?.data == null || count <= 0 ||
                kingdom?.data == null ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state) ||
                !state.Cities.TryGetValue(city.id, out CityPool pool))
                return;
            emergencyId = CityReservePoolRules.ResolveWarEmergencyId(
                state.Frozen, state.EmergencyId, emergencyId);
            if (pool.WarEmergencyId != emergencyId) return;
            pool.WarReserveConsumed = Math.Max(0,
                pool.WarReserveConsumed - count);
        }

        internal static void OnAuthenticMobilized(City city, int count)
        {
            if (count <= 0 || city?.kingdom?.data == null) return;
            MarkCityDirty(city, State(city.kingdom));
        }

        internal static void OnSyntheticMobilized(City city, int count)
        {
            if (city?.data == null || city.kingdom?.data == null ||
                count <= 0) return;
            CityPool pool = Pool(State(city.kingdom), city.id);
            pool.SyntheticMobilized = SaturatingAdd(
                pool.SyntheticMobilized, count);
            pool.ActiveCitySourcedMilitary = SaturatingAdd(
                pool.ActiveCitySourcedMilitary, count);
            pool.Ready = true;
        }

        internal static void OnSyntheticRemoved(City city, int count)
        {
            if (city?.data == null || city.kingdom?.data == null ||
                count <= 0) return;
            CityPool pool = Pool(State(city.kingdom), city.id);
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
            if (!state.Frozen)
            {
                state.Generation = CityReservePoolRules.
                    AdvanceWarEmergencyGeneration(false, state.Generation);
                state.Frozen = true;
            }
            state.EmergencyId = emergencyId;
            PersistKingdomState(kingdom, state);
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
            if (kingdom?.data == null ||
                !CityReservePoolRules.ShouldUnfreeze(
                    CountFormalWars(kingdom))) return;
            KingdomPoolState state = State(kingdom);
            state.Frozen = false;
            state.EmergencyId = -1L;
            foreach (CityPool pool in state.Cities.Values)
                ResetWarReserve(pool);
            PersistKingdomState(kingdom, state);
        }

        private static void ResetWarReserve(CityPool pool)
        {
            if (pool == null) return;
            pool.WarReserveCapacity = 0;
            pool.WarReserveConsumed = 0;
            pool.WarEmergencyId = -1L;
        }

        private static void ReconcileLedgerIfNeeded(City city,
            KingdomPoolState state)
        {
            CityPool pool = Pool(state, city.id);
            if (!pool.Ready ||
                pool.ReconciledWorldDay != CurrentWorldDay())
                ReconcileLedger(city, state);
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
            if (!pool.Ready) return 0;
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

        private static void MarkCityDirty(City city,
            KingdomPoolState state)
        {
            if (city?.data == null || state == null) return;
            Pool(state, city.id).Ready = false;
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
            long result = (long)Math.Max(0, value) +
                          Math.Max(0, addition);
            return (int)Math.Min(int.MaxValue, result);
        }

        private static long ResolveFirstWarId(Kingdom kingdom)
        {
            try
            {
                foreach (War war in kingdom.getWars())
                    if (ZhuluWarService.ShouldEnrollInAw3Systems(war))
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
                    if (ZhuluWarService.ShouldEnrollInAw3Systems(war))
                        count++;
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

        private static void PersistKingdomState(Kingdom kingdom,
            KingdomPoolState state)
        {
            if (kingdom?.data == null || state == null) return;
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_GENERATION,
                state.Generation);
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN,
                state.Frozen);
        }

        private static void RemoveEmptyState(long kingdomId,
            KingdomPoolState state)
        {
            if (!state.Frozen && state.Cities.Count == 0)
                States.Remove(kingdomId);
        }

        private static int SafePopulation(City city)
        {
            try { return Math.Max(0, city.getPopulationPeople()); }
            catch { return 0; }
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
