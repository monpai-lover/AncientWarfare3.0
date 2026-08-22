using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class DeJureNewCityAssignmentService
    {
        private const string RetryPrefix = "de_jure_new_city:";
        private static readonly HashSet<long> RetryIds = new HashSet<long>();
        private static bool _worldRepairCompleted;

        internal static void OnCityFounded(City pCity)
        {
            if (TryAssign(pCity, allowRetry: true)) return;
        }

        internal static void ClearRuntime()
        {
            RetryIds.Clear();
            _worldRepairCompleted = false;
        }

        // Existing saves may contain cities created before the automatic
        // assignment hook was installed, or while the city had no kingdom
        // yet. Repair those gaps once after the world is fully available.
        internal static void RepairUnassignedCities()
        {
            if (_worldRepairCompleted || World.world?.cities == null ||
                World.world.cities.Count == 0 || !Config.game_loaded ||
                SmoothLoader.isLoading()) return;
            _worldRepairCompleted = true;
            try
            {
                foreach (City city in World.world.cities.ToArray())
                {
                    if (city?.data == null || city.isRekt() ||
                        city.kingdom?.data == null || city.kingdom.isRekt() ||
                        city.kingdom.isNeutral() ||
                        PeasantRebelBanditStrongholdService.IsStrongholdCity(city))
                        continue;
                    if (DeJureRegionStore.TryGetForCity(city.data.id, out _))
                        continue;
                    TryAssign(city, allowRetry: true);
                }
            }
            catch (Exception error)
            {
                _worldRepairCompleted = false;
                ModClass.LogWarning("De jure unassigned city repair failed: " +
                    error.Message);
            }
        }

        private static bool TryAssign(City pCity, bool allowRetry)
        {
            if (pCity?.data == null || pCity.data.id < 0L || pCity.isRekt())
                return false;
            if (PeasantRebelBanditStrongholdService.IsStrongholdCity(pCity))
                return false;
            Kingdom kingdom = pCity.kingdom;
            if (kingdom?.data == null)
            {
                QueueRetry(pCity, allowRetry);
                return false;
            }
            if (kingdom.isRekt() || kingdom.isNeutral())
                return false;
            // A retired/unassigned city is an intentional empty-map state,
            // not a missed automatic assignment. It can be assigned again
            // only through the explicit create/assign power.
            if (DeJureRegionStore.HasExplicitDeJureRemoval(pCity.data.id))
                return false;
            if (kingdom.capital == pCity)
            {
                DeJureRegionStore.EnsureKingdomCapitalSeat(kingdom);
                return true;
            }
            if (DeJureRegionStore.TryGetForCity(pCity.data.id, out _)) return true;

            if (!PrepareNeighbours(pCity))
            {
                QueueRetry(pCity, allowRetry);
                return false;
            }

            long targetId = SelectRegion(pCity, kingdom);
            if (targetId < 0L)
            {
                if (!DeJureRegionStore.CreateState(pCity,
                    "city_created_auto_create", out _, out string createError))
                {
                    if (createError == "invalid_city")
                    {
                        QueueRetry(pCity, allowRetry);
                        return false;
                    }
                    QueueRetry(pCity, allowRetry);
                    return false;
                }
                RetryIds.Remove(pCity.data.id);
                HierarchicalVassalMapModeService.MarkHierarchyDirty(kingdom);
                HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
                return true;
            }
            if (!DeJureRegionStore.AssignCityAutomatically(targetId, pCity,
                    "city_created_auto_assign", out string error))
            {
                if (error == "already_assigned") return false;
                if (error == "invalid_city")
                {
                    QueueRetry(pCity, allowRetry);
                    return false;
                }
                QueueRetry(pCity, allowRetry);
                return false;
            }

            RetryIds.Remove(pCity.data.id);
            HierarchicalVassalMapModeService.MarkHierarchyDirty(kingdom);
            HierarchicalVassalMapModeService.RefreshAfterDeJureMutation();
            return true;
        }

        private static bool PrepareNeighbours(City pCity)
        {
            try
            {
                pCity.recalculateNeighbourZones();
                pCity.recalculateNeighbourCities();
                return pCity.getTile() != null;
            }
            catch { return false; }
        }

        private static long SelectRegion(City pCity, Kingdom pKingdom)
        {
            WorldTile cityTile = pCity.getTile();
            if (cityTile == null) return -1L;
            var adjacent = new HashSet<long>((pCity.neighbours_cities ??
                new HashSet<City>()).Where(p => p?.data != null &&
                !p.isRekt() && p.kingdom == pKingdom &&
                DeJureRegionStore.IsEligibleCityId(p.data.id)).Select(
                    p => p.data.id));
            var facts = new List<DeJureNewCityRegionCandidate>();
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                var members = (region.MemberCityIds ?? new List<long>())
                    .Select(id => World.world?.cities?.get(id))
                    .Where(city => city?.data != null && !city.isRekt() &&
                        city.kingdom == pKingdom &&
                        DeJureRegionStore.IsEligibleCityId(city.data.id))
                    .ToList();
                if (members.Count == 0) continue;
                int adjacentCount = members.Count(city =>
                    adjacent.Contains(city.data.id));
                long nearest = members.Select(city => Distance(cityTile,
                    city.getTile())).DefaultIfEmpty(long.MaxValue).Min();
                City seat = members.FirstOrDefault(city =>
                    city.data.id == region.SeatCityId);
                long seatDistance = Distance(cityTile, seat?.getTile());
                facts.Add(new DeJureNewCityRegionCandidate(region.RegionId,
                    adjacentCount > 0, adjacentCount, nearest,
                    seatDistance, true));
            }
            return DeJureNewCityAssignmentRules.Select(facts);
        }

        private static long Distance(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst?.pos == null || pSecond?.pos == null)
                return long.MaxValue;
            try
            {
                double dx = pFirst.pos.x - pSecond.pos.x;
                double dy = pFirst.pos.y - pSecond.pos.y;
                double value = dx * dx + dy * dy;
                return value >= long.MaxValue ? long.MaxValue : (long)value;
            }
            catch { return long.MaxValue; }
        }

        private static void QueueRetry(City pCity, bool allowRetry)
        {
            if (!allowRetry || pCity?.data == null ||
                !RetryIds.Add(pCity.data.id)) return;
            long cityId = pCity.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                RetryPrefix + cityId, DeferredWorkClass.Runtime,
                () =>
                {
                    RetryIds.Remove(cityId);
                    // The first retry can still race city/zone initialization;
                    // keep the same bounded coalesced retry contract until
                    // the native city graph is ready.
                    TryAssign(World.world?.cities?.get(cityId), true);
                });
        }
    }
}
