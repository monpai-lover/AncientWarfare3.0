using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal sealed class WesternCourtVacancy
    {
        public long KingdomId;
        public string OfficeId = "";
        public long FormerIncumbentActorId = -1L;
    }

    internal static class WesternCourtElectionService
    {
        private static readonly Queue<WesternCourtVacancy> VacancyQueue =
            new Queue<WesternCourtVacancy>();
        private static readonly HashSet<string> QueuedVacancies =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Reset()
        {
            VacancyQueue.Clear();
            QueuedVacancies.Clear();
        }

        public static void QueueKingdomVacancies(Kingdom pKingdom)
        {
            if (!CourtService.IsWesternElective(pKingdom)) return;
            foreach (string officeId in
                     CourtProfileRegistry.CentralOfficeIdsFor(pKingdom))
            {
                if (CourtService.TryOpenWesternElectiveVacancy(pKingdom,
                        officeId, out long formerIncumbentActorId))
                    EnqueueVacancy(pKingdom, officeId,
                        formerIncumbentActorId);
            }
        }

        public static void EnqueueVacancy(Kingdom pKingdom,
            string pOfficeId, long pFormerIncumbentActorId = -1L)
        {
            if (!CourtService.IsWesternElective(pKingdom) ||
                string.IsNullOrEmpty(pOfficeId) ||
                !CourtProfileRegistry.IsOfficeAvailableFor(pKingdom,
                    pOfficeId, CourtOfficeLayer.Central)) return;
            string key = VacancyKey(pKingdom.id, pOfficeId);
            if (!QueuedVacancies.Add(key)) return;
            VacancyQueue.Enqueue(new WesternCourtVacancy
            {
                KingdomId = pKingdom.id,
                OfficeId = pOfficeId,
                FormerIncumbentActorId = pFormerIncumbentActorId
            });
        }

        public static void ProcessAuthorityCycle()
        {
            var retry = new List<WesternCourtVacancy>(
                WesternCourtElectionRules.MaxVacanciesPerCycle);
            int processed = 0;
            while (processed <
                       WesternCourtElectionRules.MaxVacanciesPerCycle &&
                   VacancyQueue.Count > 0)
            {
                WesternCourtVacancy vacancy = VacancyQueue.Dequeue();
                QueuedVacancies.Remove(VacancyKey(vacancy.KingdomId,
                    vacancy.OfficeId));
                processed++;
                if (ShouldRetry(vacancy)) retry.Add(vacancy);
            }

            foreach (WesternCourtVacancy vacancy in retry)
            {
                Kingdom kingdom = World.world?.kingdoms?.get(
                    vacancy.KingdomId);
                EnqueueVacancy(kingdom, vacancy.OfficeId,
                    vacancy.FormerIncumbentActorId);
            }
        }

        private static bool ShouldRetry(WesternCourtVacancy pVacancy)
        {
            if (pVacancy == null) return false;
            Kingdom kingdom = World.world?.kingdoms?.get(pVacancy.KingdomId);
            if (!CourtService.IsWesternElective(kingdom) ||
                !CourtProfileRegistry.IsOfficeAvailableFor(kingdom,
                    pVacancy.OfficeId, CourtOfficeLayer.Central)) return false;
            if (!CourtService.IsWesternElectiveCentralOfficeVacant(kingdom,
                    pVacancy.OfficeId)) return false;

            List<WesternCourtElectionCandidate> candidates =
                CourtService.BuildWesternElectionCandidates(kingdom,
                    pVacancy.OfficeId,
                    pVacancy.FormerIncumbentActorId,
                    WesternCourtElectionRules.MaxCandidatesPerVacancy);
            WesternCourtElectionCandidate winner =
                WesternCourtElectionRules.SelectWinner(candidates);
            if (winner.ActorId < 0L) return true;
            Actor actor = World.world?.units?.get(winner.ActorId);
            if (CourtService.TryElectCentralOfficer(kingdom,
                    pVacancy.OfficeId, actor)) return false;
            return CourtService.IsWesternElectiveCentralOfficeVacant(kingdom,
                pVacancy.OfficeId);
        }

        private static string VacancyKey(long pKingdomId, string pOfficeId)
        {
            return pKingdomId + ":" + (pOfficeId ?? "");
        }
    }
}
