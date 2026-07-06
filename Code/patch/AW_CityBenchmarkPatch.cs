using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityBenchmarkPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateTotalFood")]
        public static void UpdateTotalFood_Prefix()
        {
            Bench.bench(CityMaintenanceBenchmarkRules.Food, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateTotalFood")]
        public static void UpdateTotalFood_Postfix()
        {
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Food, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCityStatus")]
        public static void UpdateCityStatus_Prefix()
        {
            Bench.bench(CityMaintenanceBenchmarkRules.Status, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateCityStatus")]
        public static void UpdateCityStatus_Postfix()
        {
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Status, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCitizens")]
        public static void UpdateCitizens_Prefix()
        {
            Bench.bench(CityMaintenanceBenchmarkRules.Citizens, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateCitizens")]
        public static void UpdateCitizens_Postfix()
        {
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Citizens, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static void UpdateCapture_Prefix()
        {
            Bench.bench(CityMaintenanceBenchmarkRules.Capture, CityMaintenanceBenchmarkRules.Group);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(City), "updateCapture")]
        public static void UpdateCapture_Postfix()
        {
            Bench.benchEnd(CityMaintenanceBenchmarkRules.Capture, CityMaintenanceBenchmarkRules.Group);
        }
    }
}
