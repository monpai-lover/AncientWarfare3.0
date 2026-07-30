using System;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarScoreDecisiveSettlementService
    {
        private const string QueuePrefix = "war_score_decisive_settlement:";
        private const int MaximumAttempts = 2;

        public static bool QueueIfDecisive(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(pWar)) return false;
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot) ||
                WarScoreDecisiveSettlementRules.WinnerSide(snapshot.Score) ==
                WarScoreSide.None) return false;

            long warId = pWar.data.id;
            Enqueue(warId, 0);
            return true;
        }

        private static void Enqueue(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(QueuePrefix + pWarId,
                DeferredWorkClass.Runtime,
                () => Process(pWarId, pAttempt));
        }

        private static void Process(long pWarId, int pAttempt)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            if (war?.data == null || war.hasEnded() ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(war)) return;
            Kingdom attacker = MainAttacker(war);
            Kingdom defender = MainDefender(war);
            if (attacker?.data == null || defender?.data == null ||
                !WarScoreService.TryGetSnapshot(war, attacker,
                    out WarScoreSnapshot snapshot)) return;

            WarScoreSide winnerSide = WarScoreDecisiveSettlementRules
                .WinnerSide(snapshot.Score);
            if (winnerSide == WarScoreSide.None) return;
            Kingdom winner = winnerSide == WarScoreSide.Attackers
                ? attacker
                : defender;
            Kingdom loser = winnerSide == WarScoreSide.Attackers
                ? defender
                : attacker;
            WarPeaceSettlementDraft draft = WarPeaceSettlementService
                .Instance.BuildDefaultDraft(war, winner, loser,
                    WarScoreRules.MaximumScore,
                    WarPeaceDefaultOfferMode.EnforceDemands);
            draft.PlayerInitiated = false;
            WarPeaceExecutionResult result = WarPeaceSettlementService
                .Instance.ForceDecisiveSettlement(draft);
            if (result.Success || war?.data == null || war.hasEnded()) return;
            if (pAttempt < MaximumAttempts)
            {
                DeferredRuntimeWorkService.EnqueueCoalesced(
                    QueuePrefix + pWarId, DeferredWorkClass.Runtime,
                    () => Process(pWarId, pAttempt + 1));
                return;
            }
            ModClass.LogWarning("Decisive war settlement failed war=" +
                                pWarId + " reason=" + result.Reason);
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
