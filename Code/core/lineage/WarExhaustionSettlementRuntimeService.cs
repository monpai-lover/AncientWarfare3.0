using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarExhaustionSettlementRuntimeService
    {
        private const string QueuePrefix = "war_exhaustion_settlement:";
        private const int MaximumAttempts = 2;

        public static bool QueueIfReady(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar) ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(pWar) ||
                WarPeaceSettlementService.Instance.HasActionableSettlement(
                    pWar.data.id)) return false;
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot) ||
                !WarExhaustionSettlementRules.CanForceSettlement(
                    snapshot.AttackerExhaustion,
                    snapshot.DefenderExhaustion)) return false;
            Enqueue(pWar.data.id, 0);
            return true;
        }

        private static void Enqueue(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => Process(pWarId, pAttempt));
        }

        private static void Process(long pWarId, int pAttempt)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            Kingdom attacker = MainAttacker(war);
            Kingdom defender = MainDefender(war);
            if (war?.data == null || war.hasEnded() ||
                ZhuluPeaceGuard.BlocksOrdinarySettlement(war) ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(war) ||
                attacker?.data == null || defender?.data == null ||
                WarPeaceSettlementService.Instance.HasActionableSettlement(
                    pWarId) ||
                !WarScoreService.TryGetSnapshot(war, attacker,
                    out WarScoreSnapshot snapshot) ||
                !WarExhaustionSettlementRules.CanForceSettlement(
                    snapshot.AttackerExhaustion,
                    snapshot.DefenderExhaustion)) return;

            WarScoreSide winnerSide = WarExhaustionSettlementRules.
                WinnerSide(snapshot.Score);
            Kingdom requester = winnerSide == WarScoreSide.Defenders
                ? defender
                : attacker;
            Kingdom responder = requester == attacker ? defender : attacker;
            int requesterScore = winnerSide == WarScoreSide.Defenders
                ? -snapshot.Score
                : snapshot.Score;
            WarPeaceDefaultOfferMode mode = winnerSide == WarScoreSide.None
                ? WarPeaceDefaultOfferMode.WhitePeace
                : WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit;
            WarPeaceSettlementDraft draft = WarPeaceSettlementService
                .Instance.BuildDefaultDraft(war, requester, responder,
                    requesterScore, mode);
            draft.PlayerInitiated = false;
            WarPeaceExecutionResult result = WarPeaceSettlementService
                .Instance.ForceExhaustionSettlement(draft,
                    snapshot.AttackerExhaustion,
                    snapshot.DefenderExhaustion);
            if (result.Success || war?.data == null || war.hasEnded()) return;
            if (pAttempt < MaximumAttempts)
            {
                Enqueue(pWarId, pAttempt + 1);
                return;
            }
            ModClass.LogWarning("War exhaustion settlement failed war=" +
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
