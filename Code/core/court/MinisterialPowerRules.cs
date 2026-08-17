using System;

namespace AncientWarfare3.core.court
{
    public static class MinisterialPowerRules
    {
        public const int NineBestowmentsThreshold = 80;
        public const int CoupPreparationYears = 3;
        public const int CoupCooldownYears = 20;

        public static bool ShouldLoadOfficers(bool hasOfficialCourt,
            bool republic, bool hasLivingKing)
        {
            return hasOfficialCourt && !republic && hasLivingKing;
        }

        public static readonly int[] Thresholds = { 20, 40, 60, 80, 90 };

        public static int OfficePriority(string pOfficeId)
        {
            switch (pOfficeId ?? "")
            {
                case CourtOfficeId.TaiZai: return 0;
                case CourtOfficeId.SiMa: return 1;
                case CourtOfficeId.SiTu: return 2;
                case CourtOfficeId.ZongBo: return 3;
                case CourtOfficeId.SiKou: return 4;
                case CourtOfficeId.SiKong: return 5;
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

        public static bool ShouldGrantNineBestowments(int previousPower,
            int nextPower, bool alreadyGranted)
        {
            return !alreadyGranted && CrossedThreshold(previousPower,
                nextPower, NineBestowmentsThreshold);
        }

        public static bool IsAmbitiousUsurper(bool ambitious, bool content,
            bool historicalFigure)
        {
            return historicalFigure || ambitious && !content;
        }

        public static bool CanPrepareCoup(bool monarchy, bool atWar,
            int power, bool weakRuler, bool ambitiousUsurper)
        {
            return monarchy && !atWar && ClampPower(power) >= 90 &&
                   weakRuler && ambitiousUsurper;
        }

        public static bool IsPuppetRuler(bool hasLivingRuler,
            bool weakRuler, int power, int preparationYears)
        {
            return hasLivingRuler && weakRuler && ClampPower(power) >= 95 &&
                   preparationYears >= CoupPreparationYears;
        }

        public static bool CanAttemptCoup(bool monarchy, bool atWar,
            bool puppetRuler, bool ambitiousUsurper,
            int yearsSinceLastAttempt)
        {
            return monarchy && !atWar && puppetRuler && ambitiousUsurper &&
                   yearsSinceLastAttempt >= CoupCooldownYears;
        }

        public static bool ShouldCoupSucceed(int pressure, int crisis,
            bool royalGuardPresent, bool eligibleHeirPresent,
            bool adultDirectSonPresent, int adultRoyalCount,
            bool strongRuler)
        {
            int boundedPressure = ClampPower(pressure);
            int boundedCrisis = Math.Max(0, Math.Min(100, crisis));
            if (boundedPressure < 95 || boundedCrisis < 60) return false;
            bool intactRoyalHouse = eligibleHeirPresent &&
                                    adultDirectSonPresent &&
                                    adultRoyalCount >= 2;
            if (intactRoyalHouse && boundedCrisis < 90) return false;
            if (intactRoyalHouse && (royalGuardPresent || strongRuler))
                return false;

            int attack = boundedPressure + Math.Min(50, boundedCrisis / 2);
            int defense = 85;
            if (royalGuardPresent) defense += 20;
            if (eligibleHeirPresent) defense += 12;
            if (adultDirectSonPresent) defense += 18;
            defense += Math.Min(15, Math.Max(0, adultRoyalCount) * 3);
            if (strongRuler) defense += 15;
            return attack >= defense;
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
