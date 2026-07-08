using System.Collections.Generic;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomAgeSafePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.updateAge))]
        public static bool UpdateAge_Prefix(KingdomManager __instance)
        {
            if (__instance == null) return false;

            __instance.checkLists();
            var snapshot = new List<Kingdom>(__instance.list);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Kingdom kingdom = snapshot[i];
                if (kingdom == null || !kingdom.isAlive() || kingdom.data == null) continue;
                kingdom.updateAge();
            }

            return false;
        }
    }
}
