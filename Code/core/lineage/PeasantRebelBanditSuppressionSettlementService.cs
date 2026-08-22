using System;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelBanditSuppressionSettlementService
    {
        internal static bool IsReady(War pWar)
        {
            if (!TryResolve(pWar, out Kingdom bandit, out _, out _))
                return false;
            Actor heir = HeirService.PeekRegisteredHeir(bandit);
            bool hasLivingKing = bandit.hasKing() &&
                bandit.king?.data != null && bandit.king.isAlive() &&
                !bandit.king.isRekt();
            bool hasLivingRegisteredHeir = heir?.data != null &&
                heir.isAlive() && !heir.isRekt();
            return PeasantRebelBanditStrongholdRules.
                ShouldCompleteLeaderlessSuppression(
                    isSuppressionWar: true,
                    isBanditKingdom: PeasantRebelRouteService.IsBandit(bandit),
                    hasLivingKing: hasLivingKing,
                    hasLivingRegisteredHeir: hasLivingRegisteredHeir);
        }

        internal static bool TryExecuteImmediate(War pWar)
        {
            if (!IsReady(pWar) ||
                !TryResolve(pWar, out Kingdom bandit,
                    out Kingdom suppressor, out WarWinner winner))
                return false;
            if (!PeasantRebelBanditStrongholdService.
                    TryCompleteLeadershipCollapse(bandit, suppressor))
                return false;
            if (pWar?.data != null && !pWar.hasEnded())
                World.world?.wars?.endWar(pWar, winner);
            return pWar == null || pWar.hasEnded();
        }

        private static bool TryResolve(War pWar, out Kingdom pBandit,
            out Kingdom pSuppressor, out WarWinner pWinner)
        {
            pBandit = null;
            pSuppressor = null;
            pWinner = WarWinner.Nobody;
            if (pWar?.data == null || pWar.hasEnded()) return false;
            string type = "";
            try { type = pWar.getAsset()?.id ?? ""; }
            catch { }
            if (string.IsNullOrEmpty(type)) type = pWar.data.war_type ?? "";
            if (!string.Equals(type, WarDecisionService.WAR_BANDIT_SUPPRESSION,
                    StringComparison.Ordinal)) return false;
            Kingdom attacker = pWar.main_attacker;
            Kingdom defender = pWar.main_defender;
            if (attacker?.data == null || defender?.data == null ||
                attacker == defender) return false;
            bool attackerBandit = PeasantRebelRouteService.IsBandit(attacker);
            bool defenderBandit = PeasantRebelRouteService.IsBandit(defender);
            if (attackerBandit == defenderBandit) return false;
            if (attackerBandit)
            {
                pBandit = attacker;
                pSuppressor = defender;
                pWinner = WarWinner.Defenders;
            }
            else
            {
                pBandit = defender;
                pSuppressor = attacker;
                pWinner = WarWinner.Attackers;
            }
            return pBandit?.data != null && pSuppressor?.data != null &&
                   !pBandit.isRekt() && !pSuppressor.isRekt();
        }
    }
}
