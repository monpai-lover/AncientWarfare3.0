using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarVictoryExhaustionRules
    {
        public const int MaximumReliefBank = 60;
        public const int AnnualReliefDecay = 5;
        public const int MinimumOccupationRelief = 4;
        public const int MaximumOccupationRelief = 12;
        public const int MinimumBattleRelief = 2;
        public const int MaximumBattleRelief = 8;
        public const int TerritorialGoalPreference = 10;

        public static int OccupationRelief(int pCityControlValue)
        {
            long value = Math.Max(0L, pCityControlValue);
            int relief = (int)Math.Min(int.MaxValue, (value + 1L) / 2L);
            return Clamp(relief, MinimumOccupationRelief,
                MaximumOccupationRelief);
        }

        public static int BattleRelief(int pIntensity)
        {
            int delta = WarScoreRules.BattleDelta(
                WarScoreSide.Attackers, pIntensity);
            return Clamp(Math.Abs(delta), MinimumBattleRelief,
                MaximumBattleRelief);
        }

        public static int AddRelief(int pCurrent, int pAward)
        {
            long total = Math.Max(0L, pCurrent) + Math.Max(0L, pAward);
            return (int)Math.Min(MaximumReliefBank, total);
        }

        public static int DecayRelief(int pCurrent, int pElapsedYears)
        {
            long current = Math.Max(0L,
                Math.Min(MaximumReliefBank, pCurrent));
            long decay = Math.Max(0L, pElapsedYears) * AnnualReliefDecay;
            return (int)Math.Max(0L, current - decay);
        }

        public static int ApplyRelief(int pBaseExhaustion, int pRelief)
        {
            return Math.Max(0, Math.Min(100, pBaseExhaustion) -
                               Math.Max(0, pRelief));
        }

        private static int Clamp(int pValue, int pMinimum, int pMaximum)
        {
            return Math.Max(pMinimum, Math.Min(pMaximum, pValue));
        }
    }
}
