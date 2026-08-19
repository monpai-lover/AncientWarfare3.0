using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarScoreDecisiveSettlementService
    {
        public static bool QueueIfDecisive(War pWar)
        {
            if (!TryReadWinner(pWar, out _, out _, out _)) return false;
            return WarTerminalSettlementCoordinator.NotifyWarChanged(pWar);
        }

        internal static bool TryExecuteImmediate(War pWar,
            out WarPeaceExecutionResult pResult)
        {
            pResult = new WarPeaceExecutionResult(false, -1,
                "war_score_not_decisive");
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !TryReadWinner(pWar, out Kingdom winner,
                    out Kingdom loser, out _)) return false;

            WarPeaceSettlementDraft draft = WarPeaceSettlementService
                .Instance.BuildDefaultDraft(pWar, winner, loser,
                    WarScoreRules.MaximumScore,
                    WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit);
            draft.PlayerInitiated = false;
            pResult = WarPeaceSettlementService.Instance
                .ForceDecisiveSettlement(draft);
            return pResult.Success;
        }

        private static bool TryReadWinner(War pWar, out Kingdom pWinner,
            out Kingdom pLoser, out WarScoreSnapshot pSnapshot)
        {
            pWinner = null;
            pLoser = null;
            pSnapshot = default;
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar) ||
                RebellionDirectTerritoryTransferService
                    .BlocksOrdinarySettlement(pWar)) return false;
            Kingdom attacker = MainAttacker(pWar);
            Kingdom defender = MainDefender(pWar);
            if (attacker?.data == null || defender?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out pSnapshot)) return false;
            WarScoreSide side = WarScoreDecisiveSettlementRules.WinnerSide(
                pSnapshot.Score);
            if (side == WarScoreSide.None) return false;
            pWinner = side == WarScoreSide.Attackers ? attacker : defender;
            pLoser = side == WarScoreSide.Attackers ? defender : attacker;
            return true;
        }

        private static Kingdom MainAttacker(War pWar)
        {
            try { return pWar?.getMainAttacker(); }
            catch { return null; }
        }

        private static Kingdom MainDefender(War pWar)
        {
            try { return pWar?.getMainDefender(); }
            catch { return null; }
        }
    }
}
