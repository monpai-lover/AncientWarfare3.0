using System;
using AncientWarfare3.core.policy;

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
            int militaryObligation, CentralizationEffects effects,
            int institutionAutonomyCapReduction = 0,
            int institutionTributeRateBonus = 0,
            bool applyRealmModifiers = true)
        {
            if (!applyRealmModifiers)
                return new VassalEffectiveTerms(NormalizePercent(autonomy),
                    NormalizePercent(tributeRate),
                    NormalizePercent(militaryObligation));
            int autonomyCap = NormalizePercent(effects.AutonomyCap -
                Math.Max(0, institutionAutonomyCapReduction));
            return new VassalEffectiveTerms(
                Math.Min(NormalizePercent(autonomy), autonomyCap),
                NormalizePercent(tributeRate + effects.TributeRateBonus +
                                 institutionTributeRateBonus),
                NormalizePercent(militaryObligation + effects.MilitaryObligationBonus));
        }

        public static float PoliticalTribute(float annualTax, int tributeRate,
            float vassalBalance, float suzerainBalance, float maximumBalance)
        {
            float source = NonNegative(vassalBalance);
            float transferable = Math.Max(0f,
                source - PoliticalPointSpendingRules.CourtReserve);
            float capacity = Math.Max(0f, NonNegative(maximumBalance) - NonNegative(suzerainBalance));
            float theoretical = ForecastPoliticalTribute(annualTax,
                tributeRate);
            return Math.Min(Math.Min(Math.Min(theoretical, PoliticalTributeCap), transferable), capacity);
        }

        public static float ForecastPoliticalTribute(float annualTax,
            int tributeRate)
        {
            float theoretical = NonNegative(annualTax) *
                                NormalizePercent(tributeRate) / 100f * 0.1f;
            return Math.Min(theoretical, PoliticalTributeCap);
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
