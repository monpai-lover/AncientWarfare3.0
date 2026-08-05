using System;
using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_ManualAllianceToolPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(ActionLibrary), nameof(ActionLibrary.clickUnity))]
        private static void ClickUnityPrefix()
        {
            if (ManualAllianceToolRules.IsVanillaAllianceTool(
                    "ActionLibrary.clickUnity"))
                ManualAllianceToolScope.Enter();
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ActionLibrary), nameof(ActionLibrary.clickUnity))]
        private static Exception ClickUnityFinalizer(Exception __exception)
        {
            ManualAllianceToolScope.Exit();
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(DiplomacyHelpers),
            nameof(DiplomacyHelpers.areKingdomsClose))]
        private static bool AreKingdomsClosePrefix(ref bool __result)
        {
            if (!ManualAllianceToolScope.IsActive) return true;
            __result = true;
            return false;
        }
    }
}
