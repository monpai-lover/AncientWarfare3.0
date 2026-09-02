using System;
using System.Threading;
using HarmonyLib;

namespace AncientWarfare3.patch;

/// <summary>
/// Null-boundary protection only. Valid enemy lookups remain entirely
/// vanilla; no global presence cache or negative-result scan is maintained.
///
/// 类级 [HarmonyPatch] 是必需的:ModClass 用
/// <c>harmony.CreateClassProcessor(type).Patch()</c> 装载,而
/// PatchClassProcessor 在类上没有 HarmonyPatch 特性时会直接返回、一个方法
/// 都不打 —— 而且不抛异常,ModClass 照样打印 "Harmony patch OK"。
/// 少了这一行,下面的前置和 finalizer 全是死代码。
/// </summary>
[HarmonyPatch]
internal static class AW_EnemyFinderCachePatch
{
    private static readonly EnemyFinderData EmptyResult =
        new EnemyFinderData();
    private static int recoveredNullReferences;

    /// <summary>
    ///     容器里那个 private 的 _kingdom。getData 第一件事就是读
    ///     <c>_kingdom.asset.force_look_all_chunks</c>,并把 _kingdom 传进
    ///     findEnemiesOfKingdomInChunk 当主国用,所以它是这个方法真正解引用
    ///     的东西,必须验。
    /// </summary>
    private static readonly AccessTools.FieldRef<EnemyFinderContainer, Kingdom>
        ContainerKingdom = ResolveContainerKingdom();

    private static AccessTools.FieldRef<EnemyFinderContainer, Kingdom>
        ResolveContainerKingdom()
    {
        try
        {
            return AccessTools.FieldRefAccess<EnemyFinderContainer, Kingdom>(
                "_kingdom");
        }
        catch
        {
            return null;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnemyFinderContainer),
        nameof(EnemyFinderContainer.getData))]
    private static bool GetDataPrefix(
        EnemyFinderContainer __instance,
        MapChunk pChunk,
        ref EnemyFinderData __result)
    {
        if (__instance != null && pChunk != null &&
            pChunk.objects != null && __instance.dict_data != null &&
            HasUsableKingdom(__instance))
            return true;

        __result = EmptyResult;
        RecordRecoveredNullReference();
        return false;
    }

    /// <summary>
    ///     容器是按王国缓存并从对象池取用的,取用时 setKingdom 只在**新建**
    ///     那一次调用。王国随后被 Dispose(<c>Kingdom.Dispose</c> 把 asset 置
    ///     null)时,容器仍握着这个失效引用,而 findEnemiesFrom 的前置校验的是
    ///     调用方传进来的王国,管不到容器里存着的这一个。
    /// </summary>
    private static bool HasUsableKingdom(EnemyFinderContainer pContainer)
    {
        if (ContainerKingdom == null) return true;
        try
        {
            Kingdom kingdom = ContainerKingdom(pContainer);
            return kingdom != null && kingdom.asset != null;
        }
        catch
        {
            return true;
        }
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
