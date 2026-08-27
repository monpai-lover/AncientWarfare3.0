using System;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.court
{
    internal static class ManualLocalChiefAppointmentService
    {
        internal static bool TryAppoint(Kingdom pKingdom, City pCity,
            Actor pCandidate, Func<bool> pPersistAppointment)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pCity?.data == null || pCity.isRekt() ||
                pCity.kingdom != pKingdom || pCandidate?.data == null ||
                pCandidate.isRekt() || !pCandidate.isAlive() ||
                pPersistAppointment == null) return false;

            Actor formerLeader = pCity.leader;
            City formerCandidateCity = pCandidate.city;
            using (GovernorRotationRuntimeScope.Enter())
            {
                try
                {
                    pCandidate.joinCity(pCity);
                    pCity.setLeader(pCandidate, pNew: true);
                    bool nativeCityLeader = false;
                    try { nativeCityLeader = pCandidate.isCityLeader(); }
                    catch { }
                    if (!ReferenceEquals(pCity.leader, pCandidate) ||
                        !nativeCityLeader ||
                        !pPersistAppointment())
                    {
                        RestoreRuntimePlacement(pKingdom, pCity, pCandidate,
                            formerLeader, formerCandidateCity);
                        return false;
                    }

                    CityGovernorPlacementService.OnCommittedAssignment(
                        pCity, pCandidate);
                    CourtVacancyReconciliationService.RegisterCityVacancies(
                        pKingdom, pCity);
                    RegionalGovernmentAggregationService.Invalidate(pKingdom);
                    HierarchicalVassalMapModeService.MarkCityDirty(pCity);
                    return true;
                }
                catch (Exception error)
                {
                    ModClass.LogWarning(
                        "Manual local chief appointment failed: city=" +
                        pCity.data.id + " actor=" + pCandidate.data.id +
                        " error=" + error.Message);
                    RestoreRuntimePlacement(pKingdom, pCity, pCandidate,
                        formerLeader, formerCandidateCity);
                    return false;
                }
            }
        }

        private static void RestoreRuntimePlacement(Kingdom pKingdom,
            City pCity, Actor pCandidate, Actor pFormerLeader,
            City pFormerCandidateCity)
        {
            try
            {
                if (ReferenceEquals(pCity.leader, pCandidate))
                    pCity.removeLeader();
            }
            catch { }

            bool formerLeaderRestored = false;
            try
            {
                if (pFormerLeader?.data != null &&
                    !pFormerLeader.isRekt() && pFormerLeader.isAlive() &&
                    pFormerLeader.kingdom == pKingdom)
                {
                    if (pFormerLeader.city != pCity)
                        pFormerLeader.joinCity(pCity);
                    pCity.setLeader(pFormerLeader, pNew: false);
                    formerLeaderRestored = ReferenceEquals(pCity.leader,
                        pFormerLeader);
                }
            }
            catch { }

            try
            {
                City restoreCity = pFormerCandidateCity?.data != null &&
                                   !pFormerCandidateCity.isRekt()
                    ? pFormerCandidateCity
                    : null;
                if (!ReferenceEquals(pCandidate.city, restoreCity))
                    pCandidate.joinCity(restoreCity);
            }
            catch { }

            if (!formerLeaderRestored)
                CourtVacancyReconciliationService.RegisterCityVacancies(
                    pKingdom, pCity);
        }
    }
}
