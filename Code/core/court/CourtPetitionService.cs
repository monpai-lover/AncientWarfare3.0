using System;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal static class CourtPetitionService
    {
        public static bool TryPetition(Actor pActor,
            OfficialCareerStateView pState, Kingdom pKingdom, int pYear)
        {
            if (pActor?.data == null || pState == null ||
                pKingdom?.data == null || pKingdom.king?.data == null ||
                pActor == pKingdom.king || pActor.isKing() ||
                HeirService.IsCurrentHeir(pKingdom, pActor) ||
                FeudatoryService.IsActivePrince(pActor)) return false;

            pActor.data.get(LineageKeys.COURT_PETITION_LAST_YEAR,
                out int lastYear, -1);
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                out long projectedKingdomId, -1L);
            pActor.data.get(LineageKeys.COURT_OFFICE_ID,
                out string projectedOfficeId, "");
            int ambition = CourtPetitionRules.Ambition(
                pActor.hasTrait("ambitious"), pActor.hasTrait("content"),
                pActor.hasTrait("greedy"), pActor.hasTrait("deceitful"));
            bool activeOfficial = pState.KingdomId == pKingdom.id &&
                                  projectedKingdomId == pKingdom.id &&
                                  !string.IsNullOrEmpty(pState.OfficeId) &&
                                  pState.OfficeId == projectedOfficeId;
            if (!CourtPetitionRules.IsEligible(activeOfficial, pState.Rank,
                    ambition, pActor.money, pYear, lastYear)) return false;
            bool learnedEntry = !string.IsNullOrEmpty(
                SchoolMembershipService.GetSchool(pActor.data.id));
            if (!CourtPetitionRules.ShouldAttempt(pActor.data.id, pYear,
                    ambition, pState.Rank, learnedEntry)) return false;

            pActor.data.get(LineageKeys.COURT_PETITION_FAVOR,
                out float favor, 0f);
            pActor.data.get(LineageKeys.COURT_PETITION_UNTIL_YEAR,
                out int favorUntilYear, -1);
            float nextFavor = CourtPetitionRules.ApplyFavor(
                CourtPetitionRules.ActiveFavor(favor, favorUntilYear, pYear));
            pActor.spendMoney(CourtPetitionRules.MoneyCost);
            pKingdom.king.addMoney(CourtPetitionRules.MoneyCost);
            pActor.data.set(LineageKeys.COURT_PETITION_FAVOR, nextFavor);
            pActor.data.set(LineageKeys.COURT_PETITION_UNTIL_YEAR,
                pYear + CourtPetitionRules.FavorDurationYears);
            pActor.data.set(LineageKeys.COURT_PETITION_LAST_YEAR, pYear);
            ChronicleEvents.OnOfficialPetition(pKingdom, pActor,
                CourtPetitionRules.MoneyCost, nextFavor);
            return true;
        }

        public static float AppointmentFavor(Actor pActor, int pYear)
        {
            if (pActor?.data == null) return 0f;
            pActor.data.get(LineageKeys.COURT_PETITION_FAVOR,
                out float favor, 0f);
            pActor.data.get(LineageKeys.COURT_PETITION_UNTIL_YEAR,
                out int untilYear, -1);
            return CourtPetitionRules.ActiveFavor(favor, untilYear, pYear);
        }
    }
}
