using AncientWarfare3.core.lineage;
using AncientWarfare3.api.multiplayer;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_VassalDiplomacyPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
        public static bool GetAllianceTarget_Prefix(Kingdom pKingdomStarter,
            ref Kingdom __result)
        {
            if (!ShouldBlockAlliance(pKingdomStarter)) return true;
            __result = null;
            return false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(DiplomacyHelpers), nameof(DiplomacyHelpers.getAllianceTarget))]
        public static void GetAllianceTarget_Postfix(Kingdom pKingdomStarter, ref Kingdom __result)
        {
            if (!VassalWarPermissionRules.CanUseAlliancePlot(
                    initiatorIsVassal: ShouldBlockAlliance(pKingdomStarter),
                    targetIsVassal: ShouldBlockAlliance(__result)))
                __result = null;
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
            if (AW3MultiplayerReplicaScope.IsApplying) return true;
            _ = pForce;
            if (!ShouldBlockAlliance(pKingdom)) return true;
            __result = false;
            return false;
        }

        private static bool ShouldBlockAlliance(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            bool subject = VassalService.IsVassalKingdom(pKingdom) ||
                           VassalService.IsTributaryKingdom(pKingdom);
            return !VassalWarPermissionRules.CanCreateAlliance(subject, out _);
        }
    }
}
