using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class ExamCycleService
    {
        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.isNeutral()) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                out int anchorYear, -1);
            if (anchorYear < 1)
            {
                pKingdom.data.set(LineageKeys.CIVIL_SERVICE_EXAM_ANCHOR_YEAR,
                    ExamCycleRules.FirstOpeningYear(year));
                return;
            }
            if (!ExamCycleRules.IsCycleYear(year, anchorYear)) return;
            _ = ExamCycleRules.IdempotencyKey(pKingdom.id, year);
            CivilServiceExamService.OnKingdomYear(pKingdom);
            MilitaryExamService.OnKingdomYear(pKingdom);
        }
    }
}
