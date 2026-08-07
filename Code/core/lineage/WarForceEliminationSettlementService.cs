using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class WarForceEliminationSettlementService
    {
        private const int WarsPerAuthorityCycle = 2;
        private const string QueuePrefix =
            "war_force_elimination_settlement:";
        private const int MaximumAttempts = 2;
        private static readonly MonthlyAuthorityWorkQueue<long> MonthlyWork =
            new MonthlyAuthorityWorkQueue<long>();
        private static readonly Dictionary<long, WarForceObservationState>
            Observations =
                new Dictionary<long, WarForceObservationState>();
        private static readonly HashSet<long> QueuedWarIds =
            new HashSet<long>();

        public static void ClearRuntime()
        {
            MonthlyWork.Clear();
            Observations.Clear();
            QueuedWarIds.Clear();
            WarForceSpecialSettlementService.ClearRuntime();
        }

        public static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                World.world?.wars == null) return;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            var liveWarIds = new List<long>();
            var liveSet = new HashSet<long>();
            try
            {
                foreach (War war in World.world.wars)
                {
                    if (!IsLiveWar(war)) continue;
                    liveWarIds.Add(war.data.id);
                    liveSet.Add(war.data.id);
                }
            }
            catch { return; }
            if (MonthlyWork.ScheduleMonth(monthKey, liveWarIds))
                RemoveEndedObservations(liveSet);
            MonthlyWork.Drain(WarsPerAuthorityCycle,
                (queuedMonth, warId) => ObserveAndQueue(
                    FindWar(warId), queuedMonth));
        }

        public static bool QueueIfReady(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !IsLiveWar(pWar)) return false;
            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            return ObserveAndQueue(pWar, monthKey);
        }

        private static bool ObserveAndQueue(War pWar, int pMonthKey)
        {
            if (ZhuluWarService.IsZhuluWar(pWar))
                return WarForceSpecialSettlementService.
                           TrySettleZhuluZeroForce(pWar) ==
                       WarForceSpecialSettlementResult.Handled;
            if (!ObserveWar(pWar, pMonthKey,
                    out WarForceEliminationDecision decision) ||
                WarPeaceSettlementService.Instance.
                    HasActionableSettlement(pWar.data.id)) return false;
            WarForceSpecialSettlementResult special =
                WarForceSpecialSettlementService.TrySettle(pWar, decision);
            if (special == WarForceSpecialSettlementResult.Handled)
                return true;
            if (special == WarForceSpecialSettlementResult.Failed ||
                !QueuedWarIds.Add(pWar.data.id)) return false;
            Enqueue(pWar.data.id, decision, 0);
            return true;
        }

        internal static bool TryGetConfirmedDecision(War pWar,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (!TryReadPotentials(pWar, out int attackers,
                    out int defenders) ||
                !Observations.TryGetValue(pWar.data.id,
                    out WarForceObservationState state) ||
                !TryReadScoreIfRequired(pWar, attackers, defenders, state,
                    out int score)) return false;
            pDecision = WarForceEliminationRules.Resolve(attackers,
                defenders, state.AttackerZeroStreak,
                state.DefenderZeroStreak, score);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        private static bool ObserveWar(War pWar, int pMonthKey,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (!TryReadPotentials(pWar, out int attackers,
                    out int defenders)) return false;
            if (!Observations.TryGetValue(pWar.data.id,
                    out WarForceObservationState state))
            {
                state = new WarForceObservationState();
                Observations[pWar.data.id] = state;
            }
            state.Observe(pMonthKey, attackers, defenders);
            if (!TryReadScoreIfRequired(pWar, attackers, defenders, state,
                    out int score)) return false;
            pDecision = WarForceEliminationRules.Resolve(attackers,
                defenders, state.AttackerZeroStreak,
                state.DefenderZeroStreak, score);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        private static bool TryReadScoreIfRequired(War pWar,
            int pAttackers, int pDefenders,
            WarForceObservationState pState, out int pScore)
        {
            pScore = 0;
            bool attackersExhausted = pAttackers == 0 &&
                pState.AttackerZeroStreak >=
                    WarForceEliminationRules.RequiredZeroObservations;
            bool defendersExhausted = pDefenders == 0 &&
                pState.DefenderZeroStreak >=
                    WarForceEliminationRules.RequiredZeroObservations;
            if (!attackersExhausted || !defendersExhausted) return true;
            return TryReadAttackerScore(pWar, out pScore);
        }

        internal static bool TryReadPotentials(War pWar,
            out int pAttackers, out int pDefenders)
        {
            pAttackers = 0;
            pDefenders = 0;
            if (!IsLiveWar(pWar) || MainAttacker(pWar)?.data == null ||
                MainDefender(pWar)?.data == null) return false;
            try
            {
                // The native War counters are the authoritative force facts
                // for elimination. AW3 reserve capacity is a mobilization
                // input, not an active force in the current war record.
                pAttackers = Math.Max(0, pWar.countAttackersWarriors());
                pDefenders = Math.Max(0, pWar.countDefendersWarriors());
                return true;
            }
            catch { return false; }
        }

        private static bool TryReadAttackerScore(War pWar, out int pScore)
        {
            pScore = 0;
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot)) return false;
            pScore = snapshot.Score;
            return true;
        }

        private static void RemoveEndedObservations(HashSet<long> pLive)
        {
            if (Observations.Count == 0) return;
            var stale = new List<long>();
            foreach (long warId in Observations.Keys)
                if (!pLive.Contains(warId)) stale.Add(warId);
            for (int i = 0; i < stale.Count; i++)
            {
                Observations.Remove(stale[i]);
                QueuedWarIds.Remove(stale[i]);
            }
        }

        private static void Enqueue(long pWarId,
            WarForceEliminationDecision pDecision, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => Process(pWarId, pDecision, pAttempt));
        }

        private static void Process(long pWarId,
            WarForceEliminationDecision pExpected, int pAttempt)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                QueuedWarIds.Remove(pWarId);
                return;
            }
            War war = FindWar(pWarId);
            if (!TryGetConfirmedDecision(war,
                    out WarForceEliminationDecision decision) ||
                !SameDecision(pExpected, decision))
            {
                QueuedWarIds.Remove(pWarId);
                return;
            }
            Kingdom attacker = MainAttacker(war);
            Kingdom defender = MainDefender(war);
            bool surrender = decision.Kind ==
                                 WarForceEliminationDecisionKind.
                                     AttackersSurrender ||
                             decision.Kind ==
                                 WarForceEliminationDecisionKind.
                                     DefendersSurrender;
            Kingdom winner = decision.Beneficiary == WarScoreSide.Attackers
                ? attacker
                : defender;
            Kingdom loser = winner == attacker ? defender : attacker;
            Kingdom requester = winner;
            Kingdom responder = loser;
            WarPeaceDefaultOfferMode mode = ToRuntimeOfferMode(decision.Kind);
            int signedScore = surrender
                ? WarPeaceTermsRules.MaximumWarScore
                : decision.Score;
            WarPeaceSettlementDraft draft = WarPeaceSettlementService.
                Instance.BuildDefaultDraft(war, requester, responder,
                    signedScore, mode);
            draft.PlayerInitiated = false;
            draft.AutomaticExhaustionSettlement = true;
            WarPeaceExecutionResult result = WarPeaceSettlementService.
                Instance.ForceMilitaryEliminationSettlement(draft,
                    decision);
            if (result.Success || !IsLiveWar(war))
            {
                QueuedWarIds.Remove(pWarId);
                Observations.Remove(pWarId);
                return;
            }
            if (pAttempt < MaximumAttempts)
            {
                Enqueue(pWarId, decision, pAttempt + 1);
                return;
            }
            QueuedWarIds.Remove(pWarId);
            ModClass.LogWarning("War force elimination settlement failed " +
                                "war=" + pWarId + " reason=" +
                                result.Reason);
        }

        private static WarPeaceDefaultOfferMode ToRuntimeOfferMode(
            WarForceEliminationDecisionKind pKind)
        {
            switch (WarForceEliminationRules.OfferMode(pKind))
            {
                case WarForceSettlementOfferMode.Surrender:
                    return WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit;
                case WarForceSettlementOfferMode.MaximumBenefit:
                    return WarPeaceDefaultOfferMode.
                        ExhaustionMaximumBenefit;
                default:
                    return WarPeaceDefaultOfferMode.WhitePeace;
            }
        }

        private static bool SameDecision(
            WarForceEliminationDecision pExpected,
            WarForceEliminationDecision pCurrent)
        {
            return pExpected.Kind == pCurrent.Kind &&
                   pExpected.Beneficiary == pCurrent.Beneficiary &&
                   pExpected.Score == pCurrent.Score;
        }

        private static bool IsLiveWar(War pWar)
        {
            try { return pWar?.data != null && !pWar.hasEnded(); }
            catch { return false; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
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
