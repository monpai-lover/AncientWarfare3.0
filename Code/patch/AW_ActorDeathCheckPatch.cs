using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ActorDeathCheckPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Actor), "checkDeath")]
        internal static bool CheckDeath_Prefix(Actor __instance)
        {
            bool hasData = __instance?.data != null;
            bool isRekt = false;
            bool isAlive = false;
            bool hasCurrentTile = false;
            try
            {
                if (__instance != null)
                {
                    isRekt = __instance.isRekt();
                    isAlive = __instance.isAlive();
                    hasCurrentTile = __instance.current_tile != null;
                }
            }
            catch
            {
                return false;
            }
            return ActorDeathSafetyRules.ShouldRunDeathCheck(
                hasData, isRekt, isAlive, hasCurrentTile);
        }
    }
}
