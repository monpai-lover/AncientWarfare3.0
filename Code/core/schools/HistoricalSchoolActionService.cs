using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

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

        public static void ClearRuntime()
        {
            _lastProcessedYear = -1;
            DeferredActions.Clear();
            DeferredYears.Clear();
        }

        public static void ProcessYear(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            if (pYear < 0 || pYear == _lastProcessedYear || pMembers == null) return;
            _lastProcessedYear = pYear;

            IReadOnlyDictionary<long, int> directCounts = pMembers.DirectDiscipleCounts;
            Actor[] teachers = pMembers.QualifiedTeachers(pYear, MaxTeachersPerYear).ToArray();
            HistoricalSchoolTeachingBudget teachingBudget =
                HistoricalSchoolActivityQueue.CreateTeachingBudget(pYear);
            foreach (Actor teacher in teachers)
                PlanLecture(teacher, pYear, directCounts, teachingBudget);
            ScheduleDeferredActions(pYear, pMembers);
        }

        private static void ScheduleDeferredActions(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            if (pYear < 0 || pMembers == null || !DeferredYears.Add(pYear)) return;
            while (DeferredActions.Count >= 2)
            {
                DeferredActionState dropped = DeferredActions.Dequeue();
                DeferredYears.Remove(dropped.Year);
            }
            DeferredActions.Enqueue(new DeferredActionState(pYear,
                pMembers.LivingMembers().ToArray(), pMembers.BuildAvailableTeacherIndex()));
        }

        internal static bool ProcessDeferredFrame()
        {
            if (DeferredActions.Count == 0) return false;
            DeferredActionState state = DeferredActions.Peek();
            if (state.ActorIndex < state.Members.Length &&
                state.Actions < MaxExplicitActionsPerYear)
            {
                Actor actor = state.Members[state.ActorIndex++];
                if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                    HistoricalSchoolDescentService.IsCanonicalMaster(actor)) return true;
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(actor.data.id);
                City city = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                if (membership == null || city?.data == null || city.isRekt() ||
                    !HistoricalAffiliationService.IsPresentForInfluence(actor)) return true;
                if (!TrySelectRivalSchool(city, membership.SchoolId, state.Snapshots,
                        out string targetSchool, out float rivalExposure)) return true;
                int yearsWithoutTeacher = YearsWithoutOwnTeacher(actor, membership, state.Year,
                    state.AvailableTeachers);
                if (!HistoricalSchoolRules.CanExplicitlyConvert(false, yearsWithoutTeacher,
                        rivalExposure, true)) return true;
                string actionId = "ai_rival_conversion:" + state.Year + ":" + actor.data.id +
                    ":" + targetSchool;
                if (TryExplicitConversion(actor, targetSchool, yearsWithoutTeacher,
                        rivalExposure, actionId, state.Year, null)) state.Actions++;
                return true;
            }
            state.ActorIndex = state.Members.Length;
            if (state.SchoolIndex < CourtSchoolRegistry.All.Count &&
                state.Rediscoveries < MaxRediscoveriesPerYear)
            {
                CourtSchoolDefinition school = CourtSchoolRegistry.All[state.SchoolIndex++];
                if (SchoolMembershipService.LivingCount(school.Id) > 0) return true;
                IEnumerable<string> works = HistoricalSchoolMasterRegistry.All.Where(
                        p => p.SchoolId == school.Id)
                    .SelectMany(p => p.CanonicalWorks)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.Ordinal);
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
                        if (!TryRediscover(actor, school.Id, work, actionId, state.Year,
                                null, pUseTargetedLivingCount: true)) continue;
                        state.Rediscoveries++;
                        break;
                    }
                    if (SchoolMembershipService.LivingCount(school.Id) > 0) break;
                }
                return true;
            }
            DeferredActions.Dequeue();
            DeferredYears.Remove(state.Year);
            return true;
        }

        private sealed class DeferredActionState
        {
            public DeferredActionState(int pYear, Actor[] pMembers,
                Dictionary<string, HashSet<long>> pAvailableTeachers)
            {
                Year = pYear;
                Members = pMembers ?? Array.Empty<Actor>();
                AvailableTeachers = pAvailableTeachers ??
                                    new Dictionary<string, HashSet<long>>(
                                        StringComparer.Ordinal);
            }

            public int Year { get; }
            public Actor[] Members { get; }
            public Dictionary<string, HashSet<long>> AvailableTeachers { get; }
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
            SchoolMembershipRecord pMembership, int pYear,
            IReadOnlyDictionary<string, HashSet<long>> pAvailableTeachers)
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
            string residenceKey = TeacherResidenceKey(pMembership.SchoolId, studentCityId);
            if (pAvailableTeachers != null && pAvailableTeachers.TryGetValue(residenceKey,
                    out HashSet<long> teacherIds))
                anotherTeacherAvailable = teacherIds.Any(p => p != pActor.data.id);
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

        private static string TeacherResidenceKey(string pSchoolId, long pCityId)
        {
            return (pSchoolId ?? "") + ":" + pCityId;
        }

        private static bool PlanLecture(Actor pTeacher, int pYear,
            IReadOnlyDictionary<long, int> pDirectCounts,
            HistoricalSchoolTeachingBudget pTeachingBudget)
        {
            SchoolMembershipRecord teacherMembership =
                SchoolMembershipService.GetActive(pTeacher.data.id);
            if (teacherMembership == null || pTeachingBudget == null) return true;
            City residence = HistoricalAffiliationService.ResidenceCity(pTeacher) ?? pTeacher.city;
            if (residence?.data == null || residence.isRekt() ||
                residence.kingdom?.data == null ||
                pTeacher.city?.data?.id != residence.data.id ||
                !HistoricalAffiliationService.IsAvailableForOffice(pTeacher) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pTeacher)) return true;
            bool canonical = HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher);
            var lectureCandidate = new HistoricalSchoolLectureCandidate(pTeacher.data.id,
                teacherMembership.SchoolId, residence.data.id, residence.kingdom.id,
                canonical, teacherMembership.StartYear, teacherMembership.Reputation,
                teacherMembership.Standing);
            if (!pTeachingBudget.TryPlan(lectureCandidate,
                    out HistoricalSchoolTeachingPlan plan)) return true;
            int directCount = 0;
            if (pDirectCounts != null)
                pDirectCounts.TryGetValue(pTeacher.data.id, out directCount);
            if (!HistoricalSchoolActivityQueue.TryEnqueueLecture(plan, directCount))
                return true;
            return pTeachingBudget.Commit(plan);
        }

        internal static HistoricalSchoolTeachingPersistenceOutcome CommitQueuedLecture(
            HistoricalSchoolLectureActivity pActivity)
        {
            if (pActivity == null || !pActivity.Plan.IsValid)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            HistoricalSchoolTeachingPlan plan = pActivity.Plan;
            Actor teacher = FindActor(plan.Candidate.ActorId);
            SchoolMembershipRecord teacherMembership = teacher?.data == null
                ? null
                : SchoolMembershipService.GetActive(teacher.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(teacher) ?? teacher?.city;
            if (teacher?.data == null || !teacher.isAlive() || teacher.isRekt() ||
                teacherMembership == null || teacherMembership.SchoolId !=
                plan.Candidate.SchoolId || residence?.data == null || residence.isRekt() ||
                residence.data.id != plan.Candidate.CityId ||
                teacher.city?.data?.id != residence.data.id ||
                !HistoricalAffiliationService.IsAvailableForOffice(teacher) ||
                !HistoricalAffiliationService.IsPresentForInfluence(teacher))
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;

            Actor target = residence.kingdom?.king;
            long targetActorId = plan.IncludePersuasion
                ? target?.data?.id ?? -1L
                : -1L;
            string targetName = plan.IncludePersuasion ? target?.getName() ?? "" : "";
            HistoricalSchoolTeachingDbResult teachingResult =
                HistoricalSchoolStore.RecordTeaching(plan, teacher.getName() ?? "",
                    targetActorId, targetName,
                    World.world?.getCurWorldTime() ?? 0d);
            if (!teachingResult.IsCommitted) return teachingResult.Outcome;
            if (!teachingResult.PersistedNew) return teachingResult.Outcome;

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
                if (!SchoolMembershipService.TryJoin(candidate, teacherMembership.SchoolId,
                        source, sourceId, teacher.data.id, residence.data.id, generation,
                        pInitialReputation: Math.Max(10f, CandidateScore(candidate) * 0.1f)))
                    continue;
                if (!HistoricalSchoolStore.RecordSchoolEvent("disciple_joined",
                    candidate.data.id,
                    teacher.data.id, teacherMembership.SchoolId, residence.data.id,
                    residence.kingdom?.data?.id ?? -1L, plan.Year, sourceId, 2,
                    World.world?.getCurWorldTime() ?? 0d))
                {
                    if (!SchoolMembershipService.RollbackJoin(candidate, sourceId))
                        ModClass.LogWarning("Historical school disciple rollback failed");
                    CitySchoolSnapshotService.MarkDirty(residence);
                    continue;
                }
                CitySchoolSnapshotService.MarkDirty(residence);
                recruited++;
                HistoryWriter.RecordPerson(candidate.data.id, candidate.kingdom,
                    candidate.getName(), "school_disciple", candidate.getName() +
                    " studied under " + teacher.getName(), ChronicleCategory.LIFE);
            }
            TryFoundInstitutionAfterLecture(teacher, residence, plan.Year);
            return teachingResult.Outcome;
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
                    definition.CanonicalName + " authored " + work, ChronicleCategory.HONOR);
                HistoryWriter.RecordCity(residence, residence.kingdom, "school_work_authored",
                    definition.CanonicalName + " preserved " + work);
                return true;
            }
            return false;
        }

        public static bool TryExplicitConversion(Actor pActor, string pTargetSchoolId,
            int pYearsWithoutOwnTeacher, float pRivalExposure, string pActionId,
            int pYear = -1)
        {
            return TryExplicitConversion(pActor, pTargetSchoolId, pYearsWithoutOwnTeacher,
                pRivalExposure, pActionId, pYear, null);
        }

        private static bool TryExplicitConversion(Actor pActor, string pTargetSchoolId,
            int pYearsWithoutOwnTeacher, float pRivalExposure, string pActionId,
            int pYear, HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
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
            if (!SchoolMembershipService.TryConvert(pActor, pTargetSchoolId,
                    "conversion:" + pActionId + ":actor:" + pActor.data.id,
                    city.data.id)) return false;
            if (!HistoricalSchoolStore.RecordSchoolEvent("school_conversion", pActor.data.id,
                -1,
                pTargetSchoolId, city.data.id, city.kingdom?.data?.id ?? -1L,
                eventYear, pActionId, 2, World.world?.getCurWorldTime() ?? 0d))
            {
                if (!SchoolMembershipService.RollbackConversion(pActor, current))
                    ModClass.LogWarning("Historical school conversion rollback failed");
                return false;
            }
            SchoolMembershipRecord replacement =
                SchoolMembershipService.GetActive(pActor.data.id);
            if (pMembers != null && (replacement == null ||
                !pMembers.ApplyMembershipChange(current, replacement, pActor)))
                ModClass.LogWarning("Annual school member snapshot missed conversion");
            CitySchoolSnapshotService.MarkDirty(city);
            HistoryWriter.RecordPerson(pActor.data.id,
                HistoricalAffiliationService.HomeKingdom(pActor) ?? pActor.kingdom,
                pActor.getName(), "school_conversion",
                "Converted from " + current.SchoolId + " to " + pTargetSchoolId,
                ChronicleCategory.SOCIAL);
            return true;
        }

        public static bool TryRediscover(Actor pReader, string pSchoolId, string pWorkKey,
            string pActionId, int pYear = -1)
        {
            return TryRediscover(pReader, pSchoolId, pWorkKey, pActionId, pYear,
                null, pUseTargetedLivingCount: true);
        }

        private static bool TryRediscover(Actor pReader, string pSchoolId, string pWorkKey,
            string pActionId, int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers,
            bool pUseTargetedLivingCount = false)
        {
            if (pReader?.data == null || !pReader.isAlive() || pReader.isRekt() ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pReader) ||
                string.IsNullOrWhiteSpace(pWorkKey) || string.IsNullOrWhiteSpace(pActionId) ||
                SchoolMembershipService.GetActive(pReader.data.id) != null ||
                !HistoricalAffiliationService.IsPresentForInfluence(pReader)) return false;
            int livingMembers;
            if (pMembers != null)
            {
                if (!pMembers.TryGetLivingCount(pSchoolId, out livingMembers)) return false;
            }
            else
                livingMembers = pUseTargetedLivingCount
                    ? SchoolMembershipService.LivingCount(pSchoolId)
                    : 0;
            if (!HistoricalSchoolRules.CanRediscover(livingMembers,
                    HistoricalSchoolStore.HasPreservedWork(pWorkKey, pSchoolId), true)) return false;
            City city = HistoricalAffiliationService.ResidenceCity(pReader) ?? pReader.city;
            if (city?.data == null || city.isRekt()) return false;
            long sourceCityId = HistoricalSchoolStore.PreservedWorkCity(pWorkKey, pSchoolId);
            if (sourceCityId >= 0 && city.data.id != sourceCityId) return false;
            string sourceId = "rediscover:" + pActionId + ":" + pWorkKey +
                ":reader:" + pReader.data.id;
            if (!SchoolMembershipService.TryJoin(pReader, pSchoolId,
                    SchoolMembershipSource.PreservedWork, sourceId, -1, city.data.id, 0,
                    pInitialReputation: 20f)) return false;
            int eventYear = pYear >= 0 ? pYear : Date.getCurrentYear();
            if (!HistoricalSchoolStore.RecordSchoolEvent("school_rediscovery", pReader.data.id,
                -1,
                pSchoolId, city.data.id, city.kingdom?.data?.id ?? -1L,
                eventYear, pWorkKey, 3, World.world?.getCurWorldTime() ?? 0d))
            {
                if (!SchoolMembershipService.RollbackJoin(pReader, sourceId))
                    ModClass.LogWarning("Historical school rediscovery rollback failed");
                CitySchoolSnapshotService.MarkDirty(city);
                return false;
            }
            SchoolMembershipRecord joined =
                SchoolMembershipService.GetActive(pReader.data.id);
            if (pMembers != null && (joined == null ||
                !pMembers.ApplyMembershipChange(null, joined, pReader)))
                ModClass.LogWarning("Annual school member snapshot missed rediscovery");
            CitySchoolSnapshotService.MarkDirty(city);
            HistoryWriter.RecordPerson(pReader.data.id,
                HistoricalAffiliationService.HomeKingdom(pReader) ?? pReader.kingdom,
                pReader.getName(), "school_rediscovery",
                "Rediscovered " + pWorkKey + " and joined " + pSchoolId,
                ChronicleCategory.HONOR);
            HistoryWriter.RecordCity(city, city.kingdom, "school_rediscovery",
                pReader.getName() + " revived " + pSchoolId);
            return true;
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
