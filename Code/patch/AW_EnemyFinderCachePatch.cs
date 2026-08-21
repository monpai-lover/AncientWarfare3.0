
using System;
using System.Threading;
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
    private static int _recoveredNullReferences;

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
        if (__instance == null || pChunk == null || pChunk.objects == null ||
            __instance.dict_data == null)
        {
            __result = AWEnemyPresenceCache.SharedEmptyResult;
            RecordRecoveredNullReference();
            return false;
        }

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

        Kingdom kingdom;
        try
        {
            kingdom = KingdomField(__instance);
        }
        catch (NullReferenceException)
        {
            __result = AWEnemyPresenceCache.SharedEmptyResult;
            RecordRecoveredNullReference();
            return false;
        }
        if (kingdom != null &&
            AWEnemyPresenceCache.TryGetNegativeResult(
                kingdom,
                key))
        {
            EnemiesFinder.counter_reused++;
            __result = AWEnemyPresenceCache.SharedEmptyResult;
            return false;
        }

        if (kingdom == null || kingdom.asset == null)
        {
            __result = AWEnemyPresenceCache.SharedEmptyResult;
            RecordRecoveredNullReference();
            return false;
        }

        if (!AWEnemyPresenceCache.IsPreparationActive ||
            AWEnemyPresenceCache.HasPopulatedEnemy(kingdom))
        {
            return true;
        }

        AWEnemyPresenceCache.AddNegativeResult(
            kingdom,
            key,
            pRange);
        __result = AWEnemyPresenceCache.SharedEmptyResult;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnemiesFinder), "findEnemiesFrom")]
    private static bool FindEnemiesFromPrefix(
        WorldTile pTile,
        Kingdom pKingdom,
        ref EnemyFinderData __result)
    {
        if (pTile != null && pKingdom != null && pKingdom.asset != null)
            return true;

        __result = AWEnemyPresenceCache.SharedEmptyResult;
        RecordRecoveredNullReference();
        return false;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(
        typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.getData))]
    private static Exception RecoverGetDataNullReference(
        Exception __exception,
        ref EnemyFinderData __result)
    {
        if (!(__exception is NullReferenceException))
            return __exception;

        __result = AWEnemyPresenceCache.SharedEmptyResult;
        RecordRecoveredNullReference();
        return null;
    }

    private static void RecordRecoveredNullReference()
    {
        if (Interlocked.Increment(ref _recoveredNullReferences) == 1)
        {
            ModClass.LogWarning(
                "AW recovered an invalid EnemyFinderContainer during " +
                "simulation; the affected enemy search returned empty.");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(
        typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.clear))]
    private static void clearContainer(
        EnemyFinderContainer __instance)
    {
        if (__instance == null)
            return;

        Kingdom kingdom;
        try
        {
            kingdom = KingdomField(__instance);
        }
        catch (NullReferenceException)
        {
            return;
        }
        if (kingdom == null)
            return;
        AWEnemyPresenceCache
            .ClearNegativeKeys(
                kingdom);
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
