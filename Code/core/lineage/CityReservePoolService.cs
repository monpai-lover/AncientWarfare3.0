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
            internal long Generation;
            internal bool Frozen;
            internal int CityCursor;
        }

        private static readonly Dictionary<long, KingdomPoolState> States =
            new Dictionary<long, KingdomPoolState>();

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
        }

        internal static void ClearRuntime()
        {
            States.Clear();
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
    }
}
