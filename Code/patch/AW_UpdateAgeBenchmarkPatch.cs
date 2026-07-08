using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_UpdateAgeBenchmarkPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapBox), "updateObjectAge")]
        public static void UpdateObjectAge_Postfix()
        {
            UpdateAgeBenchmark.Flush();
        }
    }
}
