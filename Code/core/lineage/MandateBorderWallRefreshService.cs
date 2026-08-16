using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderWallRefreshService
    {
        private const int WallWidth = 2;

        internal static bool Activate(Kingdom pMandate)
        {
            if (!CanMutate() || pMandate?.data == null) return false;
            MandateBorderWallState state =
                MandateBorderWallStateStore.Read(pMandate);
            if (!state.Activated)
                state = AdoptPreviousWallState(pMandate, state);
            state.Activated = true;
            state.SourceKingdomId = pMandate.getID();
            return MandateBorderWallStateStore.Write(pMandate, state);
        }

        private static MandateBorderWallState AdoptPreviousWallState(
            Kingdom pMandate, MandateBorderWallState pCurrent)
        {
            if (pCurrent == null || World.world?.kingdoms == null)
                return pCurrent ?? new MandateBorderWallState();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom == pMandate) continue;
                MandateBorderWallState previous =
                    MandateBorderWallStateStore.Read(kingdom);
                if (!previous.Activated) continue;
                pCurrent.Cities = previous.Cities ??
                    new Dictionary<long, MandateBorderCityWallManifest>();
                pCurrent.SourceKingdomId = kingdom.getID();
                previous.Activated = false;
                MandateBorderWallStateStore.Write(kingdom, previous);
                return pCurrent;
            }
            return pCurrent;
        }

        internal static bool IsActivated(Kingdom pMandate)
        {
            return pMandate?.data != null &&
                   MandateBorderWallStateStore.Read(pMandate).Activated;
        }

        internal static int RefreshCitiesNow(Kingdom pMandate,
            IEnumerable<City> pWallCities)
        {
            if (!CanMutate() || pMandate?.data == null) return 0;
            MandateBorderWallState state =
                MandateBorderWallStateStore.Read(pMandate);
            if (!state.Activated) return 0;

            var ids = new HashSet<long>(state.Cities.Keys);
            if (pWallCities != null)
                foreach (City city in pWallCities)
                    if (city?.data != null && !city.isRekt())
                        ids.Add(city.getID());
            int changed = 0;
            foreach (long cityId in ids.OrderBy(id => id))
                changed += RefreshCity(pMandate, cityId);
            return changed;
        }

        internal static void ObserveCityOwnershipChange(City pCity,
            Kingdom pPreviousKingdom, Kingdom pCurrentKingdom)
        {
            if (!CanMutate()) return;
            QueueAffected(pCity, null);
        }

        internal static void ObserveZoneOwnershipChange(TileZone pZone,
            City pPreviousCity, City pCurrentCity)
        {
            if (!CanMutate() || pPreviousCity == pCurrentCity) return;
            QueueAffected(pCurrentCity, pPreviousCity);
        }

        private static void QueueAffected(City pChangedCity,
            City pPreviousCity)
        {
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null || !IsActivated(mandate)) return;
            var neighbourIds = new HashSet<long>();
            CollectNeighbourIds(pChangedCity, neighbourIds);
            CollectNeighbourIds(pPreviousCity, neighbourIds);
            IReadOnlyCollection<long> affected =
                MandateBorderWallRefreshRules.AffectedCityIds(
                    pChangedCity?.getID() ?? -1L,
                    pPreviousCity?.getID() ?? -1L, neighbourIds);
            foreach (long cityId in affected)
                QueueCity(mandate.getID(), cityId);
        }

        private static void QueueCity(long pMandateId, long pCityId)
        {
            if (pMandateId <= 0 || pCityId <= 0) return;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "mandate_border_wall_refresh:" + pMandateId + ":" +
                pCityId, DeferredWorkClass.CriticalRuntime,
                () =>
                {
                    Kingdom mandate = ResolveKingdom(pMandateId);
                    if (mandate?.data == null || !IsActivated(mandate))
                        return;
                    RefreshCity(mandate, pCityId);
                });
        }

        private static int RefreshCity(Kingdom pMandate, long pCityId)
        {
            MandateBorderWallState state =
                MandateBorderWallStateStore.Read(pMandate);
            if (!state.Activated) return 0;
            int changed = 0;
            if (state.Cities.TryGetValue(pCityId,
                    out MandateBorderCityWallManifest previous))
            {
                changed += RestoreManifest(previous);
                state.Cities.Remove(pCityId);
            }

            City city = ResolveCity(pCityId);
            bool eligible = MandateBorderDefenseService.
                IsEligibleWallCity(city, pMandate);
            if (MandateBorderWallRefreshRules.ShouldRefresh(
                    state.Activated, eligible))
            {
                TopTileType wall = MandateBorderDefenseService.
                    ResolveBorderWallType();
                HashSet<CultiwayWallPoint> reserved =
                    CollectWatchTowerFootprint(city);
                if (wall != null && CultiwayStyleCityWallService.
                        TryPlanFrontier(city, WallWidth,
                            kingdom => MandateBorderDefenseService.
                                IsFortificationTarget(pMandate, kingdom),
                            reserved, pCarveRoadPassages: false,
                            out IReadOnlyList<CultiwayWallPoint> planned))
                {
                    var manifest = new MandateBorderCityWallManifest
                    {
                        CityId = pCityId,
                        WallTypeId = wall.id
                    };
                    foreach (CultiwayWallPoint point in planned)
                    {
                        WorldTile tile = World.world?.GetTile(
                            point.X, point.Y);
                        if (tile?.zone?.city != city) continue;
                        manifest.Points.Add(new MandateBorderWallPointState
                        {
                            X = point.X,
                            Y = point.Y,
                            OriginalTopTypeId = tile.top_type?.id ?? ""
                        });
                        if (tile.top_type == wall) continue;
                        tile.setTopTileType(wall);
                        changed++;
                    }
                    if (manifest.Points.Count > 0)
                        state.Cities[pCityId] = manifest;
                }
            }
            MandateBorderWallStateStore.Write(pMandate, state);
            return changed;
        }

        private static int RestoreManifest(
            MandateBorderCityWallManifest pManifest)
        {
            if (pManifest?.Points == null) return 0;
            int changed = 0;
            foreach (MandateBorderWallPointState point in pManifest.Points)
            {
                if (point == null) continue;
                WorldTile tile = World.world?.GetTile(point.X, point.Y);
                if (tile == null ||
                    !MandateBorderWallRefreshRules.ShouldRestore(
                        tile.top_type?.id, pManifest.WallTypeId)) continue;
                TopTileType original = string.IsNullOrWhiteSpace(
                        point.OriginalTopTypeId)
                    ? null
                    : AssetManager.top_tiles.get(
                        point.OriginalTopTypeId);
                if (tile.top_type == original) continue;
                tile.setTopTileType(original);
                changed++;
            }
            return changed;
        }

        private static HashSet<CultiwayWallPoint>
            CollectWatchTowerFootprint(City pCity)
        {
            var result = new HashSet<CultiwayWallPoint>();
            if (pCity?.buildings == null) return result;
            foreach (Building building in pCity.buildings)
            {
                if (building?.asset == null ||
                    !(building.asset.type == "type_watch_tower")) continue;
                bool added = false;
                if (building.tiles != null)
                {
                    foreach (WorldTile tile in building.tiles)
                    {
                        if (tile == null) continue;
                        result.Add(new CultiwayWallPoint(tile.x, tile.y));
                        added = true;
                    }
                }
                if (!added && building.current_tile != null)
                    result.Add(new CultiwayWallPoint(
                        building.current_tile.x, building.current_tile.y));
            }
            return result;
        }

        private static void CollectNeighbourIds(City pCity,
            HashSet<long> pIds)
        {
            if (pCity?.data == null || pIds == null) return;
            try
            {
                pCity.recalculateNeighbourZones();
                pCity.recalculateNeighbourCities();
                if (pCity.neighbours_cities == null) return;
                foreach (City neighbour in pCity.neighbours_cities)
                    if (neighbour?.data != null && !neighbour.isRekt())
                        pIds.Add(neighbour.getID());
            }
            catch { }
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0 || World.world?.cities == null) return null;
            try { return World.world.cities.get(pCityId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0 || World.world?.kingdoms == null)
                return null;
            try { return World.world.kingdoms.get(pKingdomId); }
            catch { return null; }
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
