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
            // 目录本身已按「国家+年份」缓存,这里原本再 ToArray() 复制一份整表,
            // 等于每补一个座位就全量拷贝一次候选名单。Actors 已经是
            // IReadOnlyList,而三个消费点(LocalCourtAppointmentService 的 LINQ
            // 链、SelectCandidate 的 foreach、CourtService 里自己再 ToList 的那处)
            // 全是只读,所以直接引用缓存列表即可。
            Actors = OfficerCandidateCatalog.GetOrBuild(pKingdom,
                Date.getCurrentYear());
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
