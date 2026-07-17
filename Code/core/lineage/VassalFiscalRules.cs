using System;

namespace AncientWarfare3.core.lineage
{
    public readonly struct VassalEffectiveTerms
    {
        public VassalEffectiveTerms(int pAutonomy, int pTributeRate,
            int pMilitaryObligation)
        {
            Autonomy = pAutonomy;
            TributeRate = pTributeRate;
            MilitaryObligation = pMilitaryObligation;
        }

        public int Autonomy { get; }
        public int TributeRate { get; }
        public int MilitaryObligation { get; }
    }

    public static class VassalFiscalRules
    {
        public const float PoliticalTributeCap = 12f;
        public const float MaximumPoliticalBalance = 999f;
        public const int GoldTributeCap = 25;

        public static VassalEffectiveTerms EffectiveTerms(int autonomy, int tributeRate,
            int militaryObligation, CentralizationEffects effects)
        {
            return new VassalEffectiveTerms(
                Math.Min(NormalizePercent(autonomy), NormalizePercent(effects.AutonomyCap)),
                NormalizePercent(tributeRate + effects.TributeRateBonus),
                NormalizePercent(militaryObligation + effects.MilitaryObligationBonus));
        }

        public static float PoliticalTribute(float annualTax, int tributeRate,
            float vassalBalance, float suzerainBalance, float maximumBalance)
        {
            float tax = NonNegative(annualTax);
            float source = NonNegative(vassalBalance);
            float capacity = Math.Max(0f, NonNegative(maximumBalance) - NonNegative(suzerainBalance));
            float theoretical = tax * NormalizePercent(tributeRate) / 100f * 0.1f;
            return Math.Min(Math.Min(Math.Min(theoretical, PoliticalTributeCap), source), capacity);
        }

        public static int GoldTribute(float annualTax, int tributeRate, int availableGold)
        {
            int stock = Math.Max(0, availableGold);
            int theoretical = (int)Math.Floor(
                NonNegative(annualTax) * NormalizePercent(tributeRate) / 100f);
            return Math.Min(Math.Min(theoretical, GoldTributeCap), stock);
        }

        public static int NormalizePercent(int value)
        {
            return value < 0 ? 0 : value > 100 ? 100 : value;
        }

        private static float NonNegative(float value)
        {
            return float.IsNaN(value) || value < 0f ? 0f : value;
        }
    }
}
