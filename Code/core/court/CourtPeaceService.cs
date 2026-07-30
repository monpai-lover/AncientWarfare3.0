using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtPeaceService
    {
        public static void ClearRuntime()
        {
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            DiplomacyProposalService.TryScheduleWarPeace(pKingdom);
        }

    }
}
