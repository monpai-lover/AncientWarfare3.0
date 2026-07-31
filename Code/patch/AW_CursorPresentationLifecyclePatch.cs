using AncientWarfare3.core.performance;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CursorPresentationLifecyclePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PowerButtonSelector),
            nameof(PowerButtonSelector.setPower))]
        private static void ClearCursorPoolAfterPowerSelection()
        {
            AWCursorPresentationLifecycle.ClearCursorPowerPool();
        }
    }
}
