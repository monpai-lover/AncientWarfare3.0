using System;
using System.Collections.Generic;
using System.Reflection;
using AncientWarfare3.core.performance;
using life.taxi;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsTransportProductionService
    {
        internal const int MaximumDedicatedTransportsPerKingdom = 3;
        private const double DemandLifetimeSeconds = 30d;
        private const double RetryCooldownSeconds = 3d;
        private const string TransportBoatType = "boat_type_transport";
        private static readonly FieldInfo CityBoatsField = typeof(City).
            GetField("_boats", BindingFlags.Instance |
                               BindingFlags.NonPublic);

        private sealed class ProductionDemand
        {
            internal long KingdomId;
            internal City DockCity;
            internal Docks Dock;
            internal TaxiRequest Request;
            internal double ExpiresAt;
            internal double NextBuildAttemptAt;
            internal bool AttemptLogged;
            internal bool FailureLogged;
        }

        private sealed class ProductionDock
        {
            internal City City;
            internal Docks Dock;
            // This sea tile was proven to connect both coasts. The temporary
            // boat must spawn here rather than on an arbitrary dock ocean tile.
            internal WorldTile OceanTile;
        }

        private static readonly Dictionary<long, ProductionDemand>
            DemandsByDockCity = new Dictionary<long, ProductionDemand>();
        private static readonly Dictionary<long, double>
            NextRequestAtByKingdom = new Dictionary<long, double>();
        private static readonly HashSet<string> LoggedOutcomes =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<long, Actor> TemporaryBoatIds =
            new Dictionary<long, Actor>();
        private static readonly HashSet<long> PendingTemporaryBoatRemovalIds =
            new HashSet<long>();
        private static readonly HashSet<long> TemporaryBoatBuildCityIds =
            new HashSet<long>();
        private static readonly Dictionary<long, WorldTile>
            TemporaryBoatBuildOceanTilesByCity =
                new Dictionary<long, WorldTile>();

        internal static int ActiveDemandCount
        {
            get
            {
                Prune(CurrentRealtime());
                return DemandsByDockCity.Count;
            }
        }

        internal static void Request(Kingdom pKingdom,
            TaxiRequest pRequest)
        {
            if (pKingdom?.data == null || pRequest == null ||
                !IsPendingFor(pRequest, pKingdom)) return;
            WorldTile pickup;
            WorldTile target;
            try
            {
                pickup = pRequest.getTileStart();
                target = pRequest.getTileTarget();
            }
            catch { return; }
            if (pickup?.data == null || target?.data == null) return;

            double now = CurrentRealtime();
            Prune(now);
            if (TryRefreshExisting(pRequest, now)) return;

            long kingdomId = pKingdom.id;
            if (NextRequestAtByKingdom.TryGetValue(kingdomId,
                    out double nextRequestAt) && now < nextRequestAt)
            {
                LogOutcomeOnce(kingdomId, "cooldown", null, pRequest);
                return;
            }

            int fleet = CountDedicatedTransports(pKingdom);
            int pending = CountPendingRequests(pKingdom);
            if (fleet >= MaximumDedicatedTransportsPerKingdom ||
                pending <= fleet)
            {
                NextRequestAtByKingdom[kingdomId] = now +
                    RetryCooldownSeconds;
                LogOutcomeOnce(kingdomId, "fleet_satisfied", null,
                    pRequest, " fleet=" + fleet + " pending=" + pending);
                return;
            }

            ProductionDock routeDock = FindRouteDock(pKingdom, pickup,
                target);
            City dockCity = routeDock?.City;
            if (dockCity?.data == null || routeDock.Dock == null)
            {
                NextRequestAtByKingdom[kingdomId] = now +
                    RetryCooldownSeconds;
                LogOutcomeOnce(kingdomId, "no_route_dock", null,
                    pRequest);
                return;
            }
            var demand = new ProductionDemand
            {
                KingdomId = kingdomId,
                DockCity = dockCity,
                Dock = routeDock.Dock,
                Request = pRequest,
                ExpiresAt = now + DemandLifetimeSeconds
            };
            DemandsByDockCity[dockCity.id] = demand;
            LogOutcomeOnce(kingdomId, "demand_created", dockCity,
                pRequest);
            NextRequestAtByKingdom[kingdomId] = now +
                RetryCooldownSeconds;
            if (TryBuild(demand, now))
                DemandsByDockCity.Remove(dockCity.id);
        }

        internal static bool CanProvisionRoute(Kingdom pKingdom,
            WorldTile pPickup, WorldTile pTarget)
        {
            if (pKingdom?.data == null || pPickup?.data == null ||
                pTarget?.data == null) return false;
            return FindRouteDock(pKingdom, pPickup, pTarget,
                pIgnoreDemand: true, pAllowFullDock: true) != null;
        }

        // RTS voyages own a new boat for their entire lifetime. The normal
        // production queue remains available to non-RTS travel systems.
        internal static bool TryProvisionAndBind(Kingdom pKingdom,
            TaxiRequest pRequest, out Actor pBoatActor)
        {
            pBoatActor = null;
            if (!IsPendingFor(pRequest, pKingdom)) return false;
            WorldTile pickup;
            WorldTile target;
            try
            {
                pickup = pRequest.getTileStart();
                target = pRequest.getTileTarget();
            }
            catch { return false; }
            if (pickup?.data == null || target?.data == null) return false;

            ProductionDock routeDock = FindRouteDock(pKingdom, pickup,
                target, pIgnoreDemand: true, pAllowFullDock: true);
            if (routeDock?.City?.data == null || routeDock.Dock == null)
            {
                LogOutcomeOnce(pKingdom?.id ?? -1L,
                    "temporary_no_route_dock", null, pRequest);
                return false;
            }
            Actor boatActor = null;
            try
            {
                TemporaryBoatBuildCityIds.Add(routeDock.City.id);
                TemporaryBoatBuildOceanTilesByCity[routeDock.City.id] =
                    routeDock.OceanTile;
                try
                {
                    boatActor = routeDock.Dock.buildBoatFromHere(
                        routeDock.City);
                }
                finally
                {
                    TemporaryBoatBuildCityIds.Remove(routeDock.City.id);
                    TemporaryBoatBuildOceanTilesByCity.Remove(
                        routeDock.City.id);
                }
                if (boatActor?.data == null)
                {
                    LogOutcomeOnce(pKingdom.id,
                        "temporary_build_returned_null", routeDock.City,
                        pRequest);
                    return false;
                }
                boatActor.joinKingdom(pKingdom);
                boatActor.joinCity(routeDock.City);
                Boat boat = boatActor.getSimpleComponent<Boat>();
                if (boat == null)
                {
                    LogOutcomeOnce(pKingdom.id,
                        "temporary_boat_component_missing",
                        routeDock.City, pRequest,
                        " boat=" + boatActor.data.id);
                    ActionLibrary.removeUnit(boatActor);
                    return false;
                }
                TemporaryBoatIds[boatActor.data.id] = boatActor;
                boat.taxi_request = pRequest;
                pRequest.assign(boat);
                boatActor.setTask("boat_transport_go_load", pClean: true,
                    pCleanJob: false, pForceAction: true);
                pBoatActor = boatActor;
                LogOutcomeOnce(pKingdom.id, "temporary_boat_bound",
                    routeDock.City, pRequest,
                    " boat=" + boatActor.data.id);
                return true;
            }
            catch (Exception error)
            {
                LogOutcomeOnce(pKingdom?.id ?? -1L,
                    "temporary_build_exception", routeDock?.City,
                    pRequest, " error=" + error.GetType().Name);
                if (boatActor?.data != null)
                    DestroyTemporaryTransportBoat(boatActor.data.id);
                return false;
            }
        }

        internal static bool IsTemporaryTransportBoat(long pBoatId)
        {
            return pBoatId >= 0L && TemporaryBoatIds.ContainsKey(pBoatId);
        }

        internal static void DestroyTemporaryTransportBoat(long pBoatId)
        {
            if (!TemporaryBoatIds.TryGetValue(pBoatId,
                    out Actor boatActor)) return;
            Boat boat = null;
            try { boat = boatActor?.getSimpleComponent<Boat>(); }
            catch { }
            try
            {
                // A live passenger must finish disembarking before the boat
                // can be removed without stranding the army at sea.
                if (boat?.hasPassengers() == true)
                {
                    PendingTemporaryBoatRemovalIds.Add(pBoatId);
                    return;
                }
                if (boat?.taxi_request != null)
                {
                    TaxiManager.cancelRequest(boat.taxi_request);
                    boat.taxi_request = null;
                }
                ActionLibrary.removeUnit(boatActor);
            }
            catch { }
            finally
            {
                if (boat?.hasPassengers() != true)
                {
                    TemporaryBoatIds.Remove(pBoatId);
                    PendingTemporaryBoatRemovalIds.Remove(pBoatId);
                }
            }
        }

        internal static void ProcessTemporaryBoatDisposals()
        {
            if (PendingTemporaryBoatRemovalIds.Count == 0) return;
            var pending = new List<long>(PendingTemporaryBoatRemovalIds);
            for (int i = 0; i < pending.Count; i++)
                DestroyTemporaryTransportBoat(pending[i]);
        }

        internal static bool HasDemand(City pCity)
        {
            if (pCity?.data == null || pCity.kingdom?.data == null)
                return false;
            if (TemporaryBoatBuildCityIds.Contains(pCity.id)) return true;
            double now = CurrentRealtime();
            Prune(now);
            return DemandsByDockCity.TryGetValue(pCity.id,
                       out ProductionDemand demand) &&
                   demand.KingdomId == pCity.kingdom.id &&
                   demand.ExpiresAt > now &&
                   IsPendingFor(demand.Request, pCity.kingdom);
        }

        internal static bool IsTemporaryBoatBuild(City pCity)
        {
            return pCity?.data != null &&
                   TemporaryBoatBuildCityIds.Contains(pCity.id);
        }

        internal static bool TryBuildTemporaryTransportAtDock(
            Docks pDock, City pCity, out Actor pBoatActor)
        {
            pBoatActor = null;
            if (pDock == null || pCity?.data == null ||
                !IsTemporaryBoatBuild(pCity)) return false;
            Actor created = null;
            try
            {
                string transportId = pCity.getActorAsset()?
                    .architecture_asset?.actor_asset_id_transport;
                if (string.IsNullOrEmpty(transportId)) return false;
                ActorAsset transport = AssetManager.actor_library.get(
                    transportId);
                if (transport == null) return false;
                if (pDock.tiles_ocean == null ||
                    pDock.tiles_ocean.Count == 0)
                    pDock.recalculateOceanTiles();
                if (pDock.tiles_ocean == null ||
                    pDock.tiles_ocean.Count == 0) return false;

                WorldTile spawnTile = null;
                TemporaryBoatBuildOceanTilesByCity.TryGetValue(pCity.id,
                    out spawnTile);
                if (spawnTile == null || !pDock.tiles_ocean.Contains(
                        spawnTile))
                    spawnTile = pDock.tiles_ocean.GetRandom();
                if (spawnTile?.region?.island?.goodForDocks() != true)
                {
                    spawnTile = null;
                    foreach (WorldTile candidate in pDock.tiles_ocean)
                    {
                        if (candidate?.region?.island?.goodForDocks() !=
                            true) continue;
                        spawnTile = candidate;
                        break;
                    }
                }
                if (spawnTile?.data == null) return false;
                created = World.world.units.createNewUnit(transport.id,
                    spawnTile);
                if (created?.data == null) return false;
                pDock.addBoatToDock(created);
                pBoatActor = created;
                return true;
            }
            catch
            {
                if (created?.data != null)
                {
                    try { ActionLibrary.removeUnit(created); }
                    catch { }
                }
                pBoatActor = null;
                return false;
            }
        }

        internal static void OnAssigned(TaxiRequest pRequest)
        {
            RemoveForRequest(pRequest);
        }

        internal static void Cancel(TaxiRequest pRequest)
        {
            RemoveForRequest(pRequest);
        }

        internal static void Clear()
        {
            DemandsByDockCity.Clear();
            NextRequestAtByKingdom.Clear();
            LoggedOutcomes.Clear();
            TemporaryBoatIds.Clear();
            PendingTemporaryBoatRemovalIds.Clear();
            TemporaryBoatBuildCityIds.Clear();
            TemporaryBoatBuildOceanTilesByCity.Clear();
        }

        private static bool TryRefreshExisting(TaxiRequest pRequest,
            double pNow)
        {
            long demandCityId = -1L;
            ProductionDemand matchingDemand = null;
            foreach (KeyValuePair<long, ProductionDemand> pair in
                     DemandsByDockCity)
            {
                if (!ReferenceEquals(pair.Value.Request, pRequest))
                    continue;
                demandCityId = pair.Key;
                matchingDemand = pair.Value;
                break;
            }
            if (matchingDemand == null) return false;
            matchingDemand.ExpiresAt = pNow + DemandLifetimeSeconds;
            LogOutcomeOnce(matchingDemand.KingdomId, "demand_refreshed",
                matchingDemand.DockCity, pRequest);
            if (TryBuild(matchingDemand, pNow))
                DemandsByDockCity.Remove(demandCityId);
            return true;
        }

        private static bool TryBuild(ProductionDemand pDemand,
            double pNow)
        {
            if (pDemand?.DockCity?.data == null || pDemand.Dock == null ||
                pDemand.DockCity.kingdom?.data == null ||
                pNow < pDemand.NextBuildAttemptAt) return false;
            pDemand.NextBuildAttemptAt = pNow + RetryCooldownSeconds;
            if (!pDemand.AttemptLogged)
            {
                pDemand.AttemptLogged = true;
                LogOutcomeOnce(pDemand.KingdomId, "build_attempted",
                    pDemand.DockCity, pDemand.Request);
            }
            Actor boat;
            try
            {
                boat = pDemand.Dock.buildBoatFromHere(pDemand.DockCity);
            }
            catch (Exception error)
            {
                LogBuildFailure(pDemand, error.GetType().Name);
                return false;
            }
            if (boat?.data == null)
            {
                LogBuildFailure(pDemand, "original_returned_null");
                return false;
            }
            try
            {
                boat.joinKingdom(pDemand.DockCity.kingdom);
                boat.joinCity(pDemand.DockCity);
                pDemand.DockCity.timer_build_boat = 10f;
                LogOutcomeOnce(pDemand.KingdomId, "build_succeeded",
                    pDemand.DockCity, pDemand.Request,
                    " boat=" + boat.data.id);
                return true;
            }
            catch (Exception error)
            {
                LogBuildFailure(pDemand,
                    "ownership_" + error.GetType().Name);
                return false;
            }
        }

        private static void LogBuildFailure(ProductionDemand pDemand,
            string pReason)
        {
            if (pDemand == null || pDemand.FailureLogged) return;
            pDemand.FailureLogged = true;
            LogOutcomeOnce(pDemand.KingdomId, "build_failed",
                pDemand.DockCity, pDemand.Request,
                " reason=" + pReason);
        }

        private static int CountPendingRequests(Kingdom pKingdom)
        {
            int count = 0;
            try
            {
                for (int i = 0; i < TaxiManager.list.Count; i++)
                    if (IsPendingFor(TaxiManager.list[i], pKingdom)) count++;
            }
            catch { }
            return count;
        }

        private static bool IsPendingFor(TaxiRequest pRequest,
            Kingdom pKingdom)
        {
            try
            {
                return pRequest != null && pKingdom?.data != null &&
                       pRequest.isState(TaxiRequestState.Pending) &&
                       !pRequest.hasAssignedBoat() &&
                       pRequest.isSameKingdom(pKingdom) &&
                       pRequest.countActors() > 0;
            }
            catch { return false; }
        }

        private static int CountDedicatedTransports(Kingdom pKingdom)
        {
            var seen = new HashSet<long>();
            List<City> cities = pKingdom?.cities;
            int cityCount = cities?.Count ?? 0;
            for (int cityIndex = 0; cityIndex < cityCount; cityIndex++)
            {
                City city;
                try { city = cities[cityIndex]; }
                catch { continue; }
                List<Actor> boats = SafeCityBoats(city);
                int boatCount = boats?.Count ?? 0;
                for (int boatIndex = 0; boatIndex < boatCount; boatIndex++)
                {
                    Actor boat;
                    try { boat = boats[boatIndex]; }
                    catch { continue; }
                    try
                    {
                        if (boat?.data == null || boat.asset == null ||
                            boat.kingdom != pKingdom || !boat.isAlive() ||
                            boat.isRekt() ||
                            (!boat.asset.is_boat_transport &&
                             !string.Equals(boat.asset.boat_type,
                                 TransportBoatType,
                                 StringComparison.Ordinal))) continue;
                        seen.Add(boat.data.id);
                    }
                    catch { }
                }
            }
            return seen.Count;
        }

        private static List<Actor> SafeCityBoats(City pCity)
        {
            if (pCity == null || CityBoatsField == null) return null;
            try { return CityBoatsField.GetValue(pCity) as List<Actor>; }
            catch { return null; }
        }

        private static ProductionDock FindRouteDock(Kingdom pKingdom,
            WorldTile pPickup, WorldTile pTarget,
            bool pIgnoreDemand = false, bool pAllowFullDock = false)
        {
            List<City> cities = pKingdom?.cities;
            int cityCount = cities?.Count ?? 0;
            for (int cityIndex = 0; cityIndex < cityCount; cityIndex++)
            {
                City city;
                try { city = cities[cityIndex]; }
                catch { continue; }
                if (city?.data == null || city.kingdom != pKingdom ||
                    (!pIgnoreDemand && DemandsByDockCity.ContainsKey(
                        city.id))) continue;
                List<Building> buildings = city.buildings;
                int buildingCount = buildings?.Count ?? 0;
                for (int buildingIndex = 0;
                     buildingIndex < buildingCount; buildingIndex++)
                {
                    Building building;
                    try { building = buildings[buildingIndex]; }
                    catch { continue; }
                    if (!IsUsableDock(building)) continue;
                    Docks docks = building.component_docks;
                    try
                    {
                        if (!pAllowFullDock &&
                            docks.isFull(TransportBoatType)) continue;
                        if (docks.tiles_ocean == null ||
                            docks.tiles_ocean.Count == 0)
                            docks.recalculateOceanTiles();
                        int oceanCount = docks.tiles_ocean?.Count ?? 0;
                        for (int oceanIndex = 0;
                             oceanIndex < oceanCount; oceanIndex++)
                        {
                            WorldTile ocean = docks.tiles_ocean[oceanIndex];
                            if (ConnectsCoasts(pPickup, pTarget, ocean))
                                return new ProductionDock
                                {
                                    City = city,
                                    Dock = docks,
                                    OceanTile = ocean
                                };
                        }
                    }
                    catch { }
                }
            }
            return null;
        }

        private static bool IsUsableDock(Building pBuilding)
        {
            try
            {
                return pBuilding?.asset != null &&
                       pBuilding.component_docks != null &&
                       string.Equals(pBuilding.asset.type, "type_docks",
                           StringComparison.Ordinal) &&
                       !pBuilding.isUnderConstruction() &&
                       pBuilding.isUsable();
            }
            catch { return false; }
        }

        private static bool ConnectsCoasts(WorldTile pPickup,
            WorldTile pTarget, WorldTile pOcean)
        {
            return CoastConnected(pPickup, pOcean) &&
                   CoastConnected(pTarget, pOcean);
        }

        private static bool CoastConnected(WorldTile pLand,
            WorldTile pOcean)
        {
            TileIsland landIsland = pLand?.region?.island;
            TileIsland oceanIsland = pOcean?.region?.island;
            if (landIsland == null || oceanIsland == null) return false;
            if (ReferenceEquals(landIsland, oceanIsland)) return true;
            try
            {
                landIsland.calcNeighbourIslands();
                return landIsland.isConnectedWith(oceanIsland);
            }
            catch { return false; }
        }

        private static void RemoveForRequest(TaxiRequest pRequest)
        {
            if (pRequest == null) return;
            var remove = new List<long>();
            foreach (KeyValuePair<long, ProductionDemand> pair in
                     DemandsByDockCity)
                if (ReferenceEquals(pair.Value.Request, pRequest))
                    remove.Add(pair.Key);
            for (int i = 0; i < remove.Count; i++)
                DemandsByDockCity.Remove(remove[i]);
        }

        private static void Prune(double pNow)
        {
            if (DemandsByDockCity.Count > 0)
            {
                var remove = new List<long>();
                foreach (KeyValuePair<long, ProductionDemand> pair in
                         DemandsByDockCity)
                    if (pair.Value == null || pair.Value.ExpiresAt <= pNow ||
                        pair.Value.Request == null)
                        remove.Add(pair.Key);
                for (int i = 0; i < remove.Count; i++)
                    DemandsByDockCity.Remove(remove[i]);
            }
            if (NextRequestAtByKingdom.Count == 0) return;
            var expiredCooldowns = new List<long>();
            foreach (KeyValuePair<long, double> pair in
                     NextRequestAtByKingdom)
                if (pair.Value <= pNow) expiredCooldowns.Add(pair.Key);
            for (int i = 0; i < expiredCooldowns.Count; i++)
                NextRequestAtByKingdom.Remove(expiredCooldowns[i]);
        }

        private static double CurrentRealtime()
        {
            try { return Time.realtimeSinceStartupAsDouble; }
            catch { return 0d; }
        }

        private static void LogOutcomeOnce(long pKingdomId,
            string pPhase, City pDockCity, TaxiRequest pRequest,
            string pDetail = "")
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            string key = pKingdomId + ":" + pPhase;
            if (!LoggedOutcomes.Add(key)) return;
            int actors = 0;
            try { actors = pRequest?.countActors() ?? 0; }
            catch { }
            try
            {
                ModClass.LogInfo("[AW3 RTS transport production] phase=" +
                                 pPhase + " kingdom=" + pKingdomId +
                                 " dock_city=" +
                                 (pDockCity?.id ?? -1L) + " actors=" +
                                 actors + pDetail);
            }
            catch { }
        }
    }
}
