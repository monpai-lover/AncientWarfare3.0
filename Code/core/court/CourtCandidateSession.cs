using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.court
{
    internal sealed class CourtCandidateSession
    {
        internal readonly IReadOnlyList<Actor> Actors;
        internal readonly HashSet<long> ReservedActorIds;
        private SchoolGuestOfficeService.VacancyCandidateSession
            _guestCandidates;

        internal CourtCandidateSession(Kingdom pKingdom)
        {
            Actors = OfficerCandidateCatalog.GetOrBuild(pKingdom,
                Date.getCurrentYear()).ToArray();
            ReservedActorIds = CourtService.
                BuildActiveOfficerActorSetForKingdom(pKingdom);
        }

        internal bool IsAvailable(Actor pActor, CourtVacancyKey pVacancy)
        {
            return pActor?.data != null &&
                   (!ReservedActorIds.Contains(pActor.data.id) ||
                    CourtService.IsExplicitConcurrentOffice(pActor,
                        pVacancy));
        }

        internal void Reserve(Actor pActor, CourtVacancyKey pVacancy)
        {
            if (pActor?.data != null &&
                !CourtService.IsExplicitConcurrentOffice(pActor, pVacancy))
                ReservedActorIds.Add(pActor.data.id);
        }

        internal SchoolGuestOfficeService.VacancyCandidateSession
            GuestCandidates(Kingdom pKingdom)
        {
            return _guestCandidates ??= SchoolGuestOfficeService.
                CreateVacancyCandidateSession(pKingdom);
        }
    }
}
