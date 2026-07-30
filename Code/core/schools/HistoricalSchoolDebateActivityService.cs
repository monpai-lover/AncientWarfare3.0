using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal readonly struct HistoricalSchoolDebateActorSeed
    {
        public HistoricalSchoolDebateActorSeed(long pActorId, int pDirectDiscipleCount)
        {
            ActorId = pActorId;
            DirectDiscipleCount = Math.Max(0, pDirectDiscipleCount);
        }

        public long ActorId { get; }
        public int DirectDiscipleCount { get; }
    }

    internal sealed class HistoricalSchoolDebateCityRequest
    {
        public HistoricalSchoolDebateCityRequest(long pCityId, int pYear,
            IEnumerable<HistoricalSchoolDebateActorSeed> pActors)
        {
            CityId = pCityId;
            Year = pYear;
            Actors = (pActors ?? Array.Empty<HistoricalSchoolDebateActorSeed>())
                .Where(p => p.ActorId >= 0)
                .OrderBy(p => p.ActorId)
                .ToArray();
            OperationKey = "school-debate:" + Year + ":" + CityId;
        }

        public long CityId { get; }
        public int Year { get; }
        public HistoricalSchoolDebateActorSeed[] Actors { get; }
        public string OperationKey { get; }
    }

    internal sealed class HistoricalSchoolDebateActivity
    {
        public HistoricalSchoolDebateActivity(HistoricalSchoolDebateRecord pRecord,
            HistoricalSchoolLedgerDelta pFirstDelta,
            HistoricalSchoolLedgerDelta pSecondDelta)
        {
            Record = pRecord;
            FirstDelta = pFirstDelta;
            SecondDelta = pSecondDelta;
            OperationKey = "school-debate:" + Record.DebateYear + ":" + Record.CityId;
        }

        public HistoricalSchoolDebateRecord Record { get; }
        public HistoricalSchoolLedgerDelta FirstDelta { get; }
        public HistoricalSchoolLedgerDelta SecondDelta { get; }
        public HistoricalSchoolVenueClaim Venue { get; set; }
        public bool FirstReady { get; set; }
        public bool SecondReady { get; set; }
        public bool ReadyQueued { get; set; }
        public bool PersistenceQueued { get; set; }
        public long StartFrame { get; set; }
        public string OperationKey { get; }
    }

    internal static class HistoricalSchoolDebateActivityService
    {
        private const int MaxQueuedPerYear = HistoricalSchoolDebateService.MaxDebatesPerYear;
        private const int MaxRetainedDebates = MaxQueuedPerYear * 2;
        private const int MaxConcurrentDebates = 4;
        private const long TaskLeaseFrames = 600L;
        private static readonly Queue<HistoricalSchoolDebateCityRequest> PendingCities =
            new Queue<HistoricalSchoolDebateCityRequest>();
        private static readonly Dictionary<long, HistoricalSchoolDebateActivity> ByActor =
            new Dictionary<long, HistoricalSchoolDebateActivity>();
        private static readonly Dictionary<string, HistoricalSchoolDebateActivity>
            ActivitiesById =
                new Dictionary<string, HistoricalSchoolDebateActivity>(StringComparer.Ordinal);
        private static readonly Queue<string> ReadyActivityIds = new Queue<string>();
        private static readonly Queue<string> ValidationActivityIds = new Queue<string>();
        private static readonly Dictionary<int, int> QueuedDebatesByYear =
            new Dictionary<int, int>();
        private static readonly HistoricalSchoolBoundedYearKeys OperationKeys =
            new HistoricalSchoolBoundedYearKeys();
        private static readonly HistoricalSchoolBoundedYearKeys UsedActorYears =
            new HistoricalSchoolBoundedYearKeys();

        public static bool TryEnqueueCity(long pCityId, int pYear,
            IEnumerable<HistoricalSchoolDebateActorSeed> pActors)
        {
            var request = new HistoricalSchoolDebateCityRequest(pCityId, pYear, pActors);
            if (!HistoricalSchoolActivityQueueRules.CanEnqueueTotal(
                    PendingCities.Count + ActivitiesById.Count,
                    MaxRetainedDebates)) return false;
            QueuedDebatesByYear.TryGetValue(pYear, out int yearCount);
            if (request.CityId < 0 || request.Year < 0 || request.Actors.Length < 2 ||
                !HistoricalSchoolActivityQueueRules.CanEnqueue(yearCount,
                    MaxQueuedPerYear,
                    OperationKeys.Contains(request.Year, request.OperationKey))) return false;
            if (!OperationKeys.Add(request.Year, request.OperationKey)) return false;
            PendingCities.Enqueue(request);
            QueuedDebatesByYear[pYear] = yearCount + 1;
            return true;
        }

        public static bool ProcessFrame()
        {
            if (TryProcessValidation()) return true;
            if (TryProcessReady()) return true;

            if (!HistoricalSchoolActivityQueueRules.CanActivate(ActivitiesById.Count,
                    MaxConcurrentDebates)) return false;

            while (PendingCities.Count > 0)
            {
                HistoricalSchoolDebateCityRequest request = PendingCities.Dequeue();
                var availableActors = new List<HistoricalSchoolDebateActorSeed>(
                    request.Actors.Length);
                foreach (HistoricalSchoolDebateActorSeed actor in request.Actors)
                    if (!ByActor.ContainsKey(actor.ActorId) &&
                        !UsedActorYears.Contains(request.Year,
                            actor.ActorId.ToString(
                                System.Globalization.CultureInfo.InvariantCulture)))
                        availableActors.Add(actor);
                long createDiagnostic = RuntimePerformanceDiagnostic.BeginScope();
                bool created;
                HistoricalSchoolDebateActivity activity;
                try
                {
                    created = HistoricalSchoolDebateService.TryCreateQueuedDebate(
                        request.CityId, request.Year, availableActors,
                        out activity);
                }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "school_debate_create", createDiagnostic);
                }
                if (!created)
                {
                    DecrementYearCount(request.Year);
                    return true;
                }
                City city = FindCity(request.CityId);
                Actor first = FindActor(activity.Record.FirstActorId);
                Actor second = FindActor(activity.Record.SecondActorId);
                if (city?.data == null || city.isRekt() ||
                    !IsUsable(first) || !IsUsable(second) ||
                    !HistoricalSchoolVenueService.TryClaimDebate(city, first,
                        activity.Record.FirstSchoolId, activity.OperationKey,
                        out HistoricalSchoolVenueClaim venue))
                {
                    DecrementYearCount(request.Year);
                    return true;
                }
                activity.Venue = venue;
                activity.StartFrame = HistoricalSchoolActivityQueue.CurrentFrame;
                ActivitiesById[activity.OperationKey] = activity;
                ByActor[activity.Record.FirstActorId] = activity;
                ByActor[activity.Record.SecondActorId] = activity;
                ValidationActivityIds.Enqueue(activity.OperationKey);
                UsedActorYears.Add(activity.Record.DebateYear,
                    activity.Record.FirstActorId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                UsedActorYears.Add(activity.Record.DebateYear,
                    activity.Record.SecondActorId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                long frame = HistoricalSchoolActivityQueue.CurrentFrame;
                bool firstScheduled = HistoricalSchoolTaskLeaseService.TrySchedule(
                    first,
                    activity.OperationKey,
                    HistoricalSchoolContent.DebateTravelTaskId,
                    activity.Record.FirstSchoolId,
                    activity.Record.CityId,
                    activity.OperationKey,
                    venue.Primary,
                    frame,
                    frame + TaskLeaseFrames);
                bool secondScheduled = firstScheduled &&
                    HistoricalSchoolTaskLeaseService.TrySchedule(
                        second,
                        activity.OperationKey,
                        HistoricalSchoolContent.DebateReceivingTaskId,
                        activity.Record.SecondSchoolId,
                        activity.Record.CityId,
                        activity.OperationKey,
                        venue.Secondary,
                        frame,
                        frame + TaskLeaseFrames);
                if (!secondScheduled)
                {
                    Finish(activity);
                }
                return true;
            }
            return false;
        }

        internal static bool FlushPendingPersistenceForSave()
        {
            bool resolved = true;
            int readyBudget = ReadyActivityIds.Count;
            while (readyBudget-- > 0 && ReadyActivityIds.Count > 0)
            {
                string activityId = ReadyActivityIds.Dequeue();
                if (!ActivitiesById.TryGetValue(activityId,
                        out HistoricalSchoolDebateActivity activity) ||
                    !activity.FirstReady || !activity.SecondReady) continue;
                activity.ReadyQueued = false;
                if (!QueueReadyDebate(activity)) resolved = false;
            }
            return resolved;
        }

        public static bool TryPrepareActor(Actor pActor, out Building pAcademy)
        {
            pAcademy = null;
            if (pActor?.data == null || !ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolDebateActivity activity) || !IsValidActor(pActor,
                    activity) || !HistoricalSchoolTaskLeaseService.IsCurrent(
                    pActor.data.id, activity.OperationKey)) return false;
            pAcademy = activity.Venue?.Academy;
            return HistoricalSchoolAcademyService.IsUsable(
                pAcademy, FindCity(activity.Record.CityId));
        }

        public static bool BeginDebateTask(Actor pActor)
        {
            return pActor?.data != null && ByActor.TryGetValue(pActor.data.id,
                       out HistoricalSchoolDebateActivity activity) &&
                   activity.Record.FirstActorId == pActor.data.id &&
                   IsValidActor(pActor, activity) &&
                   HistoricalSchoolTaskLeaseService.IsCurrent(
                       pActor.data.id, activity.OperationKey) &&
                   HistoricalSchoolAcademyService.IsInside(
                       pActor, activity.Venue?.Academy);
        }

        public static bool MarkActorReady(Actor pActor)
        {
            if (pActor?.data == null || !ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolDebateActivity activity) ||
                !IsValidActor(pActor, activity) ||
                !HistoricalSchoolTaskLeaseService.IsCurrent(
                    pActor.data.id, activity.OperationKey) ||
                !HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy))
                return false;
            if (pActor.data.id == activity.Record.FirstActorId)
            {
                activity.FirstReady = true;
            }
            else if (pActor.data.id == activity.Record.SecondActorId)
            {
                activity.SecondReady = true;
            }
            else return false;
            if (activity.FirstReady && activity.SecondReady) EnqueueReady(activity);
            return true;
        }

        internal static bool IsActorBusy(long pActorId)
        {
            return pActorId >= 0 && ByActor.ContainsKey(pActorId);
        }

        public static void CancelActor(Actor pActor, bool pRestoreActors)
        {
            if (pActor?.data != null && ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolDebateActivity activity))
                Finish(activity);
        }

        internal static void CancelExpiredLease(HistoricalSchoolTaskLease pLease)
        {
            if (!string.IsNullOrEmpty(pLease.ActivityId) &&
                ActivitiesById.TryGetValue(pLease.ActivityId,
                    out HistoricalSchoolDebateActivity activity) &&
                (activity.Record.FirstActorId == pLease.ActorId ||
                 activity.Record.SecondActorId == pLease.ActorId))
                Finish(activity);
        }

        public static void ClearRuntime()
        {
            foreach (HistoricalSchoolDebateActivity activity in ActivitiesById.Values)
            {
                HistoricalSchoolAcademyService.Exit(
                    FindActor(activity.Record.FirstActorId), activity.Venue?.Academy);
                HistoricalSchoolAcademyService.Exit(
                    FindActor(activity.Record.SecondActorId), activity.Venue?.Academy);
                HistoricalSchoolVenueService.Release(activity.OperationKey);
            }
            PendingCities.Clear();
            ByActor.Clear();
            ActivitiesById.Clear();
            ReadyActivityIds.Clear();
            ValidationActivityIds.Clear();
            QueuedDebatesByYear.Clear();
            OperationKeys.Clear();
            UsedActorYears.Clear();
        }

        private static bool QueueReadyDebate(HistoricalSchoolDebateActivity pActivity)
        {
            if (pActivity == null || pActivity.PersistenceQueued) return pActivity != null;
            if (HistoricalSchoolActivityQueueRules.ShouldDiscardReadyPersistence(
                    HistoricalSchoolDebateService.IsDebateCommitValid(pActivity)))
            {
                Finish(pActivity);
                return true;
            }
            if (HistoricalSchoolDebateService.TryQueueDebateCommit(pActivity))
            {
                pActivity.PersistenceQueued = true;
                return true;
            }
            EnqueueReady(pActivity);
            return false;
        }

        internal static void OnDebateWriteResolved(
            HistoricalSchoolDebateActivity pActivity,
            HistoricalSchoolTeachingPersistenceOutcome pOutcome)
        {
            if (pActivity == null) return;
            pActivity.PersistenceQueued = false;
            Finish(pActivity);
        }

        private static void Finish(HistoricalSchoolDebateActivity pActivity)
        {
            if (pActivity == null) return;
            if (!ActivitiesById.Remove(pActivity.OperationKey)) return;
            Actor first = FindActor(pActivity.Record.FirstActorId);
            Actor second = FindActor(pActivity.Record.SecondActorId);
            HistoricalSchoolAcademyService.Exit(first, pActivity.Venue?.Academy);
            HistoricalSchoolAcademyService.Exit(second, pActivity.Venue?.Academy);
            ByActor.Remove(pActivity.Record.FirstActorId);
            ByActor.Remove(pActivity.Record.SecondActorId);
            pActivity.ReadyQueued = false;
            pActivity.PersistenceQueued = false;
            DecrementYearCount(pActivity.Record.DebateYear);
            HistoricalSchoolTaskLeaseService.ReleaseExact(
                pActivity.Record.FirstActorId, pActivity.OperationKey);
            HistoricalSchoolTaskLeaseService.ReleaseExact(
                pActivity.Record.SecondActorId, pActivity.OperationKey);
            HistoricalSchoolVenueService.Release(pActivity.OperationKey);
        }

        private static bool IsValidActor(Actor pActor,
            HistoricalSchoolDebateActivity pActivity)
        {
            if (!IsUsable(pActor) || pActivity == null) return false;
            string expectedSchool = pActor.data.id == pActivity.Record.FirstActorId
                ? pActivity.Record.FirstSchoolId
                : pActor.data.id == pActivity.Record.SecondActorId
                    ? pActivity.Record.SecondSchoolId
                    : "";
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pActor.data.id);
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            return !string.IsNullOrEmpty(expectedSchool) && membership?.SchoolId ==
                   expectedSchool && residence?.data?.id == pActivity.Record.CityId &&
                   affiliation?.ResidenceCityId == pActivity.Record.CityId &&
                   HistoricalSchoolRevisionService.IsPresent(affiliation) &&
                   pActivity.Venue?.OperationKey == pActivity.OperationKey &&
                   pActivity.Venue.Primary != null && pActivity.Venue.Secondary != null &&
                   HistoricalSchoolAcademyService.IsUsable(
                       pActivity.Venue.Academy, residence);
        }

        private static bool TryProcessValidation()
        {
            while (ValidationActivityIds.Count > 0)
            {
                string activityId = ValidationActivityIds.Dequeue();
                if (!ActivitiesById.TryGetValue(activityId,
                        out HistoricalSchoolDebateActivity activity)) continue;
                if (ShouldCancelActivity(activity))
                {
                    Finish(activity);
                    return true;
                }
                ValidationActivityIds.Enqueue(activityId);
                return false;
            }
            return false;
        }

        private static bool TryProcessReady()
        {
            while (ReadyActivityIds.Count > 0)
            {
                string activityId = ReadyActivityIds.Dequeue();
                if (!ActivitiesById.TryGetValue(activityId,
                        out HistoricalSchoolDebateActivity activity) ||
                    !activity.FirstReady || !activity.SecondReady) continue;
                activity.ReadyQueued = false;
                QueueReadyDebate(activity);
                return true;
            }
            return false;
        }

        private static void EnqueueReady(HistoricalSchoolDebateActivity pActivity)
        {
            if (pActivity == null || pActivity.ReadyQueued) return;
            pActivity.ReadyQueued = true;
            ReadyActivityIds.Enqueue(pActivity.OperationKey);
        }

        private static void DecrementYearCount(int pYear)
        {
            if (!QueuedDebatesByYear.TryGetValue(pYear, out int count)) return;
            if (count > 1) QueuedDebatesByYear[pYear] = count - 1;
            else QueuedDebatesByYear.Remove(pYear);
        }

        private static bool ShouldCancelActivity(HistoricalSchoolDebateActivity pActivity)
        {
            Actor first = FindActor(pActivity?.Record.FirstActorId ?? -1L);
            Actor second = FindActor(pActivity?.Record.SecondActorId ?? -1L);
            if (!IsValidActor(first, pActivity) || !IsValidActor(second, pActivity))
                return true;
            bool firstExpected = first.isTask(HistoricalSchoolContent.DebateTravelTaskId) ||
                                 first.isTask(HistoricalSchoolContent.DebateTaskId);
            bool secondExpected = second.isTask(
                HistoricalSchoolContent.DebateReceivingTaskId);
            firstExpected &= HistoricalSchoolTaskLeaseService.IsCurrent(
                first.data.id, pActivity.OperationKey);
            secondExpected &= HistoricalSchoolTaskLeaseService.IsCurrent(
                second.data.id, pActivity.OperationKey);
            long age = HistoricalSchoolActivityQueue.CurrentFrame - pActivity.StartFrame;
            return HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(
                       pActivity.FirstReady, firstExpected, age, 120L) ||
                   HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(
                       pActivity.SecondReady, secondExpected, age, 120L);
        }

        private static bool IsUsable(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() && !pActor.isRekt();
        }

        private static Actor FindActor(long pActorId)
        {
            try { return pActorId >= 0 ? World.world?.units?.get(pActorId) : null; }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            try { return pCityId >= 0 ? World.world?.cities?.get(pCityId) : null; }
            catch { return null; }
        }
    }
}
