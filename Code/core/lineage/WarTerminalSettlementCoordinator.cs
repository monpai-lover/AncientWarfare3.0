using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarTerminalSettlementCoordinator
    {
        private const int WarsPerAuthorityCycle = 2;
        private const string QueuePrefix = "war_terminal_settlement:";
        private static readonly Dictionary<long, int> FailureCounts =
            new Dictionary<long, int>();
        private static int _recoveryCursor;

        public static void ClearRuntime()
        {
            FailureCounts.Clear();
            _recoveryCursor = 0;
        }

        public static bool NotifyWarChanged(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !IsLiveWar(pWar)) return false;
            if (!ShouldQueue(pWar)) return false;
            long warId = pWar.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + warId, DeferredWorkClass.Runtime,
                () => Process(warId));
            return true;
        }

        public static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            WarManager wars = World.world?.wars;
            if (wars == null) return;
            try { wars.checkLists(); }
            catch { return; }
            int count = wars.list.Count;
            if (count <= 0)
            {
                _recoveryCursor = 0;
                return;
            }
            if (_recoveryCursor < 0 || _recoveryCursor >= count)
                _recoveryCursor = 0;
            int inspected = 0;
            while (inspected < WarsPerAuthorityCycle && count > 0)
            {
                if (_recoveryCursor >= count) _recoveryCursor = 0;
                War war = wars.list[_recoveryCursor++];
                inspected++;
                NotifyWarChanged(war);
            }
        }

        private static bool ShouldQueue(War pWar)
        {
            if (ZhuluWarService.IsZhuluWar(pWar))
                return WarForceEliminationSettlementService
                    .TryGetConfirmedDecision(pWar, out _);
            return TryReadDecision(pWar, out _, out _);
        }

        private static void Process(long pWarId)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            if (!IsLiveWar(war))
            {
                FailureCounts.Remove(pWarId);
                return;
            }

            if (ZhuluWarService.IsZhuluWar(war))
            {
                WarForceSpecialSettlementResult zhulu =
                    WarForceSpecialSettlementService
                        .TrySettleZhuluZeroForce(war);
                if (zhulu == WarForceSpecialSettlementResult.Failed)
                    RecordFailure(pWarId, "special_zhulu_settlement_failed");
                else if (zhulu == WarForceSpecialSettlementResult.Handled)
                    FailureCounts.Remove(pWarId);
                return;
            }

            if (!TryReadDecision(war,
                    out WarTerminalSettlementDecision decision,
                    out WarForceEliminationDecision forceDecision)) return;

            bool success;
            WarPeaceExecutionResult result;
            switch (decision.Reason)
            {
                case WarTerminalSettlementReason.DecisiveScore:
                    success = WarScoreDecisiveSettlementService
                        .TryExecuteImmediate(war, out result);
                    break;
                case WarTerminalSettlementReason.ForceElimination:
                case WarTerminalSettlementReason.MutualElimination:
                case WarTerminalSettlementReason.WhitePeace:
                    success = WarForceEliminationSettlementService
                        .TryExecuteImmediate(war, forceDecision, out result);
                    break;
                case WarTerminalSettlementReason.AffordableGoal:
                    success = WarGoalSettlementRuntimeService
                        .TryExecuteImmediate(war, out result);
                    break;
                default:
                    return;
            }

            if (success || !IsLiveWar(war))
            {
                FailureCounts.Remove(pWarId);
                return;
            }
            RecordFailure(pWarId, result.Reason);
        }

        private static bool TryReadDecision(War pWar,
            out WarTerminalSettlementDecision pDecision,
            out WarForceEliminationDecision pForceDecision)
        {
            pDecision = default;
            pForceDecision = default;
            if (!IsLiveWar(pWar)) return false;
            bool guarded = ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar) ||
                RebellionDirectTerritoryTransferService
                    .BlocksOrdinarySettlement(pWar);
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot)) return false;

            // A decisive score is already an authoritative terminal state.
            // Do not gate its forced maximum-benefit peace on the separate
            // military-potential probe, which can be temporarily unavailable
            // while armies are being rebuilt or a war participant is changing.
            if (!guarded && WarTerminalSettlementRules.IsDecisiveScore(
                    snapshot.Score))
            {
                pDecision = WarTerminalSettlementRules.Resolve(
                    new WarTerminalSettlementFacts(false, snapshot.Score,
                        1, 1, false));
                return true;
            }

            if (!WarForceEliminationSettlementService.TryReadPotentials(
                    pWar, out int attackers, out int defenders)) return false;
            bool affordableGoal = !guarded &&
                WarGoalSettlementRuntimeService.HasAffordableGoal(pWar);
            pDecision = WarTerminalSettlementRules.Resolve(
                new WarTerminalSettlementFacts(guarded, snapshot.Score,
                    attackers, defenders, affordableGoal));
            if (!pDecision.IsTerminal) return false;
            if (pDecision.Reason ==
                    WarTerminalSettlementReason.ForceElimination ||
                pDecision.Reason ==
                    WarTerminalSettlementReason.MutualElimination ||
                pDecision.Reason == WarTerminalSettlementReason.WhitePeace)
                return WarForceEliminationSettlementService
                    .TryGetConfirmedDecision(pWar, out pForceDecision);
            return true;
        }

        private static void RecordFailure(long pWarId, string pReason)
        {
            FailureCounts.TryGetValue(pWarId, out int attempts);
            attempts = Math.Min(int.MaxValue, attempts + 1);
            FailureCounts[pWarId] = attempts;
            ModClass.LogError("Terminal war settlement failed war=" +
                              pWarId + " attempt=" + attempts +
                              " reason=" +
                              (string.IsNullOrEmpty(pReason)
                                  ? "unknown"
                                  : pReason));
        }

        private static bool IsLiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static Kingdom MainAttacker(War pWar)
        {
            try { return pWar?.getMainAttacker(); }
            catch { return null; }
        }
    }
}
