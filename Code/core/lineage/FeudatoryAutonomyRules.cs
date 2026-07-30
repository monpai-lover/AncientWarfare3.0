using System;

namespace AncientWarfare3.core.lineage
{
    public static class FeudatoryAutonomyRules
    {
        public const int LowAutonomyMaximum = 30;
        public const int HighAutonomyMinimum = 70;
        public const int LowMandateMaximumExclusive = 40;
        public const int HighMandateMinimum = 80;
        public const int MaximumGarrisonSize = 24;
        public const int MaximumGarrisonCandidateScan = 32;
        public const int MaximumGarrisonRecruitmentPerMaintenance = 4;
        public const int MaximumGarrisonDemobilizationPerMaintenance = 2;

        public static int ApplyCap(int autonomy, int autonomyCap)
        {
            int normalizedAutonomy = Math.Max(0, Math.Min(100, autonomy));
            int normalizedCap = Math.Max(0, Math.Min(100, autonomyCap));
            return Math.Min(normalizedAutonomy, normalizedCap);
        }

        public static int CalculateMaintenanceLoyaltyDelta(int autonomy,
            int mandateValue, int institutionLoyaltyBonus = 0)
        {
            int normalizedAutonomy = Math.Max(0, Math.Min(100, autonomy));
            int delta = normalizedAutonomy <= LowAutonomyMaximum ? 1 :
                normalizedAutonomy >= HighAutonomyMinimum ? -1 : 0;
            if (mandateValue >= HighMandateMinimum) delta++;
            else if (mandateValue < LowMandateMaximumExclusive) delta--;
            return delta + institutionLoyaltyBonus;
        }

        public static int ApplyMaintenanceLoyalty(int loyalty, int autonomy,
            int mandateValue, int institutionLoyaltyBonus = 0)
        {
            int normalized = Math.Max(0, Math.Min(100, loyalty));
            return Math.Max(0, Math.Min(100, normalized +
                CalculateMaintenanceLoyaltyDelta(autonomy, mandateValue,
                    institutionLoyaltyBonus)));
        }

        public static float CentralRemittanceMultiplier(int autonomy)
        {
            int normalized = Math.Max(0, Math.Min(100, autonomy));
            return 1f - normalized * 0.003f;
        }

        public static int GarrisonTarget(int totalWarriorSlots, int autonomy)
        {
            int slots = Math.Max(0, totalWarriorSlots);
            if (slots == 0) return 0;
            int normalized = Math.Max(0, Math.Min(100, autonomy));
            double rate = 0.10d + normalized * 0.002d;
            return Math.Min(MaximumGarrisonSize,
                Math.Max(1, (int)Math.Ceiling(slots * rate)));
        }

        public static int RecruitmentBatchSize(int currentSize,
            int targetSize)
        {
            return Math.Min(MaximumGarrisonRecruitmentPerMaintenance,
                Math.Max(0, targetSize - Math.Max(0, currentSize)));
        }

        public static int DemobilizationBatchSize(int currentSize,
            int targetSize)
        {
            return Math.Min(MaximumGarrisonDemobilizationPerMaintenance,
                Math.Max(0, Math.Max(0, currentSize) -
                            Math.Max(0, targetSize)));
        }
    }
}
