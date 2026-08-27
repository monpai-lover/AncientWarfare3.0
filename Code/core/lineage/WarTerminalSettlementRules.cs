using System;

namespace AncientWarfare3.core.lineage
{
    public enum WarTerminalSettlementReason
    {
        None = 0,
        DecisiveScore = 1,
        ForceElimination = 2,
        AffordableGoal = 3,
        MutualElimination = 4,
        WhitePeace = 5
    }

    public readonly struct WarTerminalSettlementFacts
    {
        public WarTerminalSettlementFacts(bool pSpecialWarGuarded,
            int pAttackerSignedScore, int pAttackerPotential,
            int pDefenderPotential, bool pHasAffordableGoal)
        {
            SpecialWarGuarded = pSpecialWarGuarded;
            AttackerSignedScore = Math.Max(-100,
                Math.Min(100, pAttackerSignedScore));
            AttackerPotential = Math.Max(0, pAttackerPotential);
            DefenderPotential = Math.Max(0, pDefenderPotential);
            HasAffordableGoal = pHasAffordableGoal;
        }

        public bool SpecialWarGuarded { get; }
        public int AttackerSignedScore { get; }
        public int AttackerPotential { get; }
        public int DefenderPotential { get; }
        public bool HasAffordableGoal { get; }
    }

    public readonly struct WarTerminalSettlementDecision
    {
        public WarTerminalSettlementDecision(
            WarTerminalSettlementReason pReason,
            WarScoreSide pBeneficiary, int pBudget)
        {
            Reason = pReason;
            Beneficiary = pBeneficiary;
            Budget = Math.Max(0, Math.Min(100, pBudget));
        }

        public WarTerminalSettlementReason Reason { get; }
        public WarScoreSide Beneficiary { get; }
        public int Budget { get; }
        public bool IsTerminal => Reason != WarTerminalSettlementReason.None;
    }

    public static class WarTerminalSettlementRules
    {
        public static bool IsDecisiveScore(int pAttackerSignedScore)
        {
            return pAttackerSignedScore == WarScoreRules.MaximumScore ||
                   pAttackerSignedScore == -WarScoreRules.MaximumScore;
        }

        public static WarTerminalSettlementDecision Resolve(
            WarTerminalSettlementFacts pFacts)
        {
            if (pFacts.SpecialWarGuarded) return None();

            // A decisive score is latched by WarScoreService. Once the cap is
            // reached it is the authoritative terminal outcome; transient
            // warrior-counter reads must not downgrade it to elimination or
            // white peace while the settlement task is waiting to run.
            if (pFacts.AttackerSignedScore == 100)
                return Decision(WarTerminalSettlementReason.DecisiveScore,
                    WarScoreSide.Attackers, 100);
            if (pFacts.AttackerSignedScore == -100)
                return Decision(WarTerminalSettlementReason.DecisiveScore,
                    WarScoreSide.Defenders, 100);

            bool attackersEliminated = pFacts.AttackerPotential == 0;
            bool defendersEliminated = pFacts.DefenderPotential == 0;
            if (attackersEliminated != defendersEliminated)
                return Decision(
                    WarTerminalSettlementReason.ForceElimination,
                    attackersEliminated
                        ? WarScoreSide.Defenders
                        : WarScoreSide.Attackers,
                    100);

            if (pFacts.HasAffordableGoal)
                return Decision(WarTerminalSettlementReason.AffordableGoal,
                    WarScoreSide.Attackers,
                    Math.Max(0, pFacts.AttackerSignedScore));

            if (!attackersEliminated) return None();
            if (pFacts.AttackerSignedScore == 0)
                return Decision(WarTerminalSettlementReason.WhitePeace,
                    WarScoreSide.None, 0);
            return Decision(WarTerminalSettlementReason.MutualElimination,
                pFacts.AttackerSignedScore > 0
                    ? WarScoreSide.Attackers
                    : WarScoreSide.Defenders,
                Math.Abs(pFacts.AttackerSignedScore));
        }

        private static WarTerminalSettlementDecision Decision(
            WarTerminalSettlementReason pReason,
            WarScoreSide pBeneficiary, int pBudget)
        {
            return new WarTerminalSettlementDecision(pReason,
                pBeneficiary, pBudget);
        }

        private static WarTerminalSettlementDecision None()
        {
            return Decision(WarTerminalSettlementReason.None,
                WarScoreSide.None, 0);
        }
    }
}
