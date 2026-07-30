using System;

namespace AncientWarfare3.core.lineage
{
    public enum GeneralRebellionBranch
    {
        None,
        PalaceCoup,
        FiefIndependence,
        DirectMilitaryRebellion,
        DefectToNeighbor,
        SupportRestoration
    }

    public static class GeneralRebellionRules
    {
        public static int RulerWeaknessScore(bool hasLivingRuler,
            float rulerStatSum)
        {
            if (!hasLivingRuler) return 55;
            double boundedStats = Math.Max(0d, rulerStatSum);
            int score = (int)Math.Round(45d - boundedStats * 1.25d,
                MidpointRounding.AwayFromZero);
            return Math.Max(0, Math.Min(45, score));
        }

        public static int CalculateKingdomCrisis(int weakKingScore, bool childOrOldRuler,
            bool successionUnstable, bool recentWarDefeat, bool capitalThreatened,
            int nonCoreCityCount, int disloyalVassalCount, int mandateValue, bool hasRoyalGuard)
        {
            int risk = weakKingScore;
            if (childOrOldRuler) risk += 14;
            if (successionUnstable) risk += 12;
            if (recentWarDefeat) risk += 14;
            if (capitalThreatened) risk += 16;
            risk += nonCoreCityCount * 4;
            risk += disloyalVassalCount * 5;
            if (mandateValue < 20) risk += 10;
            if (hasRoyalGuard) risk -= 10;
            if (risk < 0) return 0;
            if (risk > 100) return 100;
            return risk;
        }

        public static GeneralRebellionBranch SelectBranch(int crisis, int personalRisk,
            bool hasFief, bool nearCapital, bool borderFief,
            bool strongNeighbor, bool hasRestorationClaim,
            bool canPalaceCoup = true)
        {
            int combined = (crisis + personalRisk) / 2;
            if (combined < 55) return GeneralRebellionBranch.None;
            if (hasRestorationClaim && crisis >= 60) return GeneralRebellionBranch.SupportRestoration;
            if (borderFief && strongNeighbor && personalRisk >= 75) return GeneralRebellionBranch.DefectToNeighbor;
            if (canPalaceCoup && nearCapital && personalRisk >= 80 &&
                crisis >= 35) return GeneralRebellionBranch.PalaceCoup;
            if (hasFief && crisis >= 45 && personalRisk >= 75) return GeneralRebellionBranch.FiefIndependence;
            if (personalRisk >= 88) return GeneralRebellionBranch.DirectMilitaryRebellion;
            return GeneralRebellionBranch.None;
        }
    }
}
