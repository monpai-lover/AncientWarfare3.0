using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class CityReservePoolService
    {
        private sealed class CityPool
        {
            internal readonly SortedSet<long> ActorIds =
                new SortedSet<long>();
        }

        private sealed class KingdomPoolState
        {
            internal readonly Dictionary<long, CityPool> Cities =
                new Dictionary<long, CityPool>();
            internal readonly Dictionary<long, int> ActorCursors =
                new Dictionary<long, int>();
            internal readonly Dictionary<long, long> ValidationAfterActorIds =
                new Dictionary<long, long>();
            internal long Generation;
            internal bool Frozen;
            internal int CityCursor;
        }

        private static readonly Dictionary<long, KingdomPoolState> States =
            new Dictionary<long, KingdomPoolState>();
        private static long LastMaintenanceWorldDay = -1L;
        private static int KingdomCursor;

        internal static void ProcessAuthorityCycle()
        {
            long worldDay = CurrentWorldDay();
            bool worldDayChanged = worldDay != LastMaintenanceWorldDay;
            if (!worldDayChanged) return;
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
            if (!CityReservePoolRules.CanMaintain(state.Frozen,
                    worldDayChanged)) return;
            bool preparation = WarNoticeService.HasActiveNotice(kingdom) &&
                               !state.Frozen;
            int cityBudget = CityReservePoolRules.CityBudget(preparation);
            int actorBudget = CityReservePoolRules.ActorBudget(preparation);
            for (int i = 0; i < cityBudget && kingdom.cities.Count > 0; i++)
            {
                if (state.CityCursor < 0 ||
                    state.CityCursor >= kingdom.cities.Count)
                    state.CityCursor = 0;
                City city = kingdom.cities[state.CityCursor++];
                MaintainCity(kingdom, city, state, actorBudget);
            }
        }

        internal static void OnWarStarted(War war)
        {
            if (war?.data == null || war.hasEnded()) return;
            foreach (Kingdom kingdom in war.getAttackers()) Freeze(kingdom);
            foreach (Kingdom kingdom in war.getDefenders()) Freeze(kingdom);
        }

        internal static void OnKingdomJoinedWar(War war, Kingdom kingdom)
        {
            if (war?.data == null || war.hasEnded()) return;
            Freeze(kingdom);
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
            int capacity = CityReservePoolRules.Capacity(
                SafePopulation(city), EffectiveWarriorSlots(city, kingdom));
            if (!CityReservePoolRules.CanEnroll(actor.isAlive(),
                    actor.isAdult(), actor.city == city,
                    TemporaryLevyService.CanRegisterReserve(kingdom, city,
                        actor), state.Frozen, pool.ActorIds.Count, capacity))
                return;

            actor.data.set(LineageKeys.CITY_RESERVE_MEMBER, true);
            actor.data.set(LineageKeys.CITY_RESERVE_CITY_ID, city.id);
            actor.data.set(LineageKeys.CITY_RESERVE_KINGDOM_ID, kingdom.id);
            actor.data.set(LineageKeys.CITY_RESERVE_GENERATION,
                state.Generation);
            pool.ActorIds.Add(actor.data.id);
        }

        internal static void OnActorInvalidated(Actor actor)
        {
            RemoveRecordedMembership(actor);
        }

        internal static void OnActorCityChanged(Actor actor,
            City previousCity)
        {
            if (actor?.data == null || actor.city == previousCity) return;
            RemoveRecordedMembership(actor);
        }

        internal static void OnActorKingdomChanged(Actor actor,
            Kingdom previousKingdom)
        {
            if (actor?.data == null || actor.kingdom == previousKingdom)
                return;
            RemoveRecordedMembership(actor);
        }

        internal static void OnActorEnlisted(Actor actor)
        {
            RemoveRecordedMembership(actor);
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
            RemoveEmptyState(previousKingdom.id, state);
        }

        internal static int CountAvailable(Kingdom kingdom)
        {
            if (kingdom?.data == null ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state)) return 0;
            long count = 0L;
            foreach (CityPool pool in state.Cities.Values)
            {
                count += pool.ActorIds.Count;
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
            return pool.ActorIds.Count;
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
            confirmedExhausted = false;
            int requestedCount = Math.Max(0, requested);
            if (requestedCount <= 0 || destination == null ||
                !IsLivingKingdom(kingdom) ||
                !States.TryGetValue(kingdom.id,
                    out KingdomPoolState state) || !state.Frozen)
                return 0;
            if (targetArmy?.data != null &&
                AWArmyService.GetIntendedKingdom(targetArmy) != kingdom)
                return 0;

            WorldTile preferredTile = SafeCityTile(preferredCity);
            var donors = new List<CityReserveDonorFacts>(
                state.Cities.Count);
            foreach (long cityId in state.Cities.Keys)
            {
                City city = ResolveCity(cityId);
                donors.Add(new CityReserveDonorFacts(cityId,
                    preferredCity?.data != null &&
                    cityId == preferredCity.id,
                    DistanceSquared(SafeCityTile(city), preferredTile)));
            }
            IReadOnlyList<long> orderedCityIds =
                CityReservePoolRules.OrderDonorCityIds(donors);
            int inspectionBudget = requestedCount;
            int inspected = 0;
            int added = 0;
            bool allIndexedCitiesChecked = true;
            int remainingUsableActors = 0;

            for (int cityIndex = 0;
                 cityIndex < orderedCityIds.Count; cityIndex++)
            {
                long cityId = orderedCityIds[cityIndex];
                if (!state.Cities.TryGetValue(cityId, out CityPool pool))
                    continue;
                City city = ResolveCity(cityId);
                bool realmControlled = city?.data != null &&
                    !city.isRekt() && city.kingdom == kingdom &&
                    OccupiedCitySupplyService.CanProvideToRealm(
                        city, kingdom);
                bool canConsume = CityReservePoolRules.CanConsumeFromCity(
                    state.Frozen, realmControlled, SafePopulation(city));
                if (!canConsume) continue;

                while (added < requestedCount &&
                       inspected < inspectionBudget &&
                       CityReservePoolRules.TryTakeNextActorId(
                           pool.ActorIds, out long actorId))
                {
                    inspected++;
                    Actor actor = ResolveActor(actorId);
                    if (!IsValidMember(actor, kingdom, city,
                            state.Generation))
                    {
                        if (actor?.data != null) ClearFields(actor);
                        continue;
                    }
                    ClearFields(actor);
                    destination.Add(actor);
                    added++;
                }

                if (pool.ActorIds.Count == 0)
                {
                    state.Cities.Remove(cityId);
                    state.ActorCursors.Remove(cityId);
                    state.ValidationAfterActorIds.Remove(cityId);
                    continue;
                }
                remainingUsableActors = remainingUsableActors >=
                    int.MaxValue - pool.ActorIds.Count
                    ? int.MaxValue
                    : remainingUsableActors + pool.ActorIds.Count;
                allIndexedCitiesChecked = false;
            }

            confirmedExhausted = CityReservePoolRules.CanConfirmExhausted(
                state.Frozen, allIndexedCitiesChecked,
                remainingUsableActors);
            return added;
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.units == null) return;
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
                if (city?.data == null || kingdom?.data == null ||
                    actor.city != city || actor.kingdom != kingdom ||
                    city.kingdom != kingdom)
                {
                    ClearFields(actor);
                    continue;
                }
                KingdomPoolState state = State(kingdom);
                if (generation != state.Generation)
                {
                    ClearFields(actor);
                    continue;
                }
                Pool(state, city.id).ActorIds.Add(actor.data.id);
            }

            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsLivingKingdom(kingdom)) continue;
                int activeWarCount = CountFormalWars(kingdom);
                kingdom.data.get(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN,
                    out bool persistedFrozen, false);
                if (activeWarCount > 0)
                {
                    Freeze(kingdom);
                    continue;
                }
                if (persistedFrozen) ReevaluateFreeze(kingdom);
            }
        }

        internal static void ClearRuntime()
        {
            States.Clear();
            LastMaintenanceWorldDay = -1L;
            KingdomCursor = 0;
        }

        private static void MaintainCity(Kingdom kingdom, City city,
            KingdomPoolState state, int actorBudget)
        {
            if (city?.data == null || city.isRekt() ||
                city.kingdom != kingdom || actorBudget <= 0) return;
            CityPool pool = Pool(state, city.id);
            int capacity = CityReservePoolRules.Capacity(
                SafePopulation(city), EffectiveWarriorSlots(city, kingdom));
            int remainingBudget = ValidateMembers(kingdom, city, state, pool,
                actorBudget);
            if (pool.ActorIds.Count >= capacity || remainingBudget <= 0 ||
                city.units == null || city.units.Count == 0) return;

            state.ActorCursors.TryGetValue(city.id, out int cursor);
            int residentCount = city.units.Count;
            if (cursor < 0 || cursor >= residentCount) cursor = 0;
            int inspected = 0;
            while (inspected < remainingBudget && inspected < residentCount &&
                   pool.ActorIds.Count < capacity)
            {
                Actor actor = city.units[cursor];
                cursor++;
                if (cursor >= residentCount) cursor = 0;
                inspected++;
                if (actor?.data == null ||
                    pool.ActorIds.Contains(actor.data.id)) continue;
                OnActorBecameAdult(actor);
            }
            state.ActorCursors[city.id] = cursor;
        }

        private static int ValidateMembers(Kingdom kingdom, City city,
            KingdomPoolState state, CityPool pool, int actorBudget)
        {
            if (pool.ActorIds.Count == 0) return actorBudget;
            state.ValidationAfterActorIds.TryGetValue(city.id,
                out long afterActorId);
            var inspectedIds = new List<long>(actorBudget);
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

        private static void Freeze(Kingdom kingdom)
        {
            if (!IsLivingKingdom(kingdom)) return;
            KingdomPoolState state = State(kingdom);
            if (state.Frozen) return;
            state.Generation = state.Generation >= long.MaxValue
                ? long.MaxValue
                : state.Generation + 1L;
            state.Frozen = true;
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
        }

        private static void ReevaluateFreeze(Kingdom kingdom)
        {
            if (kingdom?.data == null) return;
            int activeWarCount = CountFormalWars(kingdom);
            if (!CityReservePoolRules.ShouldUnfreeze(activeWarCount)) return;
            KingdomPoolState state = State(kingdom);
            state.Frozen = false;
            kingdom.data.set(LineageKeys.CITY_RESERVE_KINGDOM_FROZEN, false);
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

        private static void RemoveRecordedMembership(Actor actor)
        {
            if (actor?.data == null) return;
            actor.data.get(LineageKeys.CITY_RESERVE_MEMBER,
                out bool member, false);
            actor.data.get(LineageKeys.CITY_RESERVE_CITY_ID,
                out long cityId, -1L);
            actor.data.get(LineageKeys.CITY_RESERVE_KINGDOM_ID,
                out long kingdomId, -1L);
            if (member && kingdomId >= 0L && cityId >= 0L &&
                States.TryGetValue(kingdomId,
                    out KingdomPoolState state) &&
                state.Cities.TryGetValue(cityId, out CityPool pool))
            {
                pool.ActorIds.Remove(actor.data.id);
                if (pool.ActorIds.Count == 0) state.Cities.Remove(cityId);
                RemoveEmptyState(kingdomId, state);
            }
            ClearFields(actor);
        }

        private static void RemoveEmptyState(long kingdomId,
            KingdomPoolState state)
        {
            if (!state.Frozen && state.Cities.Count == 0)
                States.Remove(kingdomId);
        }

        private static void ClearFields(Actor actor)
        {
            actor.data.set(LineageKeys.CITY_RESERVE_MEMBER, false);
            actor.data.set(LineageKeys.CITY_RESERVE_CITY_ID, -1L);
            actor.data.set(LineageKeys.CITY_RESERVE_KINGDOM_ID, -1L);
            actor.data.set(LineageKeys.CITY_RESERVE_GENERATION, -1L);
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

        private static int EffectiveWarriorSlots(City city, Kingdom kingdom)
        {
            int slots = 0;
            try { slots = city.status?.warrior_slots ?? 0; }
            catch { }
            return Math.Max(0, MandateMilitaryPhaseService.
                EffectiveWarriorSlots(kingdom, slots));
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
