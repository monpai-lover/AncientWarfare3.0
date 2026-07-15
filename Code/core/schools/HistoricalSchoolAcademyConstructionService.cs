using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolAcademyConstructionService
    {
        private const int MaxZonesToInspect = 24;
        private const int MaxTilesPerZone = 8;

        private static readonly HashSet<long> StartedCities = new HashSet<long>();
        private static readonly Dictionary<long, int> PlacementAttempts =
            new Dictionary<long, int>();

        public static void ClearRuntime()
        {
            StartedCities.Clear();
            PlacementAttempts.Clear();
        }

        public static Building TryStart(City pCity)
        {
            BuildingAsset asset = SchoolAcademyBuildingContent.Asset ??
                                  AssetManager.buildings.get(
                                      SchoolAcademyBuildingContent.BuildingId);
            bool cityValid = IsValidCity(pCity);
            bool assetAvailable = asset != null;
            bool academyIdAlreadyPresent = cityValid &&
                pCity.countBuildingsOfID(SchoolAcademyBuildingContent.BuildingId) > 0;
            bool academyTypeAlreadyPresent = cityValid &&
                pCity.countBuildingsType(SchoolAcademyBuildingContent.BuildingTypeId,
                    pCountOnlyFinished: false) > 0;
            if (!SchoolAcademyConstructionRules.ShouldStart(
                    cityValid,
                    assetAvailable,
                    academyIdAlreadyPresent,
                    academyTypeAlreadyPresent,
                    placementAvailable: true))
                return null;

            long cityId = pCity.data.id;
            if (!StartedCities.Add(cityId)) return null;

            Building building = null;
            try
            {
                int attempt = PlacementAttempts.TryGetValue(cityId, out int previousAttempt)
                    ? previousAttempt
                    : 0;
                PlacementAttempts[cityId] = attempt == int.MaxValue ? 0 : attempt + 1;
                WorldTile placement = FindPlacement(pCity, asset, attempt);
                if (!SchoolAcademyConstructionRules.ShouldStart(
                        cityValid,
                        assetAvailable,
                        pCity.countBuildingsOfID(
                            SchoolAcademyBuildingContent.BuildingId) > 0,
                        pCity.countBuildingsType(
                            SchoolAcademyBuildingContent.BuildingTypeId,
                            pCountOnlyFinished: false) > 0,
                        placementAvailable: placement != null))
                {
                    StartedCities.Remove(cityId);
                    return null;
                }

                building = World.world.buildings.addBuilding(
                    asset, placement, pCheckForBuild: false);
                if (building == null)
                {
                    StartedCities.Remove(cityId);
                    return null;
                }

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
                if (building == null) StartedCities.Remove(cityId);
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
