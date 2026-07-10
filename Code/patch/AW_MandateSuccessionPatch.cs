using AncientWarfare3.core.lineage;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    public static class AW_MandateSuccessionPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), nameof(KingdomBehCheckKing.execute))]
        public static void Execute_Prefix(Kingdom pKingdom)
        {
            if (!UsesManagedLineage(pKingdom)) return;
            if (!pKingdom.hasKing()) return;
            Actor king = pKingdom.king;
            if (king?.data == null || king.isAlive()) return;
            HeirService.PrepareSuccessionBeforeKingDeath(pKingdom, king);
        }

        private static bool UsesManagedLineage(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   (LineageService.IsXiaKingdom(pKingdom) ||
                    XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
        public static bool CheckKingdomChaos_Prefix(Kingdom pMainKingdom)
        {
            bool managed = UsesManagedLineage(pMainKingdom);
            return !SuccessionTransitionRules.ShouldBlockVanillaMassFragmentation(managed);
        }
    }
}
