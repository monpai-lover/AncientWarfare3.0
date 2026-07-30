using System;
using AncientWarfare3.core.asyncwork;
using AncientWarfare3.ui.windows;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_AsyncWorldLifecyclePatch
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static void ClearWorld_Prefix()
        {
            AWAsyncClearWorldGuard.BeginInvocation();
            if (!AWAsyncWorldLifecycle.TryBeginWorldChange(
                    out string lifecycleError))
                throw new InvalidOperationException(
                    "AW3 world clear blocked: " + lifecycleError);
            FamilyTreeWindow.ResetWorldState();
            AWAsyncClearWorldGuard.Grant();
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(MapBox), nameof(MapBox.clearWorld))]
        private static Exception ClearWorld_Finalizer(Exception __exception)
        {
            AWAsyncClearWorldGuard.EndInvocation();
            return __exception;
        }
    }
}
