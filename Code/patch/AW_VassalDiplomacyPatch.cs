using AncientWarfare3.core.lineage;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_VassalDiplomacyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.newAlliance))]
        public static bool NewAlliance_Prefix(Kingdom pKingdom, Kingdom pKingdom2, ref Alliance __result)
        {
            if (!ShouldBlockAlliance(pKingdom) && !ShouldBlockAlliance(pKingdom2)) return true;
            __result = null;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AllianceManager), nameof(AllianceManager.forceAlliance))]
        public static bool ForceAlliance_Prefix(Kingdom pKingdom1, Kingdom pKingdom2, ref bool __result)
        {
            if (!ShouldBlockAlliance(pKingdom1) && !ShouldBlockAlliance(pKingdom2)) return true;
            __result = false;
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Alliance), nameof(Alliance.join))]
        public static bool AllianceJoin_Prefix(Kingdom pKingdom, bool pForce, ref bool __result)
        {
            if (pForce || !ShouldBlockAlliance(pKingdom)) return true;
            __result = false;
            return false;
        }

        private static bool ShouldBlockAlliance(Kingdom pKingdom)
        {
            return !VassalWarPermissionRules.CanCreateAlliance(VassalService.IsVassalKingdom(pKingdom), out _);
        }
    }
}
