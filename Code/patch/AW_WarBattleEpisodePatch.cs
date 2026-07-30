using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WarBattleEpisodePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BattleKeeperManager), nameof(BattleKeeperManager.update))]
        private static void BattleKeeperUpdate_Postfix()
        {
            WarBattleEpisodeService.ProcessFrame();
        }

        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            WarBattleEpisodeService.ClearRuntime();
        }
    }
}
