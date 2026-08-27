using System;
using System.Threading;
using HarmonyLib;

namespace AncientWarfare3.patch;

/// <summary>
/// Null-boundary protection only. Valid enemy lookups remain entirely
/// vanilla; no global presence cache or negative-result scan is maintained.
/// </summary>
internal static class AW_EnemyFinderCachePatch
{
    private static readonly EnemyFinderData EmptyResult =
        new EnemyFinderData();
    private static int recoveredNullReferences;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.getData))]
    private static bool GetDataPrefix(
        EnemyFinderContainer __instance,
        MapChunk pChunk,
        ref EnemyFinderData __result)
    {
        if (__instance != null && pChunk != null &&
            pChunk.objects != null && __instance.dict_data != null)
            return true;

        __result = EmptyResult;
        RecordRecoveredNullReference();
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

        __result = EmptyResult;
        RecordRecoveredNullReference();
        return false;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.getData))]
    private static Exception RecoverGetDataNullReference(
        Exception __exception,
        ref EnemyFinderData __result)
    {
        if (!(__exception is NullReferenceException))
            return __exception;

        __result = EmptyResult;
        RecordRecoveredNullReference();
        return null;
    }

    private static void RecordRecoveredNullReference()
    {
        if (Interlocked.Increment(ref recoveredNullReferences) == 1)
        {
            ModClass.LogError(
                "AW recovered an invalid EnemyFinder input; " +
                "the affected lookup returned empty.");
        }
    }
}
