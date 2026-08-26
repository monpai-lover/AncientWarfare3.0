using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.lineage;
using UnityEngine;

namespace AncientWarfare3.core.court
{
    internal static class CourtVacancyReconciliationService
    {
        private sealed class RetryTicket
        {
            internal long KingdomId;
            internal int NotBeforeFrame;
        }

        private static readonly Dictionary<long, RetryTicket> RetryTickets =
            new Dictionary<long, RetryTicket>();

        internal static void RegisterVacancy(CourtVacancyKey pKey,
            int pMissingSeats = 1)
        {
            if (pKey.KingdomId < 0L || pMissingSeats <= 0) return;
            CourtVacancyRegistry.Register(pKey, pMissingSeats);
            Request(FindKingdom(pKey.KingdomId));
        }

        internal static void RegisterVacancy(OfficialCareerPrior pPrior)
        {
            if (pPrior == null || pPrior.KingdomId < 0L ||
                string.IsNullOrEmpty(pPrior.OfficeId)) return;
            bool local = pPrior.Layer == CourtOfficeLayer.City;
            if (!local && pPrior.Layer != CourtOfficeLayer.Central &&
                pPrior.Layer != CourtOfficeLayer.Military) return;
            City city = local && pPrior.CityId >= 0L
                ? World.world?.cities?.get(pPrior.CityId) : null;
            bool chief = local && city?.data != null &&
                CourtService.ResolveCityOffice(
                    World.world?.kingdoms?.get(pPrior.KingdomId), city) ==
                pPrior.OfficeId;
            RegisterVacancy(new CourtVacancyKey(pPrior.KingdomId,
                local ? pPrior.CityId : -1L, -1L, pPrior.Layer,
                pPrior.OfficeId, chief));
        }

        internal static void RegisterCityVacancies(Kingdom pKingdom,
            City pCity)
        {
            if (pKingdom?.data == null || pCity?.data == null ||
                pCity.isRekt() || pCity.kingdom != pKingdom) return;
            int capacity;
            try
            {
                capacity = CourtRules.CityOfficeSlots(
                    pCity.getPopulationPeople(), pCity.countZones(),
                    pKingdom.capital == pCity);
            }
            catch { capacity = 0; }
            IReadOnlyList<CourtVacancyKey> vacancies =
                LocalCourtAppointmentService.DiscoverVacancies(
                    pKingdom, pCity, capacity, Date.getCurrentYear());
            foreach (IGrouping<CourtVacancyKey, CourtVacancyKey> group in
                     vacancies.GroupBy(key => key))
                CourtVacancyRegistry.Register(group.Key, group.Count());
            if (vacancies.Count > 0) Request(pKingdom);
        }

        internal static void RefreshKingdomDefinitions(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            var centralActive = new HashSet<string>(
                CourtService.GetActiveOfficers(pKingdom, int.MaxValue)
                    .Where(row => row != null &&
                        (row.layer == CourtOfficeLayer.Central ||
                         row.layer == CourtOfficeLayer.Military))
                    .Select(row => row.office_id), StringComparer.Ordinal);
            foreach (string officeId in CourtService.
                         CentralOfficeIdsForCurrentProfile(pKingdom))
                if (!centralActive.Contains(officeId))
                    CourtVacancyRegistry.Register(new CourtVacancyKey(
                        pKingdom.id, -1L, -1L, CourtOfficeLayer.Central,
                        officeId));
            foreach (string officeId in CourtService.
                         MilitaryOfficeIdsForCurrentProfile(pKingdom))
                if (!centralActive.Contains(officeId))
                    CourtVacancyRegistry.Register(new CourtVacancyKey(
                        pKingdom.id, -1L, -1L, CourtOfficeLayer.Military,
                        officeId));
            try
            {
                foreach (City city in pKingdom.getCities())
                    RegisterCityVacancies(pKingdom, city);
            }
            catch { }
            Request(pKingdom);
        }

        internal static void CandidatePoolChanged(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                CourtVacancyRegistry.Snapshot(pKingdom.id).Count == 0) return;
            Request(pKingdom);
        }

        internal static void ActorLeftKingdom(Kingdom pPrevious)
        {
            RefreshKingdomDefinitions(pPrevious);
        }

        internal static void CityChangedKingdom(City pCity,
            Kingdom pPrevious, Kingdom pCurrent)
        {
            if (pPrevious?.data != null && pCity?.data != null)
                CourtVacancyRegistry.RemoveCity(pPrevious.id, pCity.data.id);
            if (pCurrent?.data != null && pCity?.data != null)
            {
                RegisterCityVacancies(pCurrent, pCity);
                Request(pCurrent);
            }
        }

        internal static void KingdomDestroyed(long pKingdomId)
        {
            CourtVacancyRegistry.RemoveKingdom(pKingdomId);
            RetryTickets.Remove(pKingdomId);
        }

        internal static void Request(Kingdom pKingdom, int pAttempt = 0)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            long kingdomId = pKingdom.id;
            DeferredRuntimeWorkService.EnqueueCoalesced(
                "court-vacancy:" + kingdomId, DeferredWorkClass.Runtime,
                () => ExecuteRequest(kingdomId, pAttempt));
        }

        internal static void DrainDueRetryTickets()
        {
            int frame = Time.frameCount;
            var due = RetryTickets.Values.Where(ticket =>
                ticket != null && ticket.NotBeforeFrame <= frame)
                .Select(ticket => ticket.KingdomId).ToArray();
            foreach (long kingdomId in due)
            {
                RetryTickets.Remove(kingdomId);
                Request(FindKingdom(kingdomId), 1);
            }
        }

        internal static void ClearRuntime()
        {
            CourtVacancyRegistry.ClearRuntime();
            RetryTickets.Clear();
        }

        private static void ExecuteRequest(long pKingdomId, int pAttempt)
        {
            try
            {
                Reconcile(FindKingdom(pKingdomId));
            }
            catch (Exception error)
            {
                if (CourtVacancyRules.ShouldRetry(
                        CourtVacancyOutcome.TechnicalFailure, pAttempt))
                {
                    RetryTickets[pKingdomId] = new RetryTicket
                    {
                        KingdomId = pKingdomId,
                        NotBeforeFrame = Time.frameCount + 1
                    };
                    return;
                }
                ModClass.LogError("Court vacancy reconciliation failed for " +
                    pKingdomId + ": " + error);
            }
        }

        private static void Reconcile(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return;
            IReadOnlyList<CourtVacancyEntry> entries =
                CourtVacancyRegistry.Snapshot(pKingdom.id);
            if (entries.Count == 0) return;
            var session = new CourtCandidateSession(pKingdom);
            var processed = new HashSet<CourtVacancyKey>();
            int validOfficeCount = entries.Sum(entry =>
                Math.Max(1, entry.MissingSeats));
            int processedSteps = 0;
            while (processedSteps < CourtVacancyRules.CascadeLimit(
                       validOfficeCount))
            {
                entries = CourtVacancyRegistry.Snapshot(pKingdom.id);
                CourtVacancyEntry? next = CourtVacancyCycleRules.Next(entries,
                    processed, processedSteps, validOfficeCount);
                if (!next.HasValue) break;
                CourtVacancyKey key = next.Value.Key;
                CourtVacancyOutcome outcome;
                if (key.Layer == CourtOfficeLayer.Central ||
                    key.Layer == CourtOfficeLayer.Military)
                {
                    outcome = CourtService.TryFillRegisteredCentralVacancy(
                        pKingdom, key, session);
                }
                else
                {
                    City city = World.world?.cities?.get(key.CityId);
                    outcome = LocalCourtAppointmentService.
                        TryFillRegisteredLocalVacancy(pKingdom, city, key,
                            session);
                }

                if (outcome == CourtVacancyOutcome.Filled)
                {
                    CourtVacancyEntry current = entries.First(entry =>
                        entry.Key.Equals(key));
                    CourtVacancyRegistry.Register(key,
                        current.MissingSeats - 1);
                    processedSteps++;
                    continue;
                }
                if (outcome == CourtVacancyOutcome.Invalid)
                    CourtVacancyRegistry.Remove(key);
                if (outcome == CourtVacancyOutcome.TechnicalFailure)
                {
                    if (CourtVacancyRules.ShouldRetry(outcome, 0))
                        RetryTickets[pKingdom.id] = new RetryTicket
                        {
                            KingdomId = pKingdom.id,
                            NotBeforeFrame = Time.frameCount + 1
                        };
                    else
                        ModClass.LogError("Court vacancy technical failure: " +
                            pKingdom.id + ":" + key.OfficeId);
                }
                processed.Add(key);
            }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
