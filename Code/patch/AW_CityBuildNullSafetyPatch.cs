using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    /// <summary>
    /// Clears a stale in-progress construction pointer before vanilla uses it.
    /// </summary>
    [HarmonyPatch]
    internal static class AW_CityBuildNullSafetyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehBuild), nameof(CityBehBuild.buildTick))]
        private static void SanitizeCityBeforeBuild(City pCity)
        {
            Building current = pCity?.under_construction_building;
            if (current != null &&
                (current.asset == null || current.data == null))
                pCity.under_construction_building = null;
        }
    }
}
