using System;

namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyBattleRules
    {
        public static int Roll(long worldSeed, long warId, long battleId,
            int round)
        {
            unchecked
            {
                ulong value = (ulong)worldSeed;
                value ^= (ulong)warId * 0x9E3779B185EBCA87UL;
                value ^= (ulong)battleId * 0xC2B2AE3D27D4EB4FUL;
                value ^= (ulong)(uint)round * 0x165667B19E3779F9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % 11UL);
            }
        }

        public static int ResolveFrontline(int totalStrength, int frontage)
        {
            return Math.Max(0, Math.Min(Math.Max(0, totalStrength),
                Math.Max(0, frontage)));
        }

        public static bool IsRout(double morale, double organization)
        {
            return morale <= 0.0 || organization <= 0.0;
        }

        public static int ApplyModifier(int baseStrength, int technology,
            int training, int equipment, double morale, double supply,
            int commanderBonus, int roll)
        {
            double quality = Math.Max(0, technology) * 0.08 +
                Math.Max(0, training) / 100.0 +
                Math.Max(0, equipment) * 0.05 +
                Math.Max(0, morale) * 0.25 +
                Math.Max(0, supply) * 0.15 +
                commanderBonus * 0.02 + roll * 0.03;
            return Math.Max(0, (int)Math.Round(Math.Max(0, baseStrength) *
                (1.0 + quality)));
        }
    }
}
