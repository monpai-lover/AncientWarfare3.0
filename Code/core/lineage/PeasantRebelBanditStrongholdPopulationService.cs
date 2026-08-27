using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditStrongholdPopulationService
    {
        private static readonly HashSet<long> Pending = new HashSet<long>();

        internal static void EnqueueStronghold(long pCityId)
        {
            if (pCityId > 0L) Pending.Add(pCityId);
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(int.MaxValue);
        }

        // A stronghold population check can walk every resident in a city.
        // Bound the event queue so a large-step pass cannot turn a backlog
        // into one long main-thread authority phase.
        internal static void ProcessAuthorityCycle(int pMaximumCities)
        {
            if (Pending.Count == 0 ||
                !PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;

            int limit = Math.Max(1, pMaximumCities);
            long[] cityIds = new long[Math.Min(Pending.Count, limit)];
            int snapshotCount = 0;
            foreach (long pendingCityId in Pending)
            {
                if (snapshotCount >= cityIds.Length) break;
                cityIds[snapshotCount++] = pendingCityId;
            }
            for (int i = 0; i < snapshotCount; i++)
            {
                long cityId = cityIds[i];
                Pending.Remove(cityId);
                City city = ResolveCity(cityId);
                Kingdom kingdom = city?.kingdom;
                if (city?.data == null || kingdom?.data == null ||
                    !PeasantRebelBanditStateStore.TryResolveActive(kingdom,
                        out PeasantRebelBanditStrongholdState state) ||
                    state.StrongholdCityId != cityId) continue;

                int living = CountLivingResidents(city);
                if (PeasantRebelBanditStrongholdPopulationRules.
                        ShouldQueueFall(true, living))
                    PeasantRebelBanditStrongholdService.
                        QueuePopulationFall(cityId);
            }
        }

        internal static int CountLivingResidents(City pCity)
        {
            if (pCity?.data == null) return 0;
            int count = 0;
            foreach (Actor actor in pCity.getUnits())
            {
                bool actorExists = actor?.data != null;
                bool alive = false;
                bool rekt = true;
                bool boat = false;
                bool belongs = false;
                if (actorExists)
                {
                    try
                    {
                        alive = actor.isAlive();
                        rekt = actor.isRekt();
                        boat = actor.asset?.is_boat == true;
                        belongs = actor.city == pCity;
                    }
                    catch { }
                }
                if (PeasantRebelBanditStrongholdPopulationRules.
                        IsLivingResident(actorExists, alive, rekt, boat,
                            belongs)) count++;
            }
            return count;
        }

        internal static void Clear()
        {
            Pending.Clear();
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0L || World.world?.cities == null) return null;
            try { return World.world.cities.get(pCityId); }
            catch { return null; }
        }
    }
}
