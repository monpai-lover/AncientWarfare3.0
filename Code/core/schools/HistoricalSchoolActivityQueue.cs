using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AncientWarfare3.content.schools;

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
        public int Attempts { get; set; }
        public long RetryFrame { get; set; }
        public long StartFrame { get; set; }
    }

    internal static class HistoricalSchoolActivityQueue
    {
        private const double FrameBudgetMilliseconds = 1.25d;
        private const int MaxQueuedLectures = 8;
        private static readonly Queue<HistoricalSchoolLectureActivity> PendingLectures =
            new Queue<HistoricalSchoolLectureActivity>();
        private static readonly Dictionary<long, HistoricalSchoolLectureActivity>
            ActiveLectures = new Dictionary<long, HistoricalSchoolLectureActivity>();
        private static readonly HashSet<string> OperationKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static HistoricalSchoolTeachingHistory _teachingHistory =
            new HistoricalSchoolTeachingHistory();
        private static bool _loaded;
        private static long _frame;

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
                HistoricalSchoolVenueService.Release(activity.Plan.OperationKey);
            PendingLectures.Clear();
            ActiveLectures.Clear();
            OperationKeys.Clear();
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
            foreach (HistoricalSchoolLectureActivity activity in PendingLectures
                         .Concat(ActiveLectures.Values).Distinct())
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
            int yearCount = PendingLectures.Count(p => p.Plan.Year == pPlan.Year) +
                            ActiveLectures.Values.Count(p => p.Plan.Year == pPlan.Year);
            bool duplicate = OperationKeys.Contains(pPlan.OperationKey);
            if (!HistoricalSchoolActivityQueueRules.CanEnqueue(yearCount,
                    MaxQueuedLectures, duplicate)) return false;
            OperationKeys.Add(pPlan.OperationKey);
            PendingLectures.Enqueue(new HistoricalSchoolLectureActivity(pPlan,
                pDirectDiscipleCount));
            return true;
        }

        public static bool TryPrepareLectureActor(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            if (pActor?.data == null ||
                !ActiveLectures.TryGetValue(pActor.data.id,
                    out HistoricalSchoolLectureActivity activity) ||
                !IsValidLectureActor(pActor, activity)) return false;
            pTarget = activity.Venue?.Primary;
            return pTarget != null;
        }

        public static bool MarkLectureActorReady(Actor pActor)
        {
            if (pActor?.data == null ||
                !ActiveLectures.TryGetValue(pActor.data.id,
                    out HistoricalSchoolLectureActivity activity) ||
                !IsValidLectureActor(pActor, activity)) return false;
            activity.Ready = true;
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
                FinishLecture(activity, pRestoreActor);
            HistoricalSchoolDebateActivityService.CancelActor(pActor, pRestoreActor);
        }

        public static void ProcessFrame()
        {
            EnsureLoaded();
            _frame++;
            var timer = Stopwatch.StartNew();
            int transitions = 0;
            HistoricalSchoolLectureActivity invalid = ActiveLectures.Values
                .OrderBy(p => p.Plan.Candidate.ActorId)
                .FirstOrDefault(activity => ShouldCancelLecture(activity));
            if (invalid != null)
            {
                FinishLecture(invalid, pRestoreActor: true);
                return;
            }
            HistoricalSchoolLectureActivity ready = ActiveLectures.Values
                .Where(p => p.Ready && p.RetryFrame <= _frame)
                .OrderBy(p => p.Plan.Candidate.ActorId)
                .FirstOrDefault();
            if (ready != null && HistoricalSchoolActivityQueueRules.CanAdvance(transitions,
                    timer.Elapsed.TotalMilliseconds, FrameBudgetMilliseconds))
            {
                transitions++;
                ResolveReadyLecture(ready, pScheduleRetry: true);
                return;
            }

            if (!HistoricalSchoolActivityQueueRules.CanAdvance(transitions,
                    timer.Elapsed.TotalMilliseconds, FrameBudgetMilliseconds)) return;
            if (PendingLectures.Count == 0)
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
            if (!IsValidLectureActor(actor, pending) || city?.data == null || city.isRekt() ||
                !HistoricalSchoolVenueService.TryClaimLecture(city,
                    pending.Plan.OperationKey, out HistoricalSchoolVenueClaim venue))
            {
                HistoricalSchoolVenueService.Release(pending.Plan.OperationKey);
                return;
            }
            pending.Venue = venue;
            pending.StartFrame = _frame;
            pending.CandidateActorIds = HistoricalSchoolRecruitCandidateCache.Get(city,
                actor, pending.Plan.Year);
            ActiveLectures[actor.data.id] = pending;
            try
            {
                actor.setTask(HistoricalSchoolContent.LectureTaskId, pClean: true,
                    pCleanJob: false, pForceAction: true);
            }
            catch
            {
                FinishLecture(pending, pRestoreActor: true);
            }
        }

        internal static bool FlushPendingPersistenceForSave()
        {
            EnsureLoaded();
            bool resolved = true;
            HistoricalSchoolLectureActivity[] lectures = ActiveLectures.Values
                .Where(p => HistoricalSchoolActivityQueueRules.ShouldFlushForSave(p.Ready))
                .OrderBy(p => p.Plan.Candidate.ActorId)
                .ToArray();
            foreach (HistoricalSchoolLectureActivity lecture in lectures)
                if (!ResolveReadyLecture(lecture, pScheduleRetry: false)) resolved = false;
            if (!HistoricalSchoolDebateActivityService.FlushPendingPersistenceForSave())
                resolved = false;
            return resolved;
        }

        private static bool ResolveReadyLecture(HistoricalSchoolLectureActivity pActivity,
            bool pScheduleRetry)
        {
            HistoricalSchoolTeachingPersistenceOutcome outcome;
            try
            {
                outcome = HistoricalSchoolActionService.CommitQueuedLecture(pActivity);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school lecture commit failed: " +
                                    error.Message);
                outcome = HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            }

            if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                outcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
            {
                _teachingHistory.RecordLecture(pActivity.Plan.Candidate, pActivity.Plan.Year);
                if (pActivity.Plan.IncludePersuasion)
                    _teachingHistory.RecordPersuasion(pActivity.Plan.Candidate,
                        pActivity.Plan.Year);
            }
            if (HistoricalSchoolActivityQueueRules.IsPersistenceResolved(outcome))
            {
                FinishLecture(pActivity, pRestoreActor: true);
                return true;
            }
            if (pScheduleRetry)
            {
                pActivity.Attempts++;
                pActivity.RetryFrame = _frame + RetryDelay(pActivity.Attempts);
            }
            return false;
        }

        private static void FinishLecture(HistoricalSchoolLectureActivity pActivity,
            bool pRestoreActor)
        {
            if (pActivity == null) return;
            long actorId = pActivity.Plan.Candidate.ActorId;
            ActiveLectures.Remove(actorId);
            HistoricalSchoolVenueService.Release(pActivity.Plan.OperationKey);
            if (!pRestoreActor) return;
            Actor actor = FindActor(actorId);
            if (actor?.data == null || actor.isRekt()) return;
            CitizenJobAsset job = AssetManager.citizen_job_library.get(
                HistoricalSchoolContent.CitizenJobId);
            if (job != null) actor.setCitizenJob(job);
        }

        private static bool IsValidLectureActor(Actor pActor,
            HistoricalSchoolLectureActivity pActivity)
        {
            if (pActor?.data == null || pActivity == null || !pActor.isAlive() ||
                pActor.isRekt()) return false;
            HistoricalSchoolLectureCandidate candidate = pActivity.Plan.Candidate;
            if (pActor.data.id != candidate.ActorId) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(
                pActor.data.id);
            if (membership == null || membership.SchoolId != candidate.SchoolId) return false;
            City residence = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            return residence?.data != null && !residence.isRekt() &&
                   residence.data.id == candidate.CityId &&
                   pActor.city?.data?.id == residence.data.id &&
                   HistoricalAffiliationService.IsAvailableForOffice(pActor) &&
                   HistoricalAffiliationService.IsPresentForInfluence(pActor);
        }

        private static bool ShouldCancelLecture(HistoricalSchoolLectureActivity pActivity)
        {
            Actor actor = FindActor(pActivity?.Plan.Candidate.ActorId ?? -1L);
            if (!IsValidLectureActor(actor, pActivity)) return true;
            bool expectedTask = actor.isTask(HistoricalSchoolContent.LectureTaskId);
            return HistoricalSchoolActivityQueueRules.ShouldCancelInterrupted(
                pActivity.Ready, expectedTask, _frame - pActivity.StartFrame, 120L);
        }

        private static int RetryDelay(int pAttempts)
        {
            int shift = Math.Min(8, Math.Max(0, pAttempts - 1));
            return Math.Min(240, 1 << shift);
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
