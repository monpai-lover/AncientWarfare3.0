using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal sealed class HistoricalSchoolLectureActivity
    {
        public HistoricalSchoolLectureActivity(HistoricalSchoolTeachingPlan pPlan,
            int pDirectDiscipleCount)
        {
            Plan = pPlan;
            DirectDiscipleCount = Math.Max(0, pDirectDiscipleCount);
        }

        public HistoricalSchoolTeachingPlan Plan { get; }
        public int DirectDiscipleCount { get; }
        public long[] CandidateActorIds { get; set; } = Array.Empty<long>();
        public HistoricalSchoolVenueClaim Venue { get; set; }
        public bool Ready { get; set; }
        public bool ReadyQueued { get; set; }
        public bool PersistenceQueued { get; set; }
        public long StartFrame { get; set; }
        public int RetryCount { get; set; }
    }

    internal static class HistoricalSchoolActivityQueue
    {
        private const int MaxQueuedLectures = 8;
        private const int MaxConcurrentLectures = 8;
        private const int MaxRetainedLectures = MaxQueuedLectures * 2;
        private const long TaskLeaseFrames = 600L;
        private static readonly Queue<HistoricalSchoolLectureActivity> PendingLectures =
            new Queue<HistoricalSchoolLectureActivity>();
        private static readonly Dictionary<long, HistoricalSchoolLectureActivity>
            ActiveLectures = new Dictionary<long, HistoricalSchoolLectureActivity>();
        private static readonly Queue<long> ReadyLectureActors = new Queue<long>();
        private static readonly Queue<long> ValidationActors = new Queue<long>();
        private static readonly Dictionary<int, int> QueuedLecturesByYear =
            new Dictionary<int, int>();
        private static readonly HistoricalSchoolBoundedYearKeys OperationKeys =
            new HistoricalSchoolBoundedYearKeys();
        private static readonly HashSet<long> QueuedLectureActors = new HashSet<long>();
        private static HistoricalSchoolTeachingHistory _teachingHistory =
            new HistoricalSchoolTeachingHistory();
        private static bool _loaded;
        private static long _frame;

        internal static long CurrentFrame => _frame;

        public static void LoadState()
        {
            ClearRuntime();
            _teachingHistory = HistoricalSchoolStore.LoadTeachingHistory() ??
                               new HistoricalSchoolTeachingHistory();
            _loaded = true;
        }

        public static void ClearRuntime()
        {
            foreach (HistoricalSchoolLectureActivity activity in ActiveLectures.Values)
            {
                Actor actor = FindActor(activity.Plan.Candidate.ActorId);
                HistoricalSchoolAcademyService.Exit(actor, activity.Venue?.Academy);
                HistoricalSchoolVenueService.Release(activity.Plan.OperationKey);
            }
            PendingLectures.Clear();
            ActiveLectures.Clear();
            ReadyLectureActors.Clear();
            ValidationActors.Clear();
            QueuedLecturesByYear.Clear();
            OperationKeys.Clear();
            QueuedLectureActors.Clear();
            HistoricalSchoolTaskLeaseService.Clear();
            HistoricalSchoolVenueService.Clear();
            HistoricalSchoolRecruitCandidateCache.Clear();
            HistoricalSchoolDebateActivityService.ClearRuntime();
            _teachingHistory = new HistoricalSchoolTeachingHistory();
            _loaded = false;
            _frame = 0L;
        }

        public static HistoricalSchoolTeachingBudget CreateTeachingBudget(int pYear)
        {
            EnsureLoaded();
            HistoricalSchoolTeachingHistory planningHistory = _teachingHistory.Clone();
            foreach (HistoricalSchoolLectureActivity activity in PendingLectures)
            {
                planningHistory.RecordLecture(activity.Plan.Candidate, activity.Plan.Year);
                if (activity.Plan.IncludePersuasion)
                    planningHistory.RecordPersuasion(activity.Plan.Candidate,
                        activity.Plan.Year);
            }
            foreach (HistoricalSchoolLectureActivity activity in ActiveLectures.Values)
            {
                planningHistory.RecordLecture(activity.Plan.Candidate, activity.Plan.Year);
                if (activity.Plan.IncludePersuasion)
                    planningHistory.RecordPersuasion(activity.Plan.Candidate,
                        activity.Plan.Year);
            }
            return new HistoricalSchoolTeachingBudget(pYear, planningHistory);
        }

        public static bool TryEnqueueLecture(HistoricalSchoolTeachingPlan pPlan,
            int pDirectDiscipleCount)
        {
            if (!pPlan.IsValid) return false;
            City city = FindCity(pPlan.Candidate.CityId);
            if (!HistoricalSchoolXiaAccessService.CanHostLecture(city))
                return false;
            if (!HistoricalSchoolActivityQueueRules.CanEnqueueTotal(
                    PendingLectures.Count + ActiveLectures.Count,
                    MaxRetainedLectures)) return false;
            QueuedLecturesByYear.TryGetValue(pPlan.Year, out int yearCount);
            bool duplicate = OperationKeys.Contains(pPlan.Year, pPlan.OperationKey);
            if (QueuedLectureActors.Contains(pPlan.Candidate.ActorId)) return false;
            if (!HistoricalSchoolActivityQueueRules.CanEnqueue(yearCount,
                    MaxQueuedLectures, duplicate)) return false;
            if (!OperationKeys.Add(pPlan.Year, pPlan.OperationKey)) return false;
            QueuedLectureActors.Add(pPlan.Candidate.ActorId);
            PendingLectures.Enqueue(new HistoricalSchoolLectureActivity(pPlan,
                pDirectDiscipleCount));
            QueuedLecturesByYear[pPlan.Year] = yearCount + 1;
            return true;
        }

        public static bool TryPrepareLectureActor(Actor pActor, out Building pAcademy)
        {
            pAcademy = null;
            if (pActor?.data == null ||
                !ActiveLectures.TryGetValue(pActor.data.id,
                    out HistoricalSchoolLectureActivity activity) ||
                !IsValidLectureActor(pActor, activity) ||
                !HistoricalSchoolTaskLeaseService.IsCurrent(
                    pActor.data.id, activity.Plan.OperationKey,
                    HistoricalSchoolContent.LectureTaskId)) return false;
            pAcademy = activity.Venue?.Academy;
            return HistoricalSchoolAcademyService.IsUsable(
                pAcademy, FindCity(activity.Plan.Candidate.CityId));
        }

        public static bool MarkLectureActorReady(Actor pActor)
        {
            if (pActor?.data == null ||
                !ActiveLectures.TryGetValue(pActor.data.id,
                    out HistoricalSchoolLectureActivity activity) ||
                !IsValidLectureActor(pActor, activity) ||
                !HistoricalSchoolTaskLeaseService.IsCurrent(
                    pActor.data.id, activity.Plan.OperationKey) ||
                !HistoricalSchoolAcademyService.IsInside(pActor, activity.Venue?.Academy))
                return false;
            activity.Ready = true;
            EnqueueReady(activity);
            return true;
        }

        internal static bool IsLectureActorBusy(long pActorId)
        {
            return pActorId >= 0 && ActiveLectures.ContainsKey(pActorId);
        }

        public static void CancelActor(Actor pActor, bool pRestoreActor)
        {
            if (pActor?.data == null) return;
            if (ActiveLectures.TryGetValue(pActor.data.id,
                    out HistoricalSchoolLectureActivity activity))
                FinishLecture(activity);
            HistoricalSchoolDebateActivityService.CancelActor(pActor, pRestoreActor);
        }

        public static void ProcessFrame()
        {
            EnsureLoaded();
            _frame++;
            if (HistoricalSchoolTaskLeaseService.TryTakeExpired(
                    _frame, out HistoricalSchoolTaskLease expired))
            {
                if (ActiveLectures.TryGetValue(expired.ActorId,
                        out HistoricalSchoolLectureActivity expiredLecture) &&
                    expiredLecture.Plan.OperationKey == expired.ActivityId)
                {
                    Actor expiredActor = FindActor(
                        expiredLecture.Plan.Candidate.ActorId);
                    bool stillValid = IsValidLectureActor(expiredActor,
                        expiredLecture);
                    if (HistoricalSchoolActivityQueueRules.ShouldRetryExpiredLecture(
                            expiredLecture.RetryCount, stillValid))
                        RetryLecture(expiredLecture, expiredActor);
                    else
                        FinishLecture(expiredLecture);
                }
                else if (expired.TaskId == HistoricalSchoolContent.
                             EducationTravelTaskId)
                    HistoricalSchoolEducationJourneyService.
                        CancelExpiredLease(expired);
                else if (expired.TaskId == HistoricalSchoolContent.TravelTaskId)
                    HistoricalSchoolTravelService.CancelExpiredLease(expired);
                else
                    HistoricalSchoolDebateActivityService.CancelExpiredLease(expired);
                return;
            }

            if (TryProcessValidation()) return;
            if (TryProcessReadyLecture()) return;
            if (PendingLectures.Count == 0 ||
                !HistoricalSchoolActivityQueueRules.CanActivate(
                    ActiveLectures.Count, MaxConcurrentLectures))
            {
                if (HistoricalSchoolDebateActivityService.ProcessFrame()) return;
                HistoricalSchoolActionService.ProcessDeferredFrame();
                return;
            }
            HistoricalSchoolLectureActivity pending = PendingLectures.Dequeue();
            Actor actor = FindActor(pending.Plan.Candidate.ActorId);
            City city = FindCity(pending.Plan.Candidate.CityId);
            if (actor?.data != null &&
                HistoricalSchoolDebateActivityService.IsActorBusy(actor.data.id))
            {
                PendingLectures.Enqueue(pending);
                HistoricalSchoolDebateActivityService.ProcessFrame();
                return;
            }
            if (!IsValidLectureActor(actor, pending, pRequireVenue: false) ||
                city?.data == null || city.isRekt() ||
                !HistoricalSchoolXiaAccessService.CanHostLecture(city) ||
                !HistoricalSchoolVenueService.TryClaimLecture(city, actor,
                    pending.Plan.Candidate.SchoolId, pending.Plan.OperationKey,
                    out HistoricalSchoolVenueClaim venue))
            {
                HistoricalSchoolVenueService.Release(pending.Plan.OperationKey);
                DecrementYearCount(pending.Plan.Year);
                QueuedLectureActors.Remove(pending.Plan.Candidate.ActorId);
                return;
            }
            pending.Venue = venue;
            pending.StartFrame = _frame;
            long candidateDiagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try
            {
                pending.CandidateActorIds = HistoricalSchoolRecruitCandidateCache.Get(
                    city, actor, pending.Plan.Year);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "school_lecture_candidates", candidateDiagnostic);
            }
            ActiveLectures[actor.data.id] = pending;
            ValidationActors.Enqueue(actor.data.id);
            if (!HistoricalSchoolTaskLeaseService.TrySchedule(
                    actor,
                    pending.Plan.OperationKey,
                    HistoricalSchoolContent.LectureTaskId,
                    pending.Plan.Candidate.SchoolId,
                    pending.Plan.Candidate.CityId,
                    pending.Plan.OperationKey,
                    venue.Primary,
                    _frame,
                    _frame + TaskLeaseFrames))
            {
                FinishLecture(pending);
            }
        }

        internal static bool FlushPendingPersistenceForSave()
        {
            EnsureLoaded();
            bool resolved = true;
            int readyBudget = ReadyLectureActors.Count;
            while (readyBudget-- > 0 && ReadyLectureActors.Count > 0)
            {
                long actorId = ReadyLectureActors.Dequeue();
                if (!ActiveLectures.TryGetValue(actorId,
                        out HistoricalSchoolLectureActivity lecture) || !lecture.Ready)
                    continue;
                lecture.ReadyQueued = false;
                if (!QueueReadyLecture(lecture))
                    resolved = false;
            }
            if (!HistoricalSchoolDebateActivityService.FlushPendingPersistenceForSave())
                resolved = false;
            return resolved;
        }

        private static bool QueueReadyLecture(HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null || pActivity.PersistenceQueued) return pActivity != null;
            if (HistoricalSchoolActivityQueueRules.ShouldDiscardReadyPersistence(
                    HistoricalSchoolActionService.IsLectureCommitValid(pActivity)))
            {
                FinishLecture(pActivity);
                return true;
            }
            if (HistoricalSchoolActionService.TryQueueLectureCommit(pActivity))
            {
                pActivity.PersistenceQueued = true;
                return true;
            }
            EnqueueReady(pActivity);
            return false;
        }

        private static void RetryLecture(
            HistoricalSchoolLectureActivity pActivity, Actor pActor)
        {
            if (pActivity == null) return;
            long actorId = pActivity.Plan.Candidate.ActorId;
            HistoricalSchoolAcademyService.Exit(pActor, pActivity.Venue?.Academy);
            ActiveLectures.Remove(actorId);
            HistoricalSchoolTaskLeaseService.ReleaseExact(actorId,
                pActivity.Plan.OperationKey);
            HistoricalSchoolVenueService.Release(pActivity.Plan.OperationKey);
            pActivity.Venue = null;
            pActivity.Ready = false;
            pActivity.ReadyQueued = false;
            pActivity.PersistenceQueued = false;
            pActivity.StartFrame = 0L;
            pActivity.RetryCount++;
            PendingLectures.Enqueue(pActivity);
        }

        internal static void OnLectureWriteResolved(
            HistoricalSchoolLectureActivity pActivity,
            HistoricalSchoolTeachingPersistenceOutcome pOutcome)
        {
            if (pActivity == null) return;
            pActivity.PersistenceQueued = false;
            if (pOutcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                pOutcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
            {
                _teachingHistory.RecordLecture(pActivity.Plan.Candidate,
                    pActivity.Plan.Year);
                if (pActivity.Plan.IncludePersuasion)
                    _teachingHistory.RecordPersuasion(pActivity.Plan.Candidate,
                        pActivity.Plan.Year);
            }
            FinishLecture(pActivity);
        }

        private static void FinishLecture(HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null) return;
            long actorId = pActivity.Plan.Candidate.ActorId;
            Actor actor = FindActor(actorId);
            HistoricalSchoolAcademyService.Exit(actor, pActivity.Venue?.Academy);
            if (ActiveLectures.Remove(actorId)) DecrementYearCount(pActivity.Plan.Year);
            QueuedLectureActors.Remove(actorId);
            pActivity.ReadyQueued = false;
            pActivity.PersistenceQueued = false;
            if (!HistoricalSchoolTaskLeaseService.ReleaseExact(
                    actorId, pActivity.Plan.OperationKey))
                HistoricalSchoolVenueService.Release(pActivity.Plan.OperationKey);
        }

        private static bool IsValidLectureActor(
            Actor pActor,
            HistoricalSchoolLectureActivity pActivity,
            bool pRequireVenue = true)
        {
            if (pActor?.data == null || pActivity == null || !pActor.isAlive() ||
                pActor.isRekt()) return false;
            HistoricalSchoolLectureCandidate candidate = pActivity.Plan.Candidate;
            if (pActor.data.id != candidate.ActorId) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pActor.data.id);
            if (membership == null || membership.SchoolId != candidate.SchoolId) return false;
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(pActor);
            return residence?.data != null && !residence.isRekt() &&
                   HistoricalSchoolXiaAccessService.CanHostLecture(residence) &&
                   residence.data.id == candidate.CityId &&
                   affiliation?.ResidenceCityId == candidate.CityId &&
                   HistoricalSchoolRevisionService.IsPresent(affiliation) &&
                   (!pRequireVenue ||
                    pActivity.Venue?.OperationKey == pActivity.Plan.OperationKey &&
                    pActivity.Venue.Primary != null &&
                    HistoricalSchoolAcademyService.IsUsable(
                        pActivity.Venue.Academy, residence));
        }

        private static bool TryProcessValidation()
        {
            while (ValidationActors.Count > 0)
            {
                long actorId = ValidationActors.Dequeue();
                if (!ActiveLectures.TryGetValue(actorId,
                        out HistoricalSchoolLectureActivity activity)) continue;
                if (ShouldCancelLecture(activity))
                {
                    FinishLecture(activity);
                    return true;
                }
                ValidationActors.Enqueue(actorId);
                return false;
            }
            return false;
        }

        private static bool TryProcessReadyLecture()
        {
            while (ReadyLectureActors.Count > 0)
            {
                long actorId = ReadyLectureActors.Dequeue();
                if (!ActiveLectures.TryGetValue(actorId,
                        out HistoricalSchoolLectureActivity activity) || !activity.Ready)
                    continue;
                activity.ReadyQueued = false;
                QueueReadyLecture(activity);
                return true;
            }
            return false;
        }

        private static void EnqueueReady(HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null || pActivity.ReadyQueued) return;
            pActivity.ReadyQueued = true;
            ReadyLectureActors.Enqueue(pActivity.Plan.Candidate.ActorId);
        }

        private static void DecrementYearCount(int pYear)
        {
            if (!QueuedLecturesByYear.TryGetValue(pYear, out int count)) return;
            if (count > 1) QueuedLecturesByYear[pYear] = count - 1;
            else QueuedLecturesByYear.Remove(pYear);
        }

        private static bool ShouldCancelLecture(HistoricalSchoolLectureActivity pActivity)
        {
            Actor actor = FindActor(pActivity?.Plan.Candidate.ActorId ?? -1L);
            if (!IsValidLectureActor(actor, pActivity)) return true;
            bool expectedTask = HistoricalSchoolTaskLeaseService.IsCurrent(
                actor.data.id, pActivity.Plan.OperationKey,
                HistoricalSchoolContent.LectureTaskId) &&
                actor.isTask(HistoricalSchoolContent.LectureTaskId);
            return HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(
                pActivity.Ready, expectedTask, _frame - pActivity.StartFrame, 120L);
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

        private static void EnsureLoaded()
        {
            if (!_loaded) LoadState();
        }
    }
}
