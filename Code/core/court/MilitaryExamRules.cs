using System;

namespace AncientWarfare3.core.court
{
    public enum MilitaryExamStage
    {
        Scheduled = 0,
        Local = 1,
        Prefectural = 2,
        Metropolitan = 3,
        Palace = 4,
        Ranking = 5,
        Completed = 6
    }

    public static class MilitaryExamRules
    {
        public const float RetirementAge = 75f;
        public const int MinimumReserve = 4;

        public static bool IsEligibleCandidate(bool alive, bool adult,
            float age, bool isKing, bool isHeir, bool isPrince, bool isSlave,
            bool retired)
        {
            return alive && adult && IsServiceAgeAllowed(age) && !isKing &&
                   !isHeir && !isPrince && !isSlave && !retired;
        }

        public static bool IsServiceAgeAllowed(float age)
        {
            return !float.IsNaN(age) && !float.IsInfinity(age) && age >= 0f &&
                   age < RetirementAge;
        }

        public static int Score(int warfare, int damage, int strength, int speed,
            int diplomacy, int militaryMerit)
        {
            double total = Clamp(warfare) * .35d + Clamp(damage) * .20d +
                           Clamp(strength) * .15d + Clamp(speed) * .10d +
                           Clamp(diplomacy) * .10d + Clamp(militaryMerit) * .10d;
            return Math.Max(0, Math.Min(100,
                (int)Math.Round(total, MidpointRounding.AwayFromZero)));
        }

        public static string QualificationForStage(MilitaryExamStage stage,
            bool passed, bool imperialMode)
        {
            if (!passed) return string.Empty;
            switch (stage)
            {
                case MilitaryExamStage.Local:
                    return "wuxiucai";
                case MilitaryExamStage.Prefectural:
                case MilitaryExamStage.Metropolitan:
                    return "wujuren";
                case MilitaryExamStage.Palace:
                    return imperialMode ? "wujinshi" : "wujuren";
                default:
                    return string.Empty;
            }
        }

        public static int CandidateReserveTarget(int generalVacancies,
            int localVacancies, bool hasMilitaryOrganization,
            int minimumReserve = MinimumReserve)
        {
            if (!hasMilitaryOrganization) return 0;
            int vacancies = Math.Max(0, generalVacancies) +
                            Math.Max(0, localVacancies);
            int floor = Math.Max(0, minimumReserve);
            int halfRoundedUp = (vacancies + 1) / 2;
            return Math.Max(floor, halfRoundedUp);
        }

        private static int Clamp(int value)
        {
            return Math.Max(0, Math.Min(100, value));
        }
    }
}
