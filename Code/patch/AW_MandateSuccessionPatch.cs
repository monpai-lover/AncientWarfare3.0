using AncientWarfare3.api.multiplayer;
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
        public static bool Execute_Prefix(Kingdom pKingdom,
            ref BehResult __result)
        {
            if (!UsesManagedLineage(pKingdom)) return true;
            if (!pKingdom.hasKing()) return true;
            Actor king = pKingdom.king;
            if (king?.data == null || king.isAlive()) return true;
            if (!SuccessionPreparationService.TryPublishForNativeSuccession(
                    pKingdom, king))
            {
                __result = BehResult.Continue;
                return false;
            }
            if (!AW3MultiplayerSuccessionFacade.TryDefer(pKingdom, king))
            {
                NormalizeRoyalClanForNativeSuccession(pKingdom);
                return true;
            }
            __result = BehResult.Continue;
            return false;
        }

        private static void NormalizeRoyalClanForNativeSuccession(
            Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                pKingdom.data.royal_clan_id < 0L) return;
            Clan clan = null;
            try
            {
                clan = World.world?.clans?.get(
                    pKingdom.data.royal_clan_id);
            }
            catch { }
            bool invalid = clan?.data == null;
            if (!invalid)
            {
                try { invalid = clan.isRekt(); }
                catch { invalid = true; }
            }
            if (invalid) pKingdom.data.royal_clan_id = -1L;
        }

        private static bool UsesManagedLineage(Kingdom pKingdom)
        {
            return pKingdom?.data != null &&
                   SuccessionTransitionRules.ShouldUseManagedSuccession(
                       LineageService.IsXiaKingdom(pKingdom),
                       XiaizationService.UsesXiaizedInstitutionSystem(pKingdom));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KingdomBehCheckKing), "checkKingdomChaos")]
        public static bool CheckKingdomChaos_Prefix(Kingdom pMainKingdom)
        {
            bool managed = UsesManagedLineage(pMainKingdom);
            bool blocked = SuccessionTransitionRules
                .ShouldBlockVanillaMassFragmentation(managed);
            if (!blocked) return true;
            if (MandateService.ShouldBlockPeacefulFellApart(pMainKingdom))
                MandateService.OnPeacefulFellApartBlocked(pMainKingdom);
            return false;
        }
    }
}
