using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// Evaluates late-war military collapse after mobilization and routes it
    /// through the existing diplomacy path or the bounded total-war path.
    /// </summary>
    internal static class WarNoForceSurrenderService
    {
        private const string QueuePrefix = "war_no_force_surrender:";
        private const int MaximumAttempts = 2;
        private static readonly HashSet<long> Queued =
            new HashSet<long>();

        public static bool QueueIfReady(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded()) return false;
            if (!WarNoForceMilitaryService.TryGetOutcome(pWar,
                    out WarNoForceSideFacts attackers,
                    out WarNoForceSideFacts defenders)) return false;
            if (!TrySelectDefeatedSide(pWar, attackers, defenders,
                    out _, out _)) return false;
            long warId = pWar.data.id;
            if (!Queued.Add(warId)) return true;
            Enqueue(warId, 0);
            return true;
        }

        public static void ClearRuntime()
        {
            Queued.Clear();
            WarTotalWarSurrenderService.ClearRuntime();
        }

        private static void Enqueue(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => Process(pWarId, pAttempt));
        }

        private static void Process(long pWarId, int pAttempt)
        {
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            if (war?.data == null || war.hasEnded())
            {
                Queued.Remove(pWarId);
                return;
            }
            WarNoForceSideFacts attackers = WarNoForceMilitaryService.
                BuildSideFacts(war, pAttackers: true);
            WarNoForceSideFacts defenders = WarNoForceMilitaryService.
                BuildSideFacts(war, pAttackers: false);
            if (!TrySelectDefeatedSide(war, attackers, defenders,
                    out Kingdom defeated, out Kingdom winner))
            {
                Queued.Remove(pWarId);
                return;
            }

            bool completed;
            if (BlocksOrdinaryNegotiation(war))
            {
                completed = ZhuluPeaceGuard.BlocksOrdinarySettlement(war)
                    ? ZhuluWarSettlementService.ApplyNoForce(war, defeated,
                        winner)
                    : WarTotalWarSurrenderService.Apply(war, defeated,
                        winner);
            }
            else
            {
                completed = DiplomacyProposalService.
                    TryCreateNoForceSurrender(war, defeated, winner);
            }
            if (completed || war.hasEnded())
            {
                Queued.Remove(pWarId);
                return;
            }
            if (pAttempt < MaximumAttempts)
            {
                Enqueue(pWarId, pAttempt + 1);
                return;
            }
            Queued.Remove(pWarId);
            ModClass.LogWarning("No-force surrender could not be scheduled " +
                                "war=" + pWarId);
        }

        internal static bool BlocksOrdinaryNegotiation(War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return false;
            bool totalWar = false;
            try { totalWar = pWar.isTotalWar(); }
            catch { }
            return totalWar || DiplomacyProposalService.IsProtectedWar(pWar) ||
                   ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar) ||
                   RebellionDirectTerritoryTransferService.
                       BlocksOrdinarySettlement(pWar);
        }

        private static bool TrySelectDefeatedSide(War pWar,
            WarNoForceSideFacts pAttackers,
            WarNoForceSideFacts pDefenders,
            out Kingdom pDefeated, out Kingdom pWinner)
        {
            pDefeated = null;
            pWinner = null;
            if (pWar?.data == null || pWar.hasEnded()) return false;
            bool bothEmpty = !pAttackers.HasForce && !pDefenders.HasForce;
            int years = ReadWarYears(pWar);
            bool blocked = BlocksOrdinaryNegotiation(pWar);
            bool attackerLost = WarNoForceSurrenderRules.ShouldSurrender(
                years, sideNoForce: !pAttackers.HasForce,
                enemyHasForce: pDefenders.HasForce,
                ordinaryNegotiationBlocked: blocked,
                bothSidesNoForce: bothEmpty);
            bool defenderLost = WarNoForceSurrenderRules.ShouldSurrender(
                years, sideNoForce: !pDefenders.HasForce,
                enemyHasForce: pAttackers.HasForce,
                ordinaryNegotiationBlocked: blocked,
                bothSidesNoForce: bothEmpty);
            if (!attackerLost && !defenderLost) return false;
            try
            {
                pDefeated = attackerLost ? pWar.getMainAttacker() :
                    pWar.getMainDefender();
                pWinner = attackerLost ? pWar.getMainDefender() :
                    pWar.getMainAttacker();
            }
            catch { pDefeated = null; pWinner = null; }
            return pDefeated?.data != null && pWinner?.data != null;
        }

        private static int ReadWarYears(War pWar)
        {
            try
            {
                Kingdom perspective = pWar.getMainAttacker();
                if (perspective?.data != null &&
                    WarScoreService.TryGetSnapshot(pWar, perspective,
                        out WarScoreSnapshot snapshot))
                    return Math.Max(0, snapshot.DurationYears);
            }
            catch { }
            return 0;
        }
    }
}
