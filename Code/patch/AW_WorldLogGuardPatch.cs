using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_WorldLogGuardPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WorldLog), nameof(WorldLog.logNewKing))]
        public static bool LogNewKing_Prefix(Kingdom pKingdom)
        {
            return pKingdom?.king != null;
        }
    }
}
