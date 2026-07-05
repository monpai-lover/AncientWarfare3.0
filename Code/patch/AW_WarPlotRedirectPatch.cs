using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WarPlotRedirectPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BehCheckPlotProgress), nameof(BehCheckPlotProgress.execute))]
        public static bool CheckPlotProgress_Prefix(Actor pActor, ref BehResult __result)
        {
            if (!WarPlotRedirectService.TryConsumeActiveNewWarPlot(pActor)) return true;
            __result = BehResult.Stop;
            return false;
        }
    }
}
