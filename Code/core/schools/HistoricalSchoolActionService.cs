using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolActionService
    {
        private const int MaxTeachersPerYear = 192;
        private const int MaxExplicitActionsPerYear = 8;
        private const int MaxRediscoveriesPerYear = 4;
        private static int _lastProcessedYear = -1;
        private static readonly Queue<DeferredActionState> DeferredActions =
            new Queue<DeferredActionState>();
        private static readonly HashSet<int> DeferredYears = new HashSet<int>();
        private static LecturePlanningState _lecturePlanning;

        private sealed class LecturePlanningState
        {
            public LecturePlanningState(int pYear,
                HistoricalSchoolTeachingBudget pTeachingBudget,
                int[] pSchoolOrder, int pSchoolCount)
            {
                Year = pYear;
                TeachingBudget = pTeachingBudget;
                SchoolOrder = pSchoolOrder ?? Array.Empty<int>();
                Candidates = new List<LectureTeacherCandidate>[pSchoolCount];
                CandidateIndices = new int[pSchoolCount];
            }

            public int Year { get; }
            public HistoricalSchoolTeachingBudget TeachingBudget { get; }
            public int[] SchoolOrder { get; }
            public List<LectureTeacherCandidate>[] Candidates { get; }
            public int[] CandidateIndices { get; }
            public int Pass { get; set; }
            public int Offset { get; set; }
            public int Examined { get; set; }
            public int Planned { get; set; }
        }

        public static void ClearRuntime()
        {
            _lastProcessedYear = -1;
            DeferredActions.Clear();
            DeferredYears.Clear();
            _lecturePlanning = null;
        }

        public static bool ProcessYearFrame(int pYear)
        {
            if (pYear < 0 || pYear == _lastProcessedYear) return true;
            if (_lecturePlanning == null || _lecturePlanning.Year != pYear)
            {
                long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
                try { _lecturePlanning = BeginLecturePlanning(pYear); }
                finally
                {
                    RuntimePerformanceDiagnostic.EndDetail(
                        "school_lecture_begin", diagnostic);
                }
            }
            LecturePlanningState planning = _lecturePlanning;
            if (IsLecturePlanningComplete(planning))
                return FinishLecturePlanningMeasured(planning);

            long schoolDiagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { ProcessLectureSchool(planning); }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school lecture planning failed: " +
                                    error.Message);
            }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "school_lecture_school", schoolDiagnostic);
                AdvanceLecturePlanning(planning);
            }
            return IsLecturePlanningComplete(planning)
                ? FinishLecturePlanningMeasured(planning)
                : false;
        }

        private static bool FinishLecturePlanningMeasured(
            LecturePlanningState pPlanning)
        {
            long diagnostic = RuntimePerformanceDiagnostic.BeginScope();
            try { return FinishLecturePlanning(pPlanning); }
            finally
            {
                RuntimePerformanceDiagnostic.EndDetail(
                    "school_lecture_finish", diagnostic);
            }
        }

        private static LecturePlanningState BeginLecturePlanning(int pYear)
        {
            int schoolCount = CourtSchoolRegistry.All.Count;
            int start = PositiveModulo(pYear, schoolCount);
            int[] schoolOrder = Enumerable.Range(0, schoolCount)
                .OrderByDescending(index =>
                    HistoricalSchoolLectureRules.PopulationPriority(
                        HistoricalSchoolRuntimeIndex.Instance.MemberCount(
                            CourtSchoolRegistry.All[index].Id)))
                .ThenBy(index => PositiveModulo(index - start, schoolCount))
                .ToArray();
            HistoricalSchoolTeachingBudget teachingBudget =
                HistoricalSchoolActivityQueue.CreateTeachingBudget(pYear);
            return new LecturePlanningState(pYear, teachingBudget,
                schoolOrder, schoolCount);
        }

        private static void ProcessLectureSchool(LecturePlanningState pPlanning)
        {
            if (pPlanning?.TeachingBudget == null ||
                pPlanning.Offset < 0 ||
                pPlanning.Offset >= pPlanning.SchoolOrder.Length) return;
            int schoolIndex = pPlanning.SchoolOrder[pPlanning.Offset];
            string schoolId = CourtSchoolRegistry.All[schoolIndex].Id;
            if (pPlanning.Candidates[schoolIndex] == null)
                pPlanning.Candidates[schoolIndex] = BuildSeniorTeachers(schoolId);
            int schoolAttempts = 0;
            while (pPlanning.Examined < MaxTeachersPerYear && schoolAttempts++ < 16)
            {
                int candidateIndex = pPlanning.CandidateIndices[schoolIndex]++;
                if (candidateIndex >= pPlanning.Candidates[schoolIndex].Count) break;
                LectureTeacherCandidate candidate =
                    pPlanning.Candidates[schoolIndex][candidateIndex];
                pPlanning.Examined++;
                if (!PlanLecture(candidate.Actor, pPlanning.Year,
                        candidate.DirectDiscipleCount,
                        pPlanning.TeachingBudget)) continue;
                pPlanning.Planned++;
                break;
            }
        }

        private static void AdvanceLecturePlanning(LecturePlanningState pPlanning)
        {
            if (pPlanning == null || pPlanning.SchoolOrder.Length == 0) return;
            pPlanning.Offset++;
            if (pPlanning.Offset < pPlanning.SchoolOrder.Length) return;
            pPlanning.Offset = 0;
            pPlanning.Pass++;
        }

        private static bool IsLecturePlanningComplete(LecturePlanningState pPlanning)
        {
            return pPlanning == null || pPlanning.TeachingBudget == null ||
                   pPlanning.SchoolOrder.Length == 0 || pPlanning.Pass >= 2 ||
                   pPlanning.Planned >=
                   HistoricalSchoolLectureRules.MaxWorldLecturesPerYear ||
                   pPlanning.Examined >= MaxTeachersPerYear;
        }

        private static bool FinishLecturePlanning(LecturePlanningState pPlanning)
        {
            if (pPlanning == null) return true;
            ScheduleDeferredActions(pPlanning.Year);
            _lastProcessedYear = pPlanning.Year;
            if (ReferenceEquals(_lecturePlanning, pPlanning)) _lecturePlanning = null;
            return true;
        }

        private static List<LectureTeacherCandidate> BuildSeniorTeachers(string pSchoolId)
        {
            var result = new List<LectureTeacherCandidate>(16);
            foreach (long actorId in
                     HistoricalSchoolRuntimeIndex.Instance.TeacherIds(pSchoolId))
            {
                Actor actor = FindActor(actorId);
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(actorId);
                if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                    membership == null || membership.SchoolId != pSchoolId) continue;
                int directDiscipleCount = HistoricalSchoolRuntimeIndex.Instance
                    .DirectDiscipleCount(actorId);
                var candidate = new LectureTeacherCandidate(actor,
                    membership.StartYear, directDiscipleCount);
                int insert = result.Count;
                while (insert > 0 && candidate.Precedes(result[insert - 1])) insert--;
                if (insert >= 16) continue;
                result.Insert(insert, candidate);
                if (result.Count > 16) result.RemoveAt(16);
            }
            return result;
        }

        private sealed class LectureTeacherCandidate
        {
            public LectureTeacherCandidate(Actor pActor, int pStartYear,
                int pDirectDiscipleCount)
            {
                Actor = pActor;
                StartYear = pStartYear;
                DirectDiscipleCount = Math.Max(0, pDirectDiscipleCount);
                HasDiscipleCapacity = HistoricalSchoolLectureRules
                    .HasDiscipleCapacity(DirectDiscipleCount,
                        SchoolLineageService.DirectDiscipleCap);
            }

            public Actor Actor { get; }
            public int DirectDiscipleCount { get; }
            private int StartYear { get; }
            private bool HasDiscipleCapacity { get; }

            public bool Precedes(LectureTeacherCandidate pOther)
            {
                return pOther == null ||
                       HistoricalSchoolLectureRules.TeacherPrecedesForLecture(
                           HasDiscipleCapacity, StartYear, Actor.data.id,
                           pOther.HasDiscipleCapacity, pOther.StartYear,
                           pOther.Actor.data.id);
            }
        }

        private static long[] BuildConversionCandidateIds(int pYear)
        {
            int schoolCount = CourtSchoolRegistry.All.Count;
            if (schoolCount == 0) return Array.Empty<long>();
            int start = PositiveModulo(pYear, schoolCount);
            var result = new List<long>(MaxExplicitActionsPerYear);
            for (int offset = 0; offset < schoolCount &&
                 result.Count < MaxExplicitActionsPerYear; offset++)
            {
                string schoolId = CourtSchoolRegistry.All[
                    (start + offset) % schoolCount].Id;
                long[] memberIds = HistoricalSchoolRuntimeIndex.Instance.MemberIds(schoolId);
                if (memberIds.Length == 0) continue;
                int selected = PositiveModulo(pYear + offset, memberIds.Length);
                result.Add(memberIds[selected]);
            }
            return result.ToArray();
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int result = pValue % pCount;
            return result < 0 ? result + pCount : result;
        }

        private static void ScheduleDeferredActions(int pYear)
        {
            if (pYear < 0 || !DeferredYears.Add(pYear)) return;
            while (DeferredActions.Count >= 2)
            {
                DeferredActionState dropped = DeferredActions.Dequeue();
                DeferredYears.Remove(dropped.Year);
            }
            DeferredActions.Enqueue(new DeferredActionState(
                pYear, BuildConversionCandidateIds(pYear)));
        }

        internal static bool ProcessDeferredFrame()
        {
            if (DeferredActions.Count == 0) return false;
            DeferredActionState state = DeferredActions.Peek();
            if (state.ActorIndex < state.ActorIds.Length)
            {
                Actor actor = FindActor(state.ActorIds[state.ActorIndex++]);
                if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                    HistoricalSchoolDescentService.IsCanonicalMaster(actor)) return true;
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(actor.data.id);
                City city = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                if (membership == null || city?.data == null || city.isRekt() ||
                    !HistoricalAffiliationService.IsPresentForInfluence(actor)) return true;
                if (!TrySelectRivalSchool(city, membership.SchoolId, state.Snapshots,
                        out string targetSchool, out float rivalExposure)) return true;
                int yearsWithoutTeacher = YearsWithoutOwnTeacher(actor, membership, state.Year);
                if (!HistoricalSchoolRules.CanExplicitlyConvert(false, yearsWithoutTeacher,
                        rivalExposure, true)) return true;
                string actionId = "ai_rival_conversion:" + state.Year + ":" + actor.data.id +
                    ":" + targetSchool;
                if (TryExplicitConversion(actor, targetSchool, yearsWithoutTeacher,
                        rivalExposure, actionId, state.Year)) state.Actions++;
                return true;
            }
            if (state.SchoolIndex < CourtSchoolRegistry.All.Count &&
                state.Rediscoveries < MaxRediscoveriesPerYear)
            {
                CourtSchoolDefinition school = CourtSchoolRegistry.All[state.SchoolIndex++];
                if (HistoricalSchoolRuntimeIndex.Instance.MemberCount(school.Id) > 0)
                    return true;
                IEnumerable<string> works = HistoricalSchoolMasterRegistry.All.Where(
                        p => p.SchoolId == school.Id)
                    .SelectMany(p => p.CanonicalWorks)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal);
                bool queuedRediscovery = false;
                foreach (string work in works)
                {
                    if (!HistoricalSchoolStore.HasPreservedWork(work, school.Id)) continue;
                    City city = FindCity(HistoricalSchoolStore.PreservedWorkCity(work, school.Id));
                    if (city?.data == null || city.isRekt()) continue;
                    IEnumerable<Actor> candidates = HistoricalSchoolRecruitCandidateCache
                        .Get(city, null, state.Year).Select(FindActor)
                        .Where(p => p?.data != null)
                        .OrderByDescending(CandidateScore)
                        .ThenBy(p => p.data.id);
                    foreach (Actor actor in candidates)
                    {
                        string actionId = "ai_rediscovery:" + state.Year + ":" + school.Id +
                            ":" + actor.data.id;
                        if (!TryRediscover(actor, school.Id, work, actionId, state.Year))
                            continue;
                        state.Rediscoveries++;
                        queuedRediscovery = true;
                        break;
                    }
                    if (queuedRediscovery) break;
                }
                return true;
            }
            DeferredActions.Dequeue();
            DeferredYears.Remove(state.Year);
            return true;
        }

        private sealed class DeferredActionState
        {
            public DeferredActionState(int pYear, long[] pActorIds)
            {
                Year = pYear;
                ActorIds = pActorIds ?? Array.Empty<long>();
            }

            public int Year { get; }
            public long[] ActorIds { get; }
            public Dictionary<long, CitySchoolSnapshot> Snapshots { get; } =
                new Dictionary<long, CitySchoolSnapshot>();
            public int ActorIndex { get; set; }
            public int SchoolIndex { get; set; }
            public int Actions { get; set; }
            public int Rediscoveries { get; set; }
        }

        private static bool TrySelectRivalSchool(City pCity, string pCurrentSchool,
            IDictionary<long, CitySchoolSnapshot> pSnapshots, out string pTargetSchool,
            out float pRivalExposure)
        {
            pTargetSchool = "";
            pRivalExposure = 0f;
            if (pCity?.data == null || pSnapshots == null) return false;
            if (!pSnapshots.TryGetValue(pCity.data.id, out CitySchoolSnapshot snapshot))
            {
                snapshot = CitySchoolSnapshotService.GetSnapshot(pCity);
                pSnapshots[pCity.data.id] = snapshot;
            }
            if (snapshot == null || snapshot.TotalScore <= 0f || snapshot.Scores == null)
                return false;
            float rivalScore = snapshot.Scores
                .Where(p => !string.Equals(p.Key, pCurrentSchool, StringComparison.Ordinal))
                .Sum(p => p.Value);
            if (rivalScore <= 0f) return false;
            KeyValuePair<string, float> target = snapshot.Scores
                .Where(p => !string.Equals(p.Key, pCurrentSchool, StringComparison.Ordinal))
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(target.Key) || CourtSchoolRegistry.Find(target.Key) == null)
                return false;
            pTargetSchool = target.Key;
            pRivalExposure = Math.Max(0f, Math.Min(1f, rivalScore / snapshot.TotalScore));
            return true;
        }

        private static int YearsWithoutOwnTeacher(Actor pActor,
            SchoolMembershipRecord pMembership, int pYear)
        {
            if (pActor?.data == null || pMembership == null) return 0;
            Actor teacher = FindActor(pMembership.TeacherActorId);
            City teacherResidence = HistoricalAffiliationService.ResidenceCity(teacher) ??
                teacher?.city;
            City studentResidence = HistoricalAffiliationService.ResidenceCity(pActor) ??
                pActor.city;
            long teacherCityId = teacherResidence?.data?.id ?? -1L;
            long studentCityId = studentResidence?.data?.id ?? -1L;
            SchoolMembershipRecord teacherMembership = teacher?.data == null
                ? null
                : SchoolMembershipService.GetActive(teacher.data.id);
            bool teacherAvailable = teacher != null &&
                SchoolLineageService.IsQualifiedTeacher(teacher) &&
                HistoricalAffiliationService.IsPresentForInfluence(teacher) &&
                teacherMembership?.SchoolId == pMembership.SchoolId &&
                teacherCityId >= 0 && teacherCityId == studentCityId;
            bool anotherTeacherAvailable = false;
            long[] teacherIds = HistoricalSchoolRuntimeIndex.Instance.ResidentTeacherIds(
                studentCityId, pMembership.SchoolId);
            for (int index = 0; index < teacherIds.Length; index++)
                if (teacherIds[index] != pActor.data.id)
                {
                    anotherTeacherAvailable = true;
                    break;
                }
            if (teacherAvailable || anotherTeacherAvailable)
            {
                pActor.data.set(LineageKeys.SCHOOL_TEACHER_GONE_YEAR, -1);
                return 0;
            }
            pActor.data.get(LineageKeys.SCHOOL_TEACHER_GONE_YEAR, out int goneYear, -1);
            if (goneYear < pMembership.StartYear || goneYear < 0)
            {
                goneYear = pYear;
                pActor.data.set(LineageKeys.SCHOOL_TEACHER_GONE_YEAR, goneYear);
            }
            return Math.Max(0, pYear - goneYear);
        }

        private static bool PlanLecture(Actor pTeacher, int pYear,
            int pDirectCount,
            HistoricalSchoolTeachingBudget pTeachingBudget)
        {
            if (pTeacher?.data == null || !pTeacher.isAlive() ||
                pTeacher.isRekt()) return false;
            SchoolMembershipRecord teacherMembership =
                SchoolMembershipService.GetActive(pTeacher.data.id);
            if (teacherMembership == null || pTeachingBudget == null) return false;
            City residence = HistoricalAffiliationService.ResidenceCity(pTeacher) ?? pTeacher.city;
            if (residence?.data == null || residence.isRekt() ||
                residence.kingdom?.data == null ||
                !HistoricalSchoolXiaAccessService.CanHostLecture(residence) ||
                !HistoricalAffiliationService.IsAvailableForOffice(pTeacher) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pTeacher)) return false;
            bool academyUsable =
                HistoricalSchoolAcademyService.FindUsable(residence) != null;
            bool academyBuildingPresent =
                HistoricalSchoolAcademyService.HasLiveAcademy(residence);
            if (SchoolAcademyConstructionRules.ShouldRequestForLecture(
                    cityValid: true, academyUsable, academyBuildingPresent))
                HistoricalSchoolAcademyConstructionService.TryStart(residence);
            if (!academyUsable) return false;
            bool canonical = HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher);
            var lectureCandidate = new HistoricalSchoolLectureCandidate(pTeacher.data.id,
                teacherMembership.SchoolId, residence.data.id, residence.kingdom.id,
                canonical, teacherMembership.StartYear, teacherMembership.Reputation,
                teacherMembership.Standing);
            if (!pTeachingBudget.TryPlan(lectureCandidate,
                    out HistoricalSchoolTeachingPlan plan)) return false;
            if (!HistoricalSchoolActivityQueue.TryEnqueueLecture(plan, pDirectCount))
                return false;
            return pTeachingBudget.Commit(plan);
        }

        internal static bool TryQueueLectureCommit(
            HistoricalSchoolLectureActivity pActivity)
        {
            if (!IsLectureCommitValid(pActivity)) return false;
            HistoricalSchoolTeachingPlan plan = pActivity.Plan;
            Actor teacher = FindActor(plan.Candidate.ActorId);
            City residence = HistoricalAffiliationService.ResidenceCity(teacher) ?? teacher?.city;

            Actor target = residence.kingdom?.king;
            long targetActorId = plan.IncludePersuasion
                ? target?.data?.id ?? -1L
                : -1L;
            string targetName = plan.IncludePersuasion ? target?.getName() ?? "" : "";
            var request = new HistoricalSchoolTeachingDbRequest(plan,
                teacher.getName() ?? "", targetActorId, targetName,
                World.world?.getCurWorldTime() ?? 0d);
            return HistoricalSchoolWriteBufferService.TryEnqueue(
                new LectureWriteOperation(pActivity, request));
        }

        internal static bool IsLectureCommitValid(
            HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null || !pActivity.Plan.IsValid) return false;
            HistoricalSchoolTeachingPlan plan = pActivity.Plan;
            Actor teacher = FindActor(plan.Candidate.ActorId);
            SchoolMembershipRecord teacherMembership = teacher?.data == null
                ? null
                : SchoolMembershipService.GetActive(teacher.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(teacher) ??
                             teacher?.city;
            return teacher?.data != null && teacher.isAlive() && !teacher.isRekt() &&
                   teacherMembership?.SchoolId == plan.Candidate.SchoolId &&
                   residence?.data != null && !residence.isRekt() &&
                   HistoricalSchoolXiaAccessService.CanHostLecture(residence) &&
                   residence.data.id == plan.Candidate.CityId &&
                   HistoricalAffiliationService.IsAvailableForOffice(teacher) &&
                   HistoricalAffiliationService.IsPresentForInfluence(teacher);
        }

        private static void ApplyCommittedLecture(
            HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null || !pActivity.Plan.IsValid) return;
            HistoricalSchoolTeachingPlan plan = pActivity.Plan;
            Actor teacher = FindActor(plan.Candidate.ActorId);
            SchoolMembershipRecord teacherMembership = teacher?.data == null
                ? null
                : SchoolMembershipService.GetActive(teacher.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(teacher) ?? teacher?.city;
            if (teacher?.data == null || !teacher.isAlive() || teacher.isRekt() ||
                teacherMembership == null || teacherMembership.SchoolId !=
                plan.Candidate.SchoolId || residence?.data == null || residence.isRekt() ||
                residence.data.id != plan.Candidate.CityId) return;

            HistoricalSchoolRevisionService.MarkActivity(plan.Candidate.SchoolId);
            ShowLectureEffect(teacher);
            if (plan.Announce)
                HistoricalSchoolContent.AnnounceLecture(teacher, residence,
                    teacherMembership.SchoolId);
            bool historicalTeacher = HistoricalSchoolDescentService.IsCanonicalMaster(teacher);
            if (historicalTeacher) RecordHistoricalWork(teacher, plan.Year);
            int annualLimit = HistoricalSchoolRules.AnnualDirectDiscipleLimit(
                teacher.data.id, plan.Year);
            int recruited = 0;
            IEnumerable<Actor> candidates = (pActivity.CandidateActorIds ?? Array.Empty<long>())
                .Select(FindActor)
                .Where(candidate => candidate?.data != null)
                .OrderByDescending(CandidateScore)
                .ThenBy(candidate => candidate.data.id);
            foreach (Actor candidate in candidates)
            {
                if (recruited >= annualLimit) break;
                bool sameResidence = (HistoricalAffiliationService.ResidenceCity(candidate) ??
                                      candidate.city)?.data?.id == residence.data.id;
                bool alreadyMember = SchoolMembershipService.GetActive(candidate.data.id) != null;
                if (!HistoricalSchoolRules.CanRecruitDisciple(pRealActor: true,
                        pAlive: candidate.isAlive() && !candidate.isRekt(),
                        pSameResidence: sameResidence, alreadyMember,
                        pActivity.DirectDiscipleCount + recruited,
                        SchoolLineageService.DirectDiscipleCap)) continue;
                SchoolMembershipSource source = historicalTeacher
                    ? SchoolMembershipSource.DirectDiscipleship
                    : SchoolMembershipSource.LaterDiscipleship;
                int generation = Math.Max(1, teacherMembership.Generation + 1);
                string sourceId = "teacher:" + teacher.data.id + ":year:" + plan.Year +
                    ":candidate:" + candidate.data.id;
                if (!SchoolMembershipService.TryQueueJoin(candidate,
                        teacherMembership.SchoolId, source, sourceId, teacher.data.id,
                        residence.data.id, generation,
                        Math.Max(10f, CandidateScore(candidate) * 0.1f),
                        "disciple_joined", teacher.data.id,
                        residence.kingdom?.data?.id ?? -1L, plan.Year, sourceId, 2,
                        success =>
                        {
                            if (!success) return;
                            CitySchoolSnapshotService.MarkDirty(residence);
                            HistoryWriter.RecordPerson(candidate.data.id,
                                candidate.kingdom, candidate.getName(),
                                "school_disciple",
                                HistoryText.Actor(candidate) +
                                HistoryLocalizationRules.H(
                                    "aw_hist_school_studied_under") +
                                HistoryText.Actor(teacher),
                                ChronicleCategory.LIFE);
                        })) continue;
                recruited++;
            }
            TryFoundInstitutionAfterLecture(teacher, residence, plan.Year);
        }

        private sealed class LectureWriteOperation : IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation
        {
            private readonly HistoricalSchoolLectureActivity _activity;
            private readonly HistoricalSchoolTeachingDbRequest _request;

            public LectureWriteOperation(HistoricalSchoolLectureActivity pActivity,
                HistoricalSchoolTeachingDbRequest pRequest)
            {
                _activity = pActivity;
                _request = pRequest;
            }

            public string OperationKey => _activity?.Plan.OperationKey ?? "";

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return HistoricalSchoolStore.RecordTeachingInTransaction(pDb,
                    pTransaction, _request).Outcome;
            }

            public IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                return new LectureBackgroundWrite(_request.CloneForBackground());
            }

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                try
                {
                    HistoricalSchoolStore.InvalidateTeachingCommit(
                        _activity.Plan.Candidate.CityId);
                    ApplyCommittedLecture(_activity);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Historical school lecture projection failed: " +
                                        error.Message);
                }
                finally
                {
                    HistoricalSchoolActivityQueue.OnLectureWriteResolved(_activity,
                        pOutcome);
                }
            }

            public void OnCleanFailure()
            {
                HistoricalSchoolActivityQueue.OnLectureWriteResolved(_activity,
                    HistoricalSchoolTeachingPersistenceOutcome.CleanFailure);
            }
        }

        private sealed class LectureBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly HistoricalSchoolTeachingDbRequest _request;

            public LectureBackgroundWrite(
                HistoricalSchoolTeachingDbRequest pRequest)
            {
                _request = pRequest ??
                    throw new ArgumentNullException(nameof(pRequest));
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return HistoricalSchoolStore.RecordTeachingInTransaction(pDb,
                    pTransaction, _request).Outcome;
            }
        }

        private static void ShowLectureEffect(Actor pTeacher)
        {
            try
            {
                if (pTeacher?.current_tile != null)
                    EffectsLibrary.spawnAtTileRandomScale("fx_experience_gain",
                        pTeacher.current_tile, 0.45f, 0.65f);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school lecture effect failed: " +
                                    error.Message);
            }
        }

        private static void TryFoundInstitutionAfterLecture(Actor pTeacher, City pCity,
            int pYear)
        {
            if (!HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher)) return;
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pTeacher);
            if (definition == null || pCity?.data == null) return;
            HistoricalSchoolStore.TryFoundInstitution(definition, pTeacher.data.id,
                pCity.data.id, pYear, World.world?.getCurWorldTime() ?? 0d);
        }

        public static bool RecordHistoricalWork(Actor pTeacher, int pYear)
        {
            if (pTeacher?.data == null || !pTeacher.isAlive() || pTeacher.isRekt() ||
                !HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher)) return false;
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pTeacher);
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pTeacher.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(pTeacher) ?? pTeacher.city;
            if (definition == null || membership == null || residence?.data == null ||
                residence.isRekt() || definition.CanonicalWorks.Count == 0 ||
                !HistoricalAffiliationService.IsPresentForInfluence(pTeacher)) return false;

            int start = Math.Abs((pYear + (int)(pTeacher.data.id % int.MaxValue)) %
                definition.CanonicalWorks.Count);
            for (int offset = 0; offset < definition.CanonicalWorks.Count; offset++)
            {
                string work = definition.CanonicalWorks[(start + offset) %
                    definition.CanonicalWorks.Count];
                if (string.IsNullOrWhiteSpace(work)) continue;
                if (!HistoricalSchoolStore.RecordSchoolWork(work, work, membership.SchoolId,
                        pTeacher.data.id, residence.data.id, pYear,
                        residence.kingdom?.data?.id ?? -1L)) continue;

                HistoryWriter.RecordPerson(pTeacher.data.id,
                    HistoricalAffiliationService.HomeKingdom(pTeacher) ?? pTeacher.kingdom,
                    definition.CanonicalName, "school_work_authored",
                    HistoryText.Actor(pTeacher, definition.CanonicalName) +
                    HistoryLocalizationRules.H("aw_hist_school_authored") +
                    HistoryText.PlainText(work), ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(residence, residence.kingdom, "school_work_authored",
                    HistoryText.Actor(pTeacher, definition.CanonicalName) +
                    HistoryLocalizationRules.H("aw_hist_school_authored") +
                    HistoryText.PlainText(work) +
                    HistoryLocalizationRules.H("aw_hist_school_preserved"));
                return true;
            }
            return false;
        }

        public static bool TryExplicitConversion(Actor pActor, string pTargetSchoolId,
            int pYearsWithoutOwnTeacher, float pRivalExposure, string pActionId,
            int pYear = -1)
        {
            return TryExplicitConversionCore(pActor, pTargetSchoolId,
                pYearsWithoutOwnTeacher, pRivalExposure, pActionId, pYear);
        }

        private static bool TryExplicitConversionCore(Actor pActor, string pTargetSchoolId,
            int pYearsWithoutOwnTeacher, float pRivalExposure, string pActionId,
            int pYear)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pActor) ||
                string.IsNullOrWhiteSpace(pActionId) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pActor)) return false;
            SchoolMembershipRecord current = SchoolMembershipService.GetActive(pActor.data.id);
            if (current == null || !HistoricalSchoolRules.CanExplicitlyConvert(false,
                    pYearsWithoutOwnTeacher, pRivalExposure, true)) return false;
            int eventYear = pYear >= 0 ? pYear : Date.getCurrentYear();
            if (eventYear < current.LoyaltyUntilYear) return false;
            if (string.Equals(current.SchoolId, pTargetSchoolId, StringComparison.Ordinal))
                return false;
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            if (city?.data == null || city.isRekt()) return false;
            string sourceId = "conversion:" + pActionId + ":actor:" + pActor.data.id;
            return SchoolMembershipService.TryQueueConversion(pActor, pTargetSchoolId,
                sourceId, city.data.id, "school_conversion", -1L,
                city.kingdom?.data?.id ?? -1L, eventYear, pActionId, 2,
                success =>
                {
                    if (!success) return;
                    CitySchoolSnapshotService.MarkDirty(city);
                    HistoryWriter.RecordPerson(pActor.data.id,
                        HistoricalAffiliationService.HomeKingdom(pActor) ?? pActor.kingdom,
                        pActor.getName(), "school_conversion",
                        HistoryText.Actor(pActor) +
                        HistoryLocalizationRules.H(
                            "aw_hist_school_converted_from") +
                        HistoryText.PlainText(
                            CaptiveTreatmentRules.SchoolLabel(
                                current.SchoolId)) +
                        HistoryLocalizationRules.H(
                            "aw_hist_school_converted_to") +
                        HistoryText.PlainText(
                            CaptiveTreatmentRules.SchoolLabel(
                                pTargetSchoolId)),
                        ChronicleCategory.SOCIAL);
                });
        }

        public static bool TryRediscover(Actor pReader, string pSchoolId, string pWorkKey,
            string pActionId, int pYear = -1)
        {
            return TryRediscoverCore(pReader, pSchoolId, pWorkKey, pActionId, pYear);
        }

        private static bool TryRediscoverCore(Actor pReader, string pSchoolId,
            string pWorkKey, string pActionId, int pYear)
        {
            if (pReader?.data == null || !pReader.isAlive() || pReader.isRekt() ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pReader) ||
                string.IsNullOrWhiteSpace(pWorkKey) || string.IsNullOrWhiteSpace(pActionId) ||
                SchoolMembershipService.GetActive(pReader.data.id) != null ||
                !HistoricalAffiliationService.IsPresentForInfluence(pReader)) return false;
            int livingMembers = HistoricalSchoolRuntimeIndex.Instance.MemberCount(pSchoolId);
            if (!HistoricalSchoolRules.CanRediscover(livingMembers,
                    HistoricalSchoolStore.HasPreservedWork(pWorkKey, pSchoolId), true)) return false;
            City city = HistoricalAffiliationService.ResidenceCity(pReader) ?? pReader.city;
            if (city?.data == null || city.isRekt()) return false;
            long sourceCityId = HistoricalSchoolStore.PreservedWorkCity(pWorkKey, pSchoolId);
            if (sourceCityId >= 0 && city.data.id != sourceCityId) return false;
            string sourceId = "rediscover:" + pActionId + ":" + pWorkKey +
                ":reader:" + pReader.data.id;
            int eventYear = pYear >= 0 ? pYear : Date.getCurrentYear();
            return SchoolMembershipService.TryQueueJoin(pReader, pSchoolId,
                SchoolMembershipSource.PreservedWork, sourceId, -1L, city.data.id, 0,
                20f, "school_rediscovery", -1L,
                city.kingdom?.data?.id ?? -1L, eventYear, pWorkKey, 3,
                success =>
                {
                    if (!success) return;
                    CitySchoolSnapshotService.MarkDirty(city);
                    HistoryWriter.RecordPerson(pReader.data.id,
                        HistoricalAffiliationService.HomeKingdom(pReader) ?? pReader.kingdom,
                        pReader.getName(), "school_rediscovery",
                        HistoryText.Actor(pReader) +
                        HistoryLocalizationRules.H(
                            "aw_hist_school_rediscovered") +
                        HistoryText.PlainText(pWorkKey) +
                        HistoryLocalizationRules.H(
                            "aw_hist_school_revived") +
                        HistoryText.PlainText(
                            CaptiveTreatmentRules.SchoolLabel(pSchoolId)),
                        ChronicleCategory.HONOR);
                    HistoryWriter.RecordCity(city, city.kingdom, "school_rediscovery",
                        HistoryText.Actor(pReader) +
                        HistoryLocalizationRules.H(
                            "aw_hist_school_revived") +
                        HistoryText.PlainText(
                            CaptiveTreatmentRules.SchoolLabel(pSchoolId)));
                });
        }

        private static float CandidateScore(Actor pActor)
        {
            try
            {
                return Math.Max(0f, (pActor.stats?["intelligence"] ?? 0f) * 1.5f +
                                    (pActor.stats?["diplomacy"] ?? 0f) +
                                    (pActor.stats?["stewardship"] ?? 0f) * 0.5f);
            }
            catch { return 0f; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
