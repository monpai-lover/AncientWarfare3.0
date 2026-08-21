using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Removes stale building references before vanilla city construction and
    /// dirty-building indexes dereference their asset or data fields.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_CityBuildNullSafetyPatch
    {
        private static int _loggedRepairs;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehBuild), nameof(CityBehBuild.buildTick))]
        private static void SanitizeCityBeforeBuild(City pCity)
        {
            SanitizeCity(pCity);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityManager), "updateDirtyBuildings")]
        private static void SanitizeCitiesBeforeBuildingIndex()
        {
            if (World.world?.cities == null)
                return;

            foreach (City city in World.world.cities)
                SanitizeCity(city);
        }

        private static void SanitizeCity(City pCity)
        {
            if (pCity == null)
                return;

            Building current = pCity.under_construction_building;
            if (!IsValid(current))
                pCity.under_construction_building = null;

            int removed = RemoveInvalid(pCity.buildings);
            if (pCity.zones != null)
            {
                foreach (TileZone zone in pCity.zones)
                {
                    if (zone?.buildings_all != null)
                        removed += RemoveInvalid(zone.buildings_all);
                }
            }

            if (removed > 0 && _loggedRepairs++ == 0)
            {
                ModClass.LogWarning(
                    "AW removed stale building references from city indexes; " +
                    "vanilla building updates will continue.");
            }
        }

        private static int RemoveInvalid(List<Building> pBuildings)
        {
            if (pBuildings == null)
                return 0;

            int removed = 0;
            for (int i = pBuildings.Count - 1; i >= 0; i--)
            {
                if (IsValid(pBuildings[i]))
                    continue;

                pBuildings.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        private static int RemoveInvalid(HashSet<Building> pBuildings)
        {
            if (pBuildings == null)
                return 0;

            int before = pBuildings.Count;
            pBuildings.RemoveWhere(pBuilding => !IsValid(pBuilding));
            return before - pBuildings.Count;
        }

        private static bool IsValid(Building pBuilding)
        {
            return pBuilding != null && pBuilding.asset != null &&
                   pBuilding.data != null;
        }
    }
}
