using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_RestorationPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapStats), nameof(MapStats.getNextId))]
        private static bool GetNextId_Prefix(string pType, ref long __result)
        {
            if (!KingdomIdentityContinuityService.TryConsumeKingdomId(pType, out long id)) return true;
            __result = id;
            return false;
        }
    }
}
