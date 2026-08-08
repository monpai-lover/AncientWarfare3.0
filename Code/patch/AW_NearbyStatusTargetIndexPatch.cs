using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch;

[HarmonyPatch]
internal static class AW_NearbyStatusTargetIndexPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BehTryFindTargetWithStatusNearby), "getClosestActorWithStatus")]
    private static bool GetClosestActorWithStatusPrefix(Actor __0, string[] __1, ref Actor __result)
    {
        if (!AWNearbyStatusTargetIndex.TryFindClosest(__0, __1, out Actor target)) return true;
        __result = target;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StatusManager), nameof(StatusManager.newStatus))]
    private static void NewStatusPostfix(BaseSimObject pSimObject, StatusAsset pAsset) =>
        AWNearbyStatusTargetIndex.NotifyStatusAdded(pSimObject, pAsset);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.removeFinishedStatusEffect))]
    private static void RemovedStatusPostfix(BaseSimObject __instance, Status pStatusData) =>
        AWNearbyStatusTargetIndex.NotifyStatusRemoved(__instance, pStatusData?.asset);

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseSimObject), "finishAllStatusEffects")]
    private static void RemovedAllStatusesPostfix(BaseSimObject __instance) =>
        AWNearbyStatusTargetIndex.NotifyAllStatusesRemoved(__instance);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
    private static void ClearWorldPrefix()
    {
        AWNearbyStatusTargetIndex.Reset();
    }
}
