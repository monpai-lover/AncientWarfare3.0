using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    internal sealed class WesternCourtVacancy
    {
        public long KingdomId;
        public string OfficeId = "";
        public string Layer = CourtOfficeLayer.Central;
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
            foreach (string layer in new[] { CourtOfficeLayer.Central,
                     CourtOfficeLayer.Military })
            foreach (string officeId in CourtProfileRegistry.OfficeIdsForLayer(
                         pKingdom, layer))
            {
                if (CourtService.TryOpenWesternElectiveVacancy(pKingdom,
                        officeId, out long formerIncumbentActorId, layer))
                    EnqueueVacancy(pKingdom, officeId,
                        formerIncumbentActorId, layer);
            }
        }

        public static void EnqueueVacancy(Kingdom pKingdom,
            string pOfficeId, long pFormerIncumbentActorId = -1L,
            string pLayer = CourtOfficeLayer.Central)
        {
            if (!CourtService.IsWesternElectiveOffice(pKingdom, pOfficeId,
                    pLayer)) return;
            string key = VacancyKey(pKingdom.id, pLayer, pOfficeId);
            if (!QueuedVacancies.Add(key)) return;
            VacancyQueue.Enqueue(new WesternCourtVacancy
            {
                KingdomId = pKingdom.id,
                OfficeId = pOfficeId,
                Layer = pLayer,
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
                    vacancy.Layer, vacancy.OfficeId));
                processed++;
                if (ShouldRetry(vacancy)) retry.Add(vacancy);
            }

            foreach (WesternCourtVacancy vacancy in retry)
            {
                Kingdom kingdom = World.world?.kingdoms?.get(
                    vacancy.KingdomId);
                EnqueueVacancy(kingdom, vacancy.OfficeId,
                    vacancy.FormerIncumbentActorId, vacancy.Layer);
            }
        }

        private static bool ShouldRetry(WesternCourtVacancy pVacancy)
        {
            if (pVacancy == null) return false;
            Kingdom kingdom = World.world?.kingdoms?.get(pVacancy.KingdomId);
            if (!CourtService.IsWesternElectiveOffice(kingdom,
                    pVacancy.OfficeId, pVacancy.Layer)) return false;
            if (!CourtService.IsWesternElectiveOfficeVacant(kingdom,
                    pVacancy.OfficeId, pVacancy.Layer)) return false;

            List<WesternCourtElectionCandidate> candidates =
                CourtService.BuildWesternElectionCandidates(kingdom,
                    pVacancy.OfficeId,
                    pVacancy.FormerIncumbentActorId,
                    WesternCourtElectionRules.MaxCandidatesPerVacancy,
                    pVacancy.Layer);
            WesternCourtElectionCandidate winner =
                WesternCourtElectionRules.SelectWinner(candidates);
            if (winner.ActorId < 0L) return true;
            Actor actor = World.world?.units?.get(winner.ActorId);
            if (CourtService.TryElectOfficer(kingdom, pVacancy.OfficeId,
                    actor, pVacancy.Layer)) return false;
            return CourtService.IsWesternElectiveOfficeVacant(kingdom,
                pVacancy.OfficeId, pVacancy.Layer);
        }

        private static string VacancyKey(long pKingdomId, string pLayer,
            string pOfficeId)
        {
            return pKingdomId + ":" + (pLayer ?? "") + ":" +
                (pOfficeId ?? "");
        }
    }
}
