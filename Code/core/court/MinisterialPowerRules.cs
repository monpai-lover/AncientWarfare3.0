using System;

namespace AncientWarfare3.core.court
{
    public static class MinisterialPowerRules
    {
        public static readonly int[] Thresholds = { 20, 40, 60, 80, 90 };

        public static int OfficePriority(string pOfficeId)
        {
            switch (pOfficeId ?? "")
            {
                case "chancellor": return 0;
                case "marshal": return 1;
                case "censor": return 2;
                default: return int.MaxValue;
            }
        }

        public static int ClampPower(int pPower)
        {
            return Math.Max(0, Math.Min(100, pPower));
        }

        public static int AnnualDelta(int rank, float merit, int tenureYears,
            bool weakRuler, bool childOrOldRuler, bool lowMandate,
            bool royalGuardPresent)
        {
            int delta = 1;
            delta += Math.Min(5, Math.Max(0, rank - 8) / 2);
            delta += Math.Min(4, Math.Max(0, (int)Math.Floor(merit / 2f)));
            delta += Math.Min(6, Math.Max(0, tenureYears) / 3);
            if (weakRuler) delta += 8;
            if (childOrOldRuler) delta += 6;
            if (lowMandate) delta += 5;
            if (royalGuardPresent) delta -= 6;
            return Math.Max(-8, Math.Min(20, delta));
        }

        public static int NextPower(int pCurrentPower, int pDelta)
        {
            return ClampPower(ClampPower(pCurrentPower) + pDelta);
        }

        public static int DecayFormerPremier(int pCurrentPower)
        {
            return DecayFormerPremier(pCurrentPower, 1);
        }

        public static int DecayFormerPremier(int pCurrentPower, int pYears)
        {
            return ClampPower(pCurrentPower - Math.Max(0, pYears) * 8);
        }

        public static bool CrossedThreshold(int pPreviousPower, int pNextPower,
            int pThreshold)
        {
            return pPreviousPower < pThreshold && pNextPower >= pThreshold;
        }

        public static bool CanAttemptCoup(bool monarchy, bool atWar, int power,
            bool weakRuler, bool mandateCrisis)
        {
            return monarchy && !atWar && ClampPower(power) >= 90 &&
                   (weakRuler || mandateCrisis);
        }

        public static int HighestReachedThreshold(int pPower)
        {
            int power = ClampPower(pPower);
            int reached = 0;
            foreach (int threshold in Thresholds)
            {
                if (power < threshold) break;
                reached = threshold;
            }
            return reached;
        }

        public static float DirectionMultiplier(int pPower)
        {
            int stage = HighestReachedThreshold(pPower);
            if (stage >= 90) return 1.20f;
            if (stage >= 80) return 1.15f;
            if (stage >= 60) return 1.10f;
            if (stage >= 40) return 1.05f;
            return 1f;
        }

        public static int CompareCandidates(int leftPriority, int leftRank,
            float leftMerit, int leftAppointmentYear, long leftActorId,
            int rightPriority, int rightRank, float rightMerit,
            int rightAppointmentYear, long rightActorId)
        {
            int order = leftPriority.CompareTo(rightPriority);
            if (order != 0) return order;
            order = rightRank.CompareTo(leftRank);
            if (order != 0) return order;
            order = rightMerit.CompareTo(leftMerit);
            if (order != 0) return order;
            order = leftAppointmentYear.CompareTo(rightAppointmentYear);
            return order != 0 ? order : leftActorId.CompareTo(rightActorId);
        }
    }
}
