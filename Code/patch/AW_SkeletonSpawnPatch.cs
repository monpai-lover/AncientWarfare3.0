using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(ActionLibrary), "spawnSkeleton")]
    public static class AW_SkeletonSpawnPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !SkeletonSpawnRules.ShouldBlockNewSpawn();
        }
    }
}
