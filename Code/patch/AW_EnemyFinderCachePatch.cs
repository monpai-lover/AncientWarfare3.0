
using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch;

/// <summary>
/// 褰撲竴涓帇鍥藉湪鍏ㄥ浘娌℃湁浠讳綍鏈夋晥鏁屾柟瀵硅薄鏃讹紝鐩存帴鐢熸垚鍘熺増鐨勭┖鍒嗗潡缂撳瓨椤广€?/// 姣忎釜鏂板垎鍧楅敭浠嶆寜鍘熺増瑙勫垯娑堣€椾竴娆￠殢鏈哄垽瀹氾紝纭繚鍚庣画閫昏緫鐨勯殢鏈哄簭鍒椾笉鍙樸€?/// </summary>
internal static class AW_EnemyFinderCachePatch
{
    private static readonly AccessTools.FieldRef<
        EnemyFinderContainer,
        Kingdom> KingdomField =
        AccessTools.FieldRefAccess<
            EnemyFinderContainer,
            Kingdom>("_kingdom");

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.getData))]
    private static bool getData(
        EnemyFinderContainer __instance,
        MapChunk pChunk,
        int pRange,
        ref EnemyFinderData __result)
    {
        int key =
            pChunk.id * 10000 +
            pRange;
        if (__instance.dict_data.TryGetValue(
                key,
                out EnemyFinderData cached))
        {
            EnemiesFinder.counter_reused++;
            __result = cached;
            return false;
        }

        Kingdom kingdom =
            KingdomField(__instance);
        if (kingdom != null &&
            AWEnemyPresenceCache.TryGetNegativeResult(
                kingdom,
                key))
        {
            EnemiesFinder.counter_reused++;
            __result =
                AWEnemyPresenceCache
                    .SharedEmptyResult;
            return false;
        }

        if (!AWEnemyPresenceCache
                .IsPreparationActive ||
            kingdom == null ||
            AWEnemyPresenceCache
                .HasPopulatedEnemy(kingdom))
        {
            return true;
        }

        AWEnemyPresenceCache.AddNegativeResult(
            kingdom,
            key,
            pRange);
        __result =
            AWEnemyPresenceCache
                .SharedEmptyResult;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(
        typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.clear))]
    private static void clearContainer(
        EnemyFinderContainer __instance)
    {
        AWEnemyPresenceCache
            .ClearNegativeKeys(
                KingdomField(__instance));
    }

    [HarmonyPostfix]
    [HarmonyPatch(
        typeof(EnemiesFinder),
        nameof(EnemiesFinder.clear))]
    private static void clear()
    {
        AWEnemyPresenceCache.Clear();
    }
}
