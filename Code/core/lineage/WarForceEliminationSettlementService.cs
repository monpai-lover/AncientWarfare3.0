using System;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class WarForceEliminationSettlementService
    {
        public static void ClearRuntime()
        {
            WarForceSpecialSettlementService.ClearRuntime();
        }

        public static void ProcessAuthorityCycle()
        {
            WarTerminalSettlementCoordinator.ProcessAuthorityCycle();
        }

        public static bool QueueIfReady(War pWar)
        {
            if (!TryGetConfirmedDecision(pWar, out _)) return false;
            return WarTerminalSettlementCoordinator.NotifyWarChanged(pWar);
        }

        internal static bool TryGetConfirmedDecision(War pWar,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (TryGetFullOccupationDecision(pWar, out pDecision))
                return true;
            if (!TryReadPotentials(pWar, out int attackers,
                    out int defenders)) return false;
            int score = 0;
            if (attackers == 0 && defenders == 0 &&
                !TryReadAttackerScore(pWar, out score)) return false;
            pDecision = WarForceEliminationRules.Resolve(attackers,
                defenders, attackers == 0 ? 1 : 0,
                defenders == 0 ? 1 : 0, score);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        internal static bool TryGetFullOccupationDecision(War pWar,
            out WarForceEliminationDecision pDecision)
        {
            pDecision = default;
            if (!IsLiveWar(pWar)) return false;
            Kingdom attacker = MainAttacker(pWar);
            Kingdom defender = MainDefender(pWar);
            if (attacker?.data == null || defender?.data == null)
                return false;
            int attackerBaseline = WarParticipantCityBaselineService.
                GetOrRegister(pWar, attacker);
            int defenderBaseline = WarParticipantCityBaselineService.
                GetOrRegister(pWar, defender);
            int attackerInitial = ReadLiveCityCount(attacker,
                attackerBaseline);
            int defenderInitial = ReadLiveCityCount(defender,
                defenderBaseline);
            int attackerOccupied = WarScoreService.
                CountFrozenOccupationsForHomeKingdom(pWar.data.id,
                    attacker.id);
            int defenderOccupied = WarScoreService.
                CountFrozenOccupationsForHomeKingdom(pWar.data.id,
                    defender.id);
            pDecision = WarForceEliminationRules.ResolveFullOccupation(
                attackerInitial, attackerOccupied, defenderInitial,
                defenderOccupied);
            return pDecision.Kind != WarForceEliminationDecisionKind.None;
        }

        private static int ReadLiveCityCount(Kingdom pKingdom,
            int pFallback)
        {
            try
            {
                if (pKingdom?.cities != null)
                    return Math.Max(0, pKingdom.cities.Count);
            }
            catch { }
            return Math.Max(0, pFallback);
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
                pAttackers = Math.Max(0, pWar.countAttackersWarriors());
                pDefenders = Math.Max(0, pWar.countDefendersWarriors());
                if (!TryAddSpecialGovernmentCombatants(pWar,
                        ref pAttackers, ref pDefenders)) return false;
                return true;
            }
            catch { return false; }
        }

        private static bool TryAddSpecialGovernmentCombatants(War pWar,
            ref int pAttackers, ref int pDefenders)
        {
            if (!SpecialGovernmentWarParticipationService
                    .TryAddSpecialGovernmentCombatants(pWar,
                        out int extraAttackers, out int extraDefenders))
                return false;
            pAttackers = WarForceEliminationRules.AddPotential(
                pAttackers, extraAttackers);
            pDefenders = WarForceEliminationRules.AddPotential(
                pDefenders, extraDefenders);
            return true;
        }

        internal static bool TryExecuteImmediate(War pWar,
            WarForceEliminationDecision pDecision,
            out WarPeaceExecutionResult pResult)
        {
            pResult = new WarPeaceExecutionResult(false, -1,
                "military_elimination_state_changed");
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !TryGetConfirmedDecision(pWar,
                    out WarForceEliminationDecision current) ||
                !SameDecision(pDecision, current)) return false;

            WarForceSpecialSettlementResult special =
                WarForceSpecialSettlementService.TrySettle(pWar, current);
            if (special == WarForceSpecialSettlementResult.Handled)
            {
                pResult = new WarPeaceExecutionResult(true, -1, "");
                return true;
            }
            if (special == WarForceSpecialSettlementResult.Failed)
            {
                pResult = new WarPeaceExecutionResult(false, -1,
                    "special_force_settlement_failed");
                return false;
            }

            Kingdom attacker = MainAttacker(pWar);
            Kingdom defender = MainDefender(pWar);
            Kingdom winner = current.Beneficiary == WarScoreSide.Attackers
                ? attacker
                : defender;
            Kingdom loser = winner == attacker ? defender : attacker;
            if (winner?.data == null || loser?.data == null) return false;

            WarPeaceDefaultOfferMode mode = current.Kind ==
                WarForceEliminationDecisionKind.WhitePeace
                    ? WarPeaceDefaultOfferMode.WhitePeace
                    : WarPeaceDefaultOfferMode.ExhaustionMaximumBenefit;
            int budget = current.Kind ==
                         WarForceEliminationDecisionKind.AttackersSurrender ||
                         current.Kind ==
                         WarForceEliminationDecisionKind.DefendersSurrender
                ? WarPeaceTermsRules.MaximumWarScore
                : current.Score;
            Kingdom requester = winner ?? attacker;
            Kingdom responder = winner == null ? defender : loser;
            WarPeaceSettlementDraft draft = WarPeaceSettlementService
                .Instance.BuildDefaultDraft(pWar, requester, responder,
                    budget, mode);
            draft.PlayerInitiated = false;
            draft.AutomaticExhaustionSettlement = true;
            pResult = WarPeaceSettlementService.Instance
                .ForceMilitaryEliminationSettlement(draft, current);
            return pResult.Success;
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
