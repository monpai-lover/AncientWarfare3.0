using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;

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
        }

        public long CityId { get; }
        public int Year { get; }
        public HistoricalSchoolDebateActorSeed[] Actors { get; }
        public string OperationKey => "school-debate:" + Year + ":" + CityId;
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
        }

        public HistoricalSchoolDebateRecord Record { get; }
        public HistoricalSchoolLedgerDelta FirstDelta { get; }
        public HistoricalSchoolLedgerDelta SecondDelta { get; }
        public HistoricalSchoolVenueClaim Venue { get; set; }
        public bool FirstReady { get; set; }
        public bool SecondReady { get; set; }
        public int Attempts { get; set; }
        public long RetryFrame { get; set; }
        public long StartFrame { get; set; }
        public string OperationKey => "school-debate:" + Record.DebateYear + ":" +
                                      Record.CityId;
    }

    internal static class HistoricalSchoolDebateActivityService
    {
        private const int MaxQueuedPerYear = HistoricalSchoolDebateService.MaxDebatesPerYear;
        private const int MaxConcurrentDebates = 4;
        private static readonly Queue<HistoricalSchoolDebateCityRequest> PendingCities =
            new Queue<HistoricalSchoolDebateCityRequest>();
        private static readonly Dictionary<long, HistoricalSchoolDebateActivity> ByActor =
            new Dictionary<long, HistoricalSchoolDebateActivity>();
        private static readonly HashSet<string> OperationKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> UsedActorYears =
            new HashSet<string>(StringComparer.Ordinal);
        private static long _frame;

        public static bool TryEnqueueCity(long pCityId, int pYear,
            IEnumerable<HistoricalSchoolDebateActorSeed> pActors)
        {
            var request = new HistoricalSchoolDebateCityRequest(pCityId, pYear, pActors);
            int yearCount = PendingCities.Count(p => p.Year == pYear) +
                            ByActor.Values.Distinct().Count(p => p.Record.DebateYear == pYear);
            if (request.CityId < 0 || request.Year < 0 || request.Actors.Length < 2 ||
                !HistoricalSchoolActivityQueueRules.CanEnqueue(yearCount,
                    MaxQueuedPerYear, OperationKeys.Contains(request.OperationKey))) return false;
            OperationKeys.Add(request.OperationKey);
            PendingCities.Enqueue(request);
            return true;
        }

        public static bool ProcessFrame()
        {
            _frame++;
            HistoricalSchoolDebateActivity invalid = ByActor.Values.Distinct()
                .OrderBy(p => p.Record.CityId)
                .FirstOrDefault(ShouldCancelActivity);
            if (invalid != null)
            {
                Finish(invalid, pRestoreActors: true);
                return true;
            }
            HistoricalSchoolDebateActivity ready = ByActor.Values.Distinct()
                .Where(p => p.FirstReady && p.SecondReady && p.RetryFrame <= _frame)
                .OrderBy(p => p.Record.CityId)
                .FirstOrDefault();
            if (ready != null)
            {
                ResolveReadyDebate(ready, pScheduleRetry: true);
                return true;
            }

            int activeCount = ByActor.Values.Distinct().Count();
            if (!HistoricalSchoolActivityQueueRules.CanActivate(activeCount,
                    MaxConcurrentDebates)) return false;

            while (PendingCities.Count > 0)
            {
                HistoricalSchoolDebateCityRequest request = PendingCities.Dequeue();
                HistoricalSchoolDebateActorSeed[] available = request.Actors
                    .Where(p => !UsedActorYears.Contains(
                        HistoricalSchoolActivityQueueRules.ActorYearKey(request.Year,
                            p.ActorId))).ToArray();
                if (!HistoricalSchoolDebateService.TryCreateQueuedDebate(request.CityId,
                        request.Year, available, out HistoricalSchoolDebateActivity activity))
                    return true;
                City city = FindCity(request.CityId);
                if (city?.data == null || city.isRekt() ||
                    !HistoricalSchoolVenueService.TryClaimDebate(city,
                        activity.OperationKey, out HistoricalSchoolVenueClaim venue))
                    return true;
                activity.Venue = venue;
                activity.StartFrame = _frame;
                ByActor[activity.Record.FirstActorId] = activity;
                ByActor[activity.Record.SecondActorId] = activity;
                UsedActorYears.Add(HistoricalSchoolActivityQueueRules.ActorYearKey(
                    activity.Record.DebateYear, activity.Record.FirstActorId));
                UsedActorYears.Add(HistoricalSchoolActivityQueueRules.ActorYearKey(
                    activity.Record.DebateYear, activity.Record.SecondActorId));
                Actor first = FindActor(activity.Record.FirstActorId);
                Actor second = FindActor(activity.Record.SecondActorId);
                if (!IsUsable(first) || !IsUsable(second))
                {
                    Finish(activity, pRestoreActors: true);
                    return true;
                }
                try
                {
                    first.setTask(HistoricalSchoolContent.DebateTravelTaskId, pClean: true,
                        pCleanJob: false, pForceAction: true);
                    second.setTask(HistoricalSchoolContent.DebateReceivingTaskId,
                        pClean: true, pCleanJob: false, pForceAction: true);
                }
                catch
                {
                    Finish(activity, pRestoreActors: true);
                }
                return true;
            }
            return false;
        }

        internal static bool FlushPendingPersistenceForSave()
        {
            bool resolved = true;
            HistoricalSchoolDebateActivity[] activities = ByActor.Values.Distinct()
                .Where(p => HistoricalSchoolActivityQueueRules.ShouldFlushForSave(
                    p.FirstReady && p.SecondReady))
                .OrderBy(p => p.Record.CityId)
                .ToArray();
            foreach (HistoricalSchoolDebateActivity activity in activities)
                if (!ResolveReadyDebate(activity, pScheduleRetry: false)) resolved = false;
            return resolved;
        }

        public static bool TryPrepareActor(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null || !ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolDebateActivity activity) || !IsValidActor(pActor,
                    activity)) return false;
            pTarget = pActor.data.id == activity.Record.FirstActorId
                ? activity.Venue?.Primary
                : activity.Venue?.Secondary;
            return pTarget != null;
        }

        public static bool BeginDebateTask(Actor pActor)
        {
            return pActor?.data != null && ByActor.TryGetValue(pActor.data.id,
                       out HistoricalSchoolDebateActivity activity) &&
                   activity.Record.FirstActorId == pActor.data.id &&
                   IsValidActor(pActor, activity);
        }

        public static bool MarkActorReady(Actor pActor)
        {
            if (pActor?.data == null || !ByActor.TryGetValue(pActor.data.id,
                    out HistoricalSchoolDebateActivity activity) ||
                !IsValidActor(pActor, activity)) return false;
            if (pActor.data.id == activity.Record.FirstActorId) activity.FirstReady = true;
            else if (pActor.data.id == activity.Record.SecondActorId)
                activity.SecondReady = true;
            else return false;
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
                Finish(activity, pRestoreActors);
        }

        public static void ClearRuntime()
        {
            foreach (HistoricalSchoolDebateActivity activity in ByActor.Values.Distinct())
                HistoricalSchoolVenueService.Release(activity.OperationKey);
            PendingCities.Clear();
            ByActor.Clear();
            OperationKeys.Clear();
            UsedActorYears.Clear();
            _frame = 0L;
        }

        private static bool ResolveReadyDebate(HistoricalSchoolDebateActivity pActivity,
            bool pScheduleRetry)
        {
            HistoricalSchoolTeachingPersistenceOutcome outcome;
            try
            {
                outcome = HistoricalSchoolDebateService.CommitQueuedDebate(pActivity);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school debate commit failed: " + error.Message);
                outcome = HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            }
            if (HistoricalSchoolActivityQueueRules.IsPersistenceResolved(outcome))
            {
                Finish(pActivity, pRestoreActors: true);
                return true;
            }
            if (pScheduleRetry)
            {
                pActivity.Attempts++;
                pActivity.RetryFrame = _frame + Math.Min(120,
                    1L << Math.Min(6, pActivity.Attempts));
            }
            return false;
        }

        private static void Finish(HistoricalSchoolDebateActivity pActivity,
            bool pRestoreActors)
        {
            if (pActivity == null) return;
            ByActor.Remove(pActivity.Record.FirstActorId);
            ByActor.Remove(pActivity.Record.SecondActorId);
            HistoricalSchoolVenueService.Release(pActivity.OperationKey);
            if (!pRestoreActors) return;
            RestoreScholar(FindActor(pActivity.Record.FirstActorId));
            RestoreScholar(FindActor(pActivity.Record.SecondActorId));
        }

        private static void RestoreScholar(Actor pActor)
        {
            if (!IsUsable(pActor)) return;
            CitizenJobAsset job = AssetManager.citizen_job_library.get(
                HistoricalSchoolContent.CitizenJobId);
            if (job != null) pActor.setCitizenJob(job);
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
            City residence = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            return !string.IsNullOrEmpty(expectedSchool) && membership?.SchoolId ==
                   expectedSchool && residence?.data?.id == pActivity.Record.CityId &&
                   HistoricalAffiliationService.IsPresentForInfluence(pActor);
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
            long age = _frame - pActivity.StartFrame;
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
