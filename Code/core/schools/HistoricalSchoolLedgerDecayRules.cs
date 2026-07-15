using System;

namespace AncientWarfare3.core.schools
{
    public readonly struct HistoricalSchoolEffectiveLedger
    {
        public HistoricalSchoolEffectiveLedger(double pTradition, double pMembership,
            double pInstitutions, double pActivePresence, double pMomentum,
            int pLastActiveYear, int pLastDecayYear)
        {
            Tradition = pTradition;
            Membership = pMembership;
            Institutions = pInstitutions;
            ActivePresence = pActivePresence;
            Momentum = pMomentum;
            LastActiveYear = pLastActiveYear;
            LastDecayYear = pLastDecayYear;
        }

        public double Tradition { get; }
        public double Membership { get; }
        public double Institutions { get; }
        public double ActivePresence { get; }
        public double Momentum { get; }
        public int LastActiveYear { get; }
        public int LastDecayYear { get; }
    }

    public static class HistoricalSchoolLedgerDecayRules
    {
        public const double TraditionDecay = 0.995d;
        public const double PresenceDecay = 0.97d;
        public const double MomentumDecay = 0.85d;
        public const int TraditionGraceYears = 3;

        public static HistoricalSchoolEffectiveLedger Effective(
            double pTradition, double pMembership, double pInstitutions,
            double pActivePresence, double pMomentum, int pLastActiveYear,
            int pLastDecayYear, int pCurrentYear)
        {
            int targetYear = Math.Max(pLastDecayYear, pCurrentYear);
            int elapsedYears = pLastDecayYear < 0
                ? 0
                : Math.Max(0, pCurrentYear - pLastDecayYear);
            int traditionYears = TraditionDecayYears(pLastActiveYear,
                pLastDecayYear, pCurrentYear);
            return new HistoricalSchoolEffectiveLedger(
                Decay01(pTradition, TraditionDecay, traditionYears),
                Clamp01(pMembership), NonNegative(pInstitutions),
                Decay01(pActivePresence, PresenceDecay, elapsedYears),
                Decay01(pMomentum, MomentumDecay, elapsedYears),
                pLastActiveYear, targetYear);
        }

        private static int TraditionDecayYears(int pLastActiveYear,
            int pLastDecayYear, int pCurrentYear)
        {
            if (pLastDecayYear < 0 || pCurrentYear <= pLastDecayYear) return 0;
            int firstAfterWatermark = pLastDecayYear == int.MaxValue
                ? int.MaxValue
                : pLastDecayYear + 1;
            int firstEligibleYear = pLastActiveYear < 0
                ? firstAfterWatermark
                : pLastActiveYear > int.MaxValue - TraditionGraceYears
                    ? int.MaxValue
                    : pLastActiveYear + TraditionGraceYears;
            int firstDecayYear = Math.Max(firstAfterWatermark, firstEligibleYear);
            return pCurrentYear < firstDecayYear
                ? 0
                : pCurrentYear - firstDecayYear + 1;
        }

        private static double Decay01(double pValue, double pRate, int pYears)
        {
            double value = Clamp01(pValue);
            return pYears <= 0 ? value : Clamp01(value * Math.Pow(pRate, pYears));
        }

        private static double Clamp01(double pValue)
        {
            if (double.IsNaN(pValue) || double.IsNegativeInfinity(pValue)) return 0d;
            if (double.IsPositiveInfinity(pValue)) return 1d;
            return Math.Max(0d, Math.Min(1d, pValue));
        }

        private static double NonNegative(double pValue)
        {
            return double.IsNaN(pValue) || double.IsNegativeInfinity(pValue)
                ? 0d
                : Math.Max(0d, pValue);
        }
    }
}
