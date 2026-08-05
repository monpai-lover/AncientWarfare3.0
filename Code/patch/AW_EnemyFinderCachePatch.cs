using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_EnemyFinderCachePatch
    {
        private static readonly AccessTools.FieldRef<
            EnemyFinderContainer, Kingdom> KingdomField =
            AccessTools.FieldRefAccess<EnemyFinderContainer, Kingdom>(
                "_kingdom");

        [HarmonyPrefix]
        [HarmonyPatch(typeof(EnemyFinderContainer),
            nameof(EnemyFinderContainer.getData))]
        private static bool GetDataPrefix(
            EnemyFinderContainer __instance,
            MapChunk pChunk,
            int pRange,
            ref EnemyFinderData __result)
        {
            int key = pChunk == null
                ? pRange
                : pChunk.id * 10000 + pRange;
            if (__instance != null &&
                __instance.dict_data != null &&
                __instance.dict_data.TryGetValue(key,
                    out EnemyFinderData cached))
            {
                EnemiesFinder.counter_reused++;
                __result = cached;
                return false;
            }

            Kingdom kingdom = KingdomField(__instance);
            if (AWEnemyPresenceCache.TryGetNegativeResult(
                    kingdom, key, out EnemyFinderData negative))
            {
                EnemiesFinder.counter_reused++;
                __result = negative;
                return false;
            }

            if (AWEnemyPresenceCache.TryGetEmptyResult(
                    kingdom, out EnemyFinderData empty))
            {
                AWEnemyPresenceCache.AddNegativeResult(kingdom, key);
                EnemiesFinder.counter_reused++;
                __result = empty;
                return false;
            }
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemyFinderContainer), nameof(
            EnemyFinderContainer.clear))]
        private static void ClearContainerPostfix(
            EnemyFinderContainer __instance)
        {
            AWEnemyPresenceCache.ClearNegativeKeys(
                KingdomField(__instance));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(EnemiesFinder), nameof(EnemiesFinder.clear))]
        private static void ClearCachePostfix()
        {
            AWEnemyPresenceCache.Clear();
        }
    }
}
