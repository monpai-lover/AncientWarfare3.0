using System;

namespace AncientWarfare3.core.grandstrategy
{
    public static class GrandStrategyTroopRules
    {
        public static bool IsUnlocked(GrandStrategyTroopType type, int technology)
        {
            if (technology < 0) return false;
            return type != GrandStrategyTroopType.Engineers || technology >= 2;
        }

        public static int TrainingCeiling(GrandStrategyTroopType type, int technology)
        {
            if (!IsUnlocked(type, technology)) return 0;
            return Math.Min(100, 45 + Math.Max(0, technology) * 10 +
                (type == GrandStrategyTroopType.Cavalry ? 5 : 0));
        }

        public static GrandStrategyTroopComposition Compose(int manpower,
            int technology)
        {
            if (manpower < 0) throw new ArgumentOutOfRangeException(nameof(manpower));
            var result = new GrandStrategyTroopComposition();
            int engineers = IsUnlocked(GrandStrategyTroopType.Engineers, technology)
                ? manpower / 20 : 0;
            int cavalry = manpower / 10;
            int archers = manpower / 5;
            int spearmen = manpower / 5;
            int infantry = manpower - engineers - cavalry - archers - spearmen;
            result[GrandStrategyTroopType.Infantry] = infantry;
            result[GrandStrategyTroopType.Spearmen] = spearmen;
            result[GrandStrategyTroopType.Archers] = archers;
            result[GrandStrategyTroopType.Cavalry] = cavalry;
            result[GrandStrategyTroopType.Engineers] = engineers;
            return result;
        }

        public static int Frontage(int terrainWidth, int totalStrength)
        {
            return Math.Max(0, Math.Min(Math.Max(0, terrainWidth),
                Math.Max(0, totalStrength)));
        }
    }
}
