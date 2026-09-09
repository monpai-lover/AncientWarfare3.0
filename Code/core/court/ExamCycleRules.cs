using System;

namespace AncientWarfare3.core.court
{
    public static class ExamCycleRules
    {
        public static int FirstOpeningYear(int completionYear)
        {
            return completionYear == int.MaxValue
                ? int.MaxValue
                : completionYear + 1;
        }

        public static bool IsCycleYear(int year, int anchorYear)
        {
            return anchorYear >= 0 && year >= anchorYear &&
                   (year - anchorYear) % 3 == 0;
        }

        public static string ResolveMode(bool hasMandate,
            bool hasEmpireTitle)
        {
            return hasMandate || hasEmpireTitle
                ? "imperial_exam"
                : "tribute_exam";
        }

        public static string IdempotencyKey(long kingdomId, int cycleYear)
        {
            return kingdomId.ToString() + ":" + cycleYear.ToString();
        }
    }
}
