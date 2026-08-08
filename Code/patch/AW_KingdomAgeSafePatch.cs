using System.Collections.Generic;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_KingdomAgeSafePatch
    {
        private static readonly List<Kingdom> Snapshot =
            new List<Kingdom>();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.updateAge))]
        public static bool UpdateAge_Prefix(KingdomManager __instance)
        {
            if (__instance == null) return false;

            __instance.checkLists();
            Snapshot.Clear();
            foreach (Kingdom kingdom in __instance.list)
                Snapshot.Add(kingdom);
            try
            {
                for (int i = 0; i < Snapshot.Count; i++)
                {
                    Kingdom kingdom = Snapshot[i];
                    if (kingdom == null || !kingdom.isAlive() ||
                        kingdom.data == null) continue;
                    kingdom.updateAge();
                }
            }
            finally
            {
                Snapshot.Clear();
            }

            return false;
        }
    }
}
