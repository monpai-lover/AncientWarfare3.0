using System;

namespace AncientWarfare3.core.lineage
{
    public enum WarForceEliminationDecisionKind
    {
        None = 0,
        AttackersSurrender = 1,
        DefendersSurrender = 2,
        ScoreSettlement = 3,
        WhitePeace = 4
    }

    public enum WarForceSettlementOfferMode
    {
        WhitePeace = 0,
        Surrender = 1,
        MaximumBenefit = 2
    }

    public enum WarForceSpecialSettlementKind
    {
        None = 0,
        Rebellion = 1
    }

    public readonly struct WarForceEliminationDecision
    {
        public WarForceEliminationDecision(
            WarForceEliminationDecisionKind pKind,
            WarScoreSide pBeneficiary, int pScore)
        {
            Kind = pKind;
            Beneficiary = pBeneficiary;
            Score = Math.Max(0, Math.Min(100, pScore));
        }

        public WarForceEliminationDecisionKind Kind { get; }
        public WarScoreSide Beneficiary { get; }
        public int Score { get; }
    }

    public sealed class WarForceObservationState
    {
        private int _lastMonthKey = int.MinValue;

        public int AttackerZeroStreak { get; private set; }
        public int DefenderZeroStreak { get; private set; }

        public bool Observe(int pMonthKey, int pAttackerPotential,
            int pDefenderPotential)
        {
            if (pMonthKey <= _lastMonthKey) return false;
            _lastMonthKey = pMonthKey;
            AttackerZeroStreak = WarForceEliminationRules.NextZeroStreak(
                pAttackerPotential, AttackerZeroStreak);
            DefenderZeroStreak = WarForceEliminationRules.NextZeroStreak(
                pDefenderPotential, DefenderZeroStreak);
            return true;
        }
    }

    public static class WarForceEliminationRules
    {
        public const int RequiredZeroObservations = 1;

        public static int NextZeroStreak(int pPotential,
            int pCurrentStreak)
        {
            if (pPotential != 0) return 0;
            return Math.Min(RequiredZeroObservations,
                Math.Max(0, pCurrentStreak) + 1);
        }

        public static int AddPotential(int pFirst, int pSecond)
        {
            long total = (long)Math.Max(0, pFirst) +
                         Math.Max(0, pSecond);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        public static WarForceSettlementOfferMode OfferMode(
            WarForceEliminationDecisionKind pKind)
        {
            switch (pKind)
            {
                case WarForceEliminationDecisionKind.AttackersSurrender:
                case WarForceEliminationDecisionKind.DefendersSurrender:
                    return WarForceSettlementOfferMode.Surrender;
                case WarForceEliminationDecisionKind.ScoreSettlement:
                    return WarForceSettlementOfferMode.MaximumBenefit;
                default:
                    return WarForceSettlementOfferMode.WhitePeace;
            }
        }

        public static WarForceSpecialSettlementKind SpecialKind(
            bool pIsRebellion)
        {
            if (pIsRebellion)
                return WarForceSpecialSettlementKind.Rebellion;
            return WarForceSpecialSettlementKind.None;
        }

        public static WarForceEliminationDecision Resolve(
            int pAttackerPotential, int pDefenderPotential,
            int pAttackerZeroStreak, int pDefenderZeroStreak,
            int pAttackerSignedScore)
        {
            bool attackersExhausted = pAttackerPotential == 0 &&
                pAttackerZeroStreak >= RequiredZeroObservations;
            bool defendersExhausted = pDefenderPotential == 0 &&
                pDefenderZeroStreak >= RequiredZeroObservations;
            if (!attackersExhausted && !defendersExhausted)
                return new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.None,
                    WarScoreSide.None, 0);
            if (attackersExhausted && !defendersExhausted)
                return new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.AttackersSurrender,
                    WarScoreSide.Defenders, 100);
            if (defendersExhausted && !attackersExhausted)
                return new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.DefendersSurrender,
                    WarScoreSide.Attackers, 100);
            if (pAttackerSignedScore == 0)
                return new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.WhitePeace,
                    WarScoreSide.None, 0);
            return new WarForceEliminationDecision(
                WarForceEliminationDecisionKind.ScoreSettlement,
                pAttackerSignedScore > 0
                    ? WarScoreSide.Attackers
                    : WarScoreSide.Defenders,
                Math.Abs(pAttackerSignedScore));
        }

        public static WarForceEliminationDecision ResolveFullOccupation(
            int pAttackerInitialCities, int pAttackerOccupiedCities,
            int pDefenderInitialCities, int pDefenderOccupiedCities)
        {
            bool attackersOccupied = pAttackerInitialCities > 0 &&
                pAttackerOccupiedCities >= pAttackerInitialCities;
            bool defendersOccupied = pDefenderInitialCities > 0 &&
                pDefenderOccupiedCities >= pDefenderInitialCities;
            if (attackersOccupied == defendersOccupied)
                return new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.None,
                    WarScoreSide.None, 0);
            return attackersOccupied
                ? new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.AttackersSurrender,
                    WarScoreSide.Defenders, 100)
                : new WarForceEliminationDecision(
                    WarForceEliminationDecisionKind.DefendersSurrender,
                    WarScoreSide.Attackers, 100);
        }
    }
}
