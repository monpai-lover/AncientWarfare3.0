using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarTerminalSettlementCoordinator
    {
        // Terminal wars must be noticed promptly, but the recovery scan must
        // remain bounded so a large world cannot turn this into a full-war
        // hot path.
        private const int WarsPerAuthorityCycle = 8;
        private const int RecoveryScanIntervalCycles = 4;
        private const string QueuePrefix = "war_terminal_settlement:";
        private static readonly Dictionary<long, int> FailureCounts =
            new Dictionary<long, int>();
        private static int _recoveryCursor;
        private static int _recoveryCyclesUntilScan;

        public static void ClearRuntime()
        {
            FailureCounts.Clear();
            _recoveryCursor = 0;
            _recoveryCyclesUntilScan = 0;
        }

        public static bool NotifyWarChanged(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !IsLiveWar(pWar)) return false;
            if (!ShouldQueue(pWar)) return false;
            long warId = pWar.data.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + warId, DeferredWorkClass.CriticalRuntime,
                () => Process(warId));
            return true;
        }

        public static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            WarManager wars = World.world?.wars;
            if (wars == null) return;
            if (_recoveryCyclesUntilScan > 0)
            {
                _recoveryCyclesUntilScan--;
                return;
            }
            _recoveryCyclesUntilScan = RecoveryScanIntervalCycles - 1;
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
            if (WarTerritoryService.HasOpenMandateConquestGoal(pWar))
                return false;
            if (PeasantRebelBanditSuppressionSettlementService.IsReady(pWar))
                return true;
            if (ZhuluWarService.IsZhuluWar(pWar)) return false;
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

            if (WarTerritoryService.HasOpenMandateConquestGoal(war))
                return;

            if (PeasantRebelBanditSuppressionSettlementService.IsReady(war))
            {
                if (PeasantRebelBanditSuppressionSettlementService.TryExecuteImmediate(war))
                    FailureCounts.Remove(pWarId);
                else
                    RecordFailure(pWarId,
                        "bandit_leadership_collapse_failed");
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
            // 天命战争只挡「记分板收局」这一路:决定性战功与战争目标兑现。
            // 全境占领 / 兵力被彻底消灭仍然照常结束 —— 否则天命战争
            // 永远打不完,拖着一堆军队反过来吃性能。
            bool scoreGuarded = guarded ||
                MandateWarPeaceGuard.BlocksScoreAndGoalSettlement(pWar);
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot)) return false;

            if (!guarded && WarForceEliminationSettlementService
                    .TryGetFullOccupationDecision(pWar,
                        out WarForceEliminationDecision fullOccupation))
            {
                pDecision = new WarTerminalSettlementDecision(
                    WarTerminalSettlementReason.ForceElimination,
                    fullOccupation.Beneficiary, 100);
                pForceDecision = fullOccupation;
                return true;
            }

            bool potentialRead = WarForceEliminationSettlementService
                .TryReadPotentials(pWar, out int attackers,
                    out int defenders);
            // A decisive score remains a safe fallback while the native
            // warrior counters are temporarily unavailable. If counters are
            // available, Resolve must see them so a stale +100/-100 score
            // cannot override a confirmed one-sided military collapse.
            if (!potentialRead)
            {
                if (scoreGuarded ||
                    !WarTerminalSettlementRules.IsDecisiveScore(
                        snapshot.Score)) return false;
                pDecision = WarTerminalSettlementRules.Resolve(
                    new WarTerminalSettlementFacts(false, snapshot.Score,
                        1, 1, false));
                return true;
            }
            if (!scoreGuarded && WarTerminalSettlementRules.IsDecisiveScore(
                    snapshot.Score))
            {
                pDecision = WarTerminalSettlementRules.Resolve(
                    new WarTerminalSettlementFacts(false, snapshot.Score,
                        attackers, defenders, false));
                return pDecision.IsTerminal;
            }
            bool affordableGoal = !scoreGuarded &&
                WarGoalSettlementRuntimeService.HasAffordableGoal(pWar);
            // 天命战争不整段禁掉收局(那会让它永远打不完),只把「记分板」这一项
            // 抹平:分数当 0 看,决定性战功与目标兑现都走不到,兵力被打光那条
            // 判定原样保留。
            pDecision = WarTerminalSettlementRules.Resolve(
                new WarTerminalSettlementFacts(guarded,
                    scoreGuarded ? 0 : snapshot.Score,
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
