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
            if (pKingdom?.data == null || !MandateService.IsMandateKingdom(pKingdom)) return;
            if (!pKingdom.hasKing()) return;
            Actor king = pKingdom.king;
            if (king?.data == null || king.isAlive()) return;
            HeirService.PrepareSuccessionBeforeKingDeath(pKingdom, king);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
        public static bool CheckKingdomChaos_Prefix(Kingdom pMainKingdom)
        {
            if (!MandateService.ShouldBlockPeacefulFellApart(pMainKingdom)) return true;
            MandateService.OnPeacefulFellApartBlocked(pMainKingdom);
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), "checkShatteredCrownEvent")]
        public static bool CheckShatteredCrownEvent_Prefix(Kingdom pMainKingdom, Actor pMainKing, Clan pRoyalClan)
        {
            if (!MandateService.ShouldBlockPeacefulFellApart(pMainKingdom)) return true;
            MandateService.OnPeacefulFellApartBlocked(pMainKingdom);
            return false;
        }
    }
}
