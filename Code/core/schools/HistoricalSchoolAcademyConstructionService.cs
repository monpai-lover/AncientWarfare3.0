using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyConstructionService
    {
        private const int MaxZonesToInspect = 24;
        private const int MaxTilesPerZone = 8;

        private static readonly Dictionary<long, Building> StartedAcademies =
            new Dictionary<long, Building>();
        private static readonly Dictionary<long, int> PlacementAttempts =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> LastPlacementAttemptYear =
            new Dictionary<long, int>();
        private const int RebuildBudgetPerFrame = 4;
        private static readonly Queue<long> PendingRebuildCities =
            new Queue<long>();
        private static readonly HashSet<long> PendingRebuildCityIds =
            new HashSet<long>();

        public static void ClearRuntime()
        {
            StartedAcademies.Clear();
            PlacementAttempts.Clear();
            LastPlacementAttemptYear.Clear();
            PendingRebuildCities.Clear();
            PendingRebuildCityIds.Clear();
        }

        internal static void RequestRebuild(Building pBuilding)
        {
            if (!IsAcademyBuilding(pBuilding)) return;
            City city = null;
            try { city = pBuilding.getCity(); }
            catch { }
            city ??= pBuilding.current_tile?.zone?.city;
            long cityId = city?.data?.id ?? -1L;
            if (cityId < 0 || !PendingRebuildCityIds.Add(cityId)) return;
            LastPlacementAttemptYear.Remove(cityId);
            PendingRebuildCities.Enqueue(cityId);
        }

        internal static void ProcessPendingRebuilds()
        {
            int processed = 0;
            while (processed++ < RebuildBudgetPerFrame &&
                   PendingRebuildCities.Count > 0)
            {
                long cityId = PendingRebuildCities.Dequeue();
                PendingRebuildCityIds.Remove(cityId);
                City city = null;
                try { city = World.world?.cities?.get(cityId); }
                catch { }
                if (city?.data == null || city.isRekt() ||
                    HistoricalSchoolAcademyService.HasLiveAcademy(city))
                    continue;
                TryStart(city);
            }
        }

        public static void InvalidateCity(long pCityId)
        {
            if (pCityId < 0) return;
            StartedAcademies.Remove(pCityId);
            PlacementAttempts.Remove(pCityId);
            LastPlacementAttemptYear.Remove(pCityId);
        }

        public static Building TryStart(City pCity)
        {
            BuildingAsset asset = SchoolAcademyBuildingContent.Asset ??
                                  AssetManager.buildings.get(
                                      SchoolAcademyBuildingContent.BuildingId);
            bool cityValid = IsValidCity(pCity);
            bool assetAvailable = asset != null;
            bool academyAlreadyPresent = cityValid &&
                HistoricalSchoolAcademyService.HasLiveAcademy(pCity);
            if (!SchoolAcademyConstructionRules.ShouldStart(
                    cityValid,
                    assetAvailable,
                    academyAlreadyPresent,
                    academyAlreadyPresent,
                    placementAvailable: true))
                return null;

            long cityId = pCity.data.id;
            if (StartedAcademies.TryGetValue(cityId, out Building started))
            {
                if (HistoricalSchoolAcademyService.IsLiveAcademyForCity(
                        started, pCity)) return null;
                StartedAcademies.Remove(cityId);
            }

            int currentYear = Date.getCurrentYear();
            LastPlacementAttemptYear.TryGetValue(cityId,
                out int lastAttemptYear);
            if (!SchoolAcademyConstructionRules.ShouldAttemptPlacement(
                    currentYear,
                    LastPlacementAttemptYear.ContainsKey(cityId)
                        ? lastAttemptYear
                        : -1))
                return null;
            LastPlacementAttemptYear[cityId] = currentYear;

            Building building = null;
            try
            {
                int attempt = PlacementAttempts.TryGetValue(cityId, out int previousAttempt)
                    ? previousAttempt
                    : 0;
                PlacementAttempts[cityId] = attempt == int.MaxValue ? 0 : attempt + 1;
                WorldTile placement = FindPlacement(pCity, asset, attempt);
                bool academyAppeared =
                    HistoricalSchoolAcademyService.HasLiveAcademy(pCity);
                if (!SchoolAcademyConstructionRules.ShouldStart(
                        cityValid,
                        assetAvailable,
                        academyAppeared,
                        academyAppeared,
                        placementAvailable: placement != null))
                {
                    return null;
                }

                building = World.world.buildings.addBuilding(
                    asset, placement, pCheckForBuild: true);
                if (building == null)
                {
                    return null;
                }

                StartedAcademies[cityId] = building;
                building.setKingdom(pCity.kingdom);
                building.setUnderConstruction();
                if (pCity.under_construction_building == null)
                    pCity.under_construction_building = building;
                ModClass.LogInfo("School academy construction started: city=" + cityId +
                                 " building=" + building.data.id);
                return building;
            }
            catch (Exception error)
            {
                if (building == null) StartedAcademies.Remove(cityId);
                ModClass.LogWarning("School academy construction failed: city=" + cityId +
                                    " - " + error.Message);
                return null;
            }
        }

        private static bool IsValidCity(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.zones != null && pCity.zones.Count > 0 &&
                   kingdom?.data != null && !kingdom.isRekt() && !kingdom.isNeutral();
        }

        private static bool IsAcademyBuilding(Building pBuilding)
        {
            BuildingAsset asset = pBuilding?.asset;
            return asset != null &&
                   (asset.id == SchoolAcademyBuildingContent.BuildingId ||
                    asset.type == SchoolAcademyBuildingContent.BuildingTypeId);
        }

        private static bool IsStartedAcademyAlive(Building pBuilding, City pCity)
        {
            if (!HistoricalSchoolAcademyService.IsAcademy(pBuilding) ||
                pBuilding?.data == null || !pBuilding.isAlive() ||
                pBuilding.isOnRemove() || pBuilding.isRemoved() ||
                pBuilding.isRuin()) return false;
            City attached = null;
            try { attached = pBuilding.getCity(); }
            catch { }
            return ReferenceEquals(attached, pCity) || attached == null &&
                   ReferenceEquals(pBuilding.current_tile?.zone?.city, pCity);
        }

        private static WorldTile FindPlacement(City pCity, BuildingAsset pAsset, int pAttempt)
        {
            WorldTile cityCenter = pCity.getTile();
            if (CanBuildAt(pCity, pAsset, cityCenter)) return cityCenter;

            int zoneCount = Math.Min(MaxZonesToInspect, pCity.zones.Count);
            int zoneStart = SchoolAcademyConstructionRules.ZoneStartIndex(
                pCity.zones.Count, pAttempt, MaxZonesToInspect);
            for (int i = 0; i < zoneCount; i++)
            {
                TileZone zone = pCity.zones[(zoneStart + i) % pCity.zones.Count];
                if (CanBuildAt(pCity, pAsset, zone?.centerTile)) return zone.centerTile;
            }

            for (int i = 0; i < zoneCount; i++)
            {
                TileZone zone = pCity.zones[(zoneStart + i) % pCity.zones.Count];
                int tileCount = zone?.tiles?.Length ?? 0;
                int checks = Math.Min(MaxTilesPerZone, tileCount);
                for (int j = 0; j < checks; j++)
                {
                    int index = checks == 1 ? tileCount / 2 :
                        j * (tileCount - 1) / (checks - 1);
                    WorldTile tile = zone.tiles[index];
                    if (CanBuildAt(pCity, pAsset, tile)) return tile;
                }
            }
            return null;
        }

        private static bool CanBuildAt(City pCity, BuildingAsset pAsset, WorldTile pTile)
        {
            if (pTile == null || pAsset == null || pTile.zone?.city != pCity ||
                World.world?.buildings == null)
                return false;
            try
            {
                return pTile.canBuildOn(pAsset) &&
                       World.world.buildings.canBuildFrom(pTile, pAsset, pCity);
            }
            catch
            {
                return false;
            }
        }
    }
}
