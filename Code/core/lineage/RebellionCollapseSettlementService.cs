using AncientWarfare3.api.multiplayer;
using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class RebellionCollapseSettlementService
    {
        private const string QueuePrefix =
            "rebellion_force_collapse_settlement:";
        private const string ForceQueuePrefix =
            "rebellion_force_elimination_settlement:";
        private const int MaximumAttempts = 2;
        private static readonly HashSet<long> ForceQueuedWarIds =
            new HashSet<long>();

        public static void ClearRuntime()
        {
            ForceQueuedWarIds.Clear();
        }

        public static bool QueueIfCollapsed(War pWar)
        {
            return WarForceEliminationSettlementService.QueueIfReady(pWar);
        }

        public static bool QueueForceElimination(War pWar,
            WarForceEliminationDecision pDecision)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(pWar) ||
                pDecision.Kind == WarForceEliminationDecisionKind.None)
                return false;
            long warId = pWar.data.id;
            if (!ForceQueuedWarIds.Add(warId)) return true;
            EnqueueForce(warId, 0);
            return true;
        }

        private static void EnqueueForce(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                ForceQueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => ProcessForceElimination(pWarId, pAttempt));
        }

        private static void ProcessForceElimination(long pWarId,
            int pAttempt)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            if (war?.data == null || war.hasEnded() ||
                !RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(war) ||
                !WarForceEliminationSettlementService.
                    TryGetConfirmedDecision(war,
                        out WarForceEliminationDecision decision))
            {
                ForceQueuedWarIds.Remove(pWarId);
                return;
            }
            Kingdom attacker = war.getMainAttacker();
            Kingdom defender = war.getMainDefender();
            Kingdom winner = decision.Beneficiary == WarScoreSide.Attackers
                ? attacker
                : defender;
            Kingdom loser = winner == attacker ? defender : attacker;
            WarWinner result = decision.Beneficiary == WarScoreSide.Attackers
                ? WarWinner.Attackers
                : decision.Beneficiary == WarScoreSide.Defenders
                    ? WarWinner.Defenders
                    : WarWinner.Peace;
            try
            {
                bool surrender = decision.Kind ==
                                     WarForceEliminationDecisionKind.
                                         AttackersSurrender ||
                                 decision.Kind ==
                                     WarForceEliminationDecisionKind.
                                         DefendersSurrender;
                bool transferred = result == WarWinner.Peace ||
                    (surrender
                        ? WarForceSpecialSettlementService.
                            TransferAllCities(loser, winner)
                        : WarForceSpecialSettlementService.
                            TransferScoreAffordableCities(war, loser,
                                winner, decision.Score));
                if (!transferred)
                    throw new InvalidOperationException(
                        "rebellion force territory transfer failed");
                World.world.wars.endWar(war, result);
                ForceQueuedWarIds.Remove(pWarId);
            }
            catch (Exception exception)
            {
                if (pAttempt < MaximumAttempts)
                {
                    EnqueueForce(pWarId, pAttempt + 1);
                    return;
                }
                ForceQueuedWarIds.Remove(pWarId);
                ModClass.LogWarning(
                    "Rebellion force elimination failed war=" + pWarId +
                    ": " + exception.Message);
            }
        }

        private static void Process(long pWarId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            bool valid = war?.data != null;
            bool active = false;
            bool rebellion = false;
            bool rebelParticipantValid = false;
            bool warriorFactsReadable = false;
            int rebelWarriors = -1;
            int rebelReserves = -1;
            Kingdom rebel = null;
            try
            {
                active = valid && !war.hasEnded();
                rebellion = active && war.getAsset()?.rebellion == true;
                rebel = active ? war.getMainAttacker() : null;
                rebelParticipantValid = rebel?.data != null &&
                                        war.isAttacker(rebel);
                if (rebelParticipantValid)
                {
                    rebelWarriors = war.countAttackersWarriors();
                    warriorFactsReadable = rebelWarriors >= 0;
                    rebelReserves = CityReservePoolService.
                        CountAvailable(rebel);
                }
            }
            catch
            {
                warriorFactsReadable = false;
            }

            if (!RebellionForceCollapseRules.ShouldCollapse(
                    valid, active, rebellion, rebelParticipantValid,
                    warriorFactsReadable, rebelWarriors, rebelReserves))
                return;
            World.world?.wars?.endWar(war, WarWinner.Defenders);
        }
    }
}
