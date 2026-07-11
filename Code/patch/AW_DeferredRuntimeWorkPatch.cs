using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_DeferredRuntimeWorkPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "Update")]
        private static void MapBoxUpdate_Postfix()
        {
            if (!Config.game_loaded || SmoothLoader.isLoading()) return;
            Bench.bench(CityMaintenanceBenchmarkRules.DeferredFlush,
                CityMaintenanceBenchmarkRules.Group);
            DeferredRuntimeWorkService.DrainFrame();
            Bench.benchEnd(CityMaintenanceBenchmarkRules.DeferredFlush,
                CityMaintenanceBenchmarkRules.Group);
            Bench.bench(CityMaintenanceBenchmarkRules.CaptureScanStep,
                CityMaintenanceBenchmarkRules.Group);
            SlaveCaptureScanService.DrainFrame();
            Bench.benchEnd(CityMaintenanceBenchmarkRules.CaptureScanStep,
                CityMaintenanceBenchmarkRules.Group);
        }
    }
}
