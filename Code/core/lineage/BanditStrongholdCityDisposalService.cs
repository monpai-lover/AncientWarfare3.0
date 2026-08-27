using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal static class BanditStrongholdCityDisposalService
    {
        private static readonly Dictionary<long, long> Pending =
            new Dictionary<long, long>();

        internal static void Schedule(long pCityId,
            long pExpectedKingdomId)
        {
            if (pCityId <= 0L) return;
            Pending[pCityId] = pExpectedKingdomId;
        }

        internal static bool IsPending(long pCityId)
        {
            return pCityId > 0L && Pending.ContainsKey(pCityId);
        }

        internal static void ProcessAuthorityCycle()
        {
            ProcessAuthorityCycle(int.MaxValue);
        }

        // Disposal can mutate the city container and trigger several native
        // maintenance paths. Process only a small event slice per authority
        // pass to keep large-step frame time bounded.
        internal static void ProcessAuthorityCycle(int pMaximumCities)
        {
            if (Pending.Count <= 0 ||
                CityManagerMutationScope.IsCityUpdateActive ||
                !PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;

            int limit = Math.Max(1, pMaximumCities);
            KeyValuePair<long, long>[] snapshot =
                new KeyValuePair<long, long>[Math.Min(Pending.Count, limit)];
            int index = 0;
            foreach (KeyValuePair<long, long> item in Pending)
            {
                if (index >= snapshot.Length) break;
                snapshot[index++] = item;
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                long cityId = snapshot[i].Key;
                long expectedKingdomId = snapshot[i].Value;
                City city = ResolveCity(cityId);
                if (city?.data == null || city.isRekt())
                {
                    Pending.Remove(cityId);
                    continue;
                }
                int zoneCount;
                try { zoneCount = city.zones?.Count ?? 0; }
                catch { zoneCount = int.MaxValue; }
                bool expectedOwnerMatches;
                try
                {
                    expectedOwnerMatches = expectedKingdomId <= 0L ||
                        city.kingdom?.getID() == expectedKingdomId;
                }
                catch { expectedOwnerMatches = false; }
                if (!PeasantRebelBanditStrongholdRules.
                        CanDisposeStrongholdCity(true, false, zoneCount,
                            expectedOwnerMatches))
                {
                    Pending.Remove(cityId);
                    continue;
                }
                try
                {
                    World.world.cities.removeObject(city);
                    Pending.Remove(cityId);
                }
                catch (System.Exception e)
                {
                    ModClass.LogWarning(
                        "Bandit stronghold city disposal deferred: " +
                        e.Message);
                }
            }
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
