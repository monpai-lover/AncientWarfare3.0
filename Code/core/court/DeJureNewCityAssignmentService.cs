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

        internal static void OnCityFounded(City pCity)
        {
            if (TryAssign(pCity, allowRetry: true)) return;
        }

        internal static void ClearRuntime()
        {
            RetryIds.Clear();
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
                !p.isRekt() && DeJureRegionStore.IsEligibleCityId(
                    p.data.id)).Select(p => p.data.id));
            var facts = new List<DeJureNewCityRegionCandidate>();
            foreach (DeJureRegion region in DeJureRegionStore.ActiveRegions())
            {
                City seat = World.world?.cities?.get(region.SeatCityId);
                if (seat?.data == null || seat.isRekt() ||
                    !DeJureRegionStore.IsEligibleCityId(seat.data.id)) continue;
                bool adjacentSeat = adjacent.Contains(seat.data.id);
                long seatDistance = Distance(cityTile, seat?.getTile());
                facts.Add(new DeJureNewCityRegionCandidate(region.RegionId,
                    adjacentSeat, adjacentSeat ? 1 : 0, seatDistance,
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
                    TryAssign(World.world?.cities?.get(cityId), false);
                });
        }
    }
}
