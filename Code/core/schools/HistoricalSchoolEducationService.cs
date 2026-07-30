using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolEducationService
    {
        public static bool CanAppoint(Actor pActor, Kingdom pKingdom,
            string pLayer, string pOfficeId)
        {
            if (!HistoricalSchoolEducationRules.RequiresEducation(
                    pLayer, pOfficeId)) return true;
            if (pActor?.data == null) return false;
            if (IsIdentityExempt(pActor, pKingdom)) return true;
            return IsEducated(pActor, Date.getCurrentYear());
        }

        public static bool IsEducated(Actor pActor, int pCurrentYear)
        {
            if (pActor?.data == null) return false;
            bool canonical = HistoricalSchoolDescentService.
                IsCanonicalMaster(pActor);
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pActor.data.id);
            bool registered = membership != null &&
                              CourtSchoolRegistry.Find(
                                  membership.SchoolId) != null;
            return HistoricalSchoolEducationRules.IsEducated(
                canonical, activeMembership: membership?.Active == true,
                registeredSchool: registered,
                membershipStartYear: membership?.StartYear ?? -1,
                currentYear: pCurrentYear, pendingFailure: false);
        }

        private static bool IsIdentityExempt(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor.isKing()) return true;
            if (pKingdom?.data != null &&
                HeirService.PeekRegisteredHeir(pKingdom) == pActor)
                return true;
            return FeudatoryService.TryGetByPrince(pActor.data.id,
                out FeudatorySnapshot _);
        }
    }
}
