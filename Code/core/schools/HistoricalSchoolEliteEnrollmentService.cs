using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolEliteEnrollmentService
    {
        private readonly struct TeacherSelection
        {
            public TeacherSelection(Actor pActor,
                SchoolMembershipRecord pMembership)
            {
                Actor = pActor;
                Membership = pMembership;
            }

            public Actor Actor { get; }
            public SchoolMembershipRecord Membership { get; }
            public bool IsValid => Actor?.data != null && Membership != null;
        }

        private readonly struct AcademyCandidate
        {
            public AcademyCandidate(Actor pActor, float pScore)
            {
                Actor = pActor;
                Score = pScore;
            }

            public Actor Actor { get; }
            public float Score { get; }
        }

        private static readonly Dictionary<long, Dictionary<long,
                HistoricalSchoolEliteCandidate>> PriorityCandidatesByRealm =
            new Dictionary<long, Dictionary<long,
                HistoricalSchoolEliteCandidate>>();
        private static readonly Dictionary<long, Kingdom> Realms =
            new Dictionary<long, Kingdom>();
        private static readonly List<long> RealmIds = new List<long>();
        private static readonly List<HistoricalSchoolEliteCandidate>
            PlanningCandidates = new List<HistoricalSchoolEliteCandidate>();
        private static readonly List<HistoricalSchoolEliteCandidate> Work =
            new List<HistoricalSchoolEliteCandidate>();
        private static readonly Dictionary<long, int> QueuedByRealm =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> RealmJoinLimits =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> RealmBaseJoinLimits =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int> QueuedByAcademyCity =
            new Dictionary<long, int>();
        private static readonly Dictionary<long, int>
            AdmissionReservationsByRealm = new Dictionary<long, int>();
        private static readonly Dictionary<long, int>
            AdmissionLimitsByRealm = new Dictionary<long, int>();
        private static readonly Dictionary<long, int>
            BaseAdmissionLimitsByRealm = new Dictionary<long, int>();
        private static readonly Dictionary<long, int>
            PendingAdmissionsByTeacher = new Dictionary<long, int>();
        private static readonly Dictionary<long, List<City>> CitiesByRealm =
            new Dictionary<long, List<City>>();

        private static int _admissionReservationYear = -1;
        private static int _workYear = -1;
        private static int _workIndex;
        private static int _realmIndex;
        private static bool _workReady;

        public static void MarkPriority(Actor pActor, Kingdom pKingdom,
            HistoricalSchoolElitePriority pPriority)
        {
            long actorId = pActor?.data?.id ?? -1L;
            long kingdomId = pKingdom?.data?.id ?? -1L;
            if (actorId < 0 || kingdomId < 0) return;
            var candidate = new HistoricalSchoolEliteCandidate(
                kingdomId, actorId, pPriority, pCityId: -1L,
                pAge: SafeAge(pActor), pExamPipelineEligible:
                IsExamPipelineAdmissionEligible(pActor, pKingdom, pPriority));
            if (!PriorityCandidatesByRealm.TryGetValue(kingdomId,
                    out Dictionary<long,
                        HistoricalSchoolEliteCandidate> realmCandidates))
            {
                realmCandidates = new Dictionary<long,
                    HistoricalSchoolEliteCandidate>();
                PriorityCandidatesByRealm.Add(kingdomId, realmCandidates);
            }
            if (!realmCandidates.ContainsKey(actorId) &&
                realmCandidates.Count >=
                HistoricalSchoolEliteEnrollmentRules
                    .MaxCandidateAttemptsPerRealmPerYear * 2) return;
            if (!realmCandidates.TryGetValue(actorId,
                    out HistoricalSchoolEliteCandidate existing) ||
                candidate.Priority < existing.Priority)
                realmCandidates[actorId] = candidate;
        }

        public static bool ProcessYearFrame(int pYear,
            IReadOnlyList<City> pLivingXiaCities)
        {
            if (pYear < 0) return true;
            if (_workYear != pYear) BeginYear(pYear, pLivingXiaCities);
            if (!_workReady)
            {
                int remainingRealms = RealmIds.Count - _realmIndex;
                int preparationBudget = HistoricalSchoolEliteEnrollmentRules
                    .RealmPreparationBudget(remainingRealms);
                for (int i = 0; i < preparationBudget; i++)
                {
                    long realmId = RealmIds[_realmIndex++];
                    AddRealmCandidates(Realms[realmId], PlanningCandidates);
                }
                if (_realmIndex < RealmIds.Count) return false;
                Work.AddRange(HistoricalSchoolEliteEnrollmentRules
                    .SelectCandidates(PlanningCandidates, pYear,
                        HistoricalSchoolEliteEnrollmentRules
                            .MaxCandidateAttemptsPerRealmPerYear,
                        RealmBaseJoinLimits, RealmJoinLimits));
                PlanningCandidates.Clear();
                _workReady = true;
                if (Work.Count == 0)
                {
                    FinishYear();
                    return true;
                }
                return false;
            }
            int remaining = Work.Count - _workIndex;
            if (HistoricalSchoolEliteEnrollmentRules.FrameAttemptBudget(
                    remaining) <= 0)
            {
                FinishYear();
                return true;
            }

            HistoricalSchoolEliteCandidate candidate = Work[_workIndex++];
            TryQueue(candidate, pYear);
            if (_workIndex < Work.Count) return false;
            FinishYear();
            return true;
        }

        public static void ClearRuntime()
        {
            PriorityCandidatesByRealm.Clear();
            Realms.Clear();
            RealmIds.Clear();
            PlanningCandidates.Clear();
            Work.Clear();
            QueuedByRealm.Clear();
            RealmJoinLimits.Clear();
            RealmBaseJoinLimits.Clear();
            QueuedByAcademyCity.Clear();
            AdmissionReservationsByRealm.Clear();
            AdmissionLimitsByRealm.Clear();
            BaseAdmissionLimitsByRealm.Clear();
            PendingAdmissionsByTeacher.Clear();
            CitiesByRealm.Clear();
            _admissionReservationYear = -1;
            _workYear = -1;
            _workIndex = 0;
            _realmIndex = 0;
            _workReady = false;
        }

        private static void BeginYear(int pYear,
            IReadOnlyList<City> pLivingXiaCities)
        {
            Work.Clear();
            Realms.Clear();
            RealmIds.Clear();
            PlanningCandidates.Clear();
            QueuedByRealm.Clear();
            RealmJoinLimits.Clear();
            RealmBaseJoinLimits.Clear();
            QueuedByAcademyCity.Clear();
            CitiesByRealm.Clear();
            EnsureAdmissionReservationYear(pYear);
            _workYear = pYear;
            _workIndex = 0;
            _realmIndex = 0;
            _workReady = false;

            if (pLivingXiaCities != null)
                for (int i = 0; i < pLivingXiaCities.Count; i++)
                {
                    Kingdom kingdom = pLivingXiaCities[i]?.kingdom;
                    if (kingdom?.data == null || kingdom.isRekt() ||
                        kingdom.isNeutral()) continue;
                    Realms[kingdom.id] = kingdom;
                    if (!CitiesByRealm.TryGetValue(kingdom.id,
                            out List<City> cities))
                    {
                        cities = new List<City>();
                        CitiesByRealm.Add(kingdom.id, cities);
                    }
                    City city = pLivingXiaCities[i];
                    if (city?.data != null && !city.isRekt()) cities.Add(city);
                }
            foreach (List<City> cities in CitiesByRealm.Values)
                cities.Sort((left, right) =>
                    left.data.id.CompareTo(right.data.id));
            RealmIds.AddRange(Realms.Keys);
            RealmIds.Sort();
        }

        private static void FinishYear()
        {
            Work.Clear();
            Realms.Clear();
            RealmIds.Clear();
            PlanningCandidates.Clear();
            QueuedByRealm.Clear();
            RealmJoinLimits.Clear();
            RealmBaseJoinLimits.Clear();
            QueuedByAcademyCity.Clear();
            CitiesByRealm.Clear();
            _workYear = -1;
            _workIndex = 0;
            _realmIndex = 0;
            _workReady = false;
        }

        private static void AddRealmCandidates(Kingdom pKingdom,
            List<HistoricalSchoolEliteCandidate> pCandidates)
        {
            if (pKingdom?.data == null) return;
            bool examinationEnabled = CivilServiceQualificationService.
                HasExaminationSystem(pKingdom);
            if (PriorityCandidatesByRealm.TryGetValue(pKingdom.id,
                    out Dictionary<long,
                        HistoricalSchoolEliteCandidate> priority))
                foreach (HistoricalSchoolEliteCandidate candidate in
                         priority.Values)
                {
                    Actor actor = FindActor(candidate.ActorId);
                    pCandidates.Add(new HistoricalSchoolEliteCandidate(
                        candidate.KingdomId, candidate.ActorId,
                        candidate.Priority, candidate.CityId, candidate.Age,
                        IsExamPipelineAdmissionEligible(actor, pKingdom,
                            candidate.Priority)));
                }
            pKingdom.data.get(LineageKeys.SCHOOL_NOBLE_EDUCATION_CURSOR,
                out long nobleCursor, -1L);
            IReadOnlyList<long> nobleActorIds = LineageArchiveReader.
                ReadLivingNobleActorIds(pKingdom.id, nobleCursor,
                    HistoricalSchoolEliteEnrollmentRules.
                        MaxNobleArchiveRowsPerRealmYear,
                    out long nextNobleCursor);
            pKingdom.data.set(LineageKeys.SCHOOL_NOBLE_EDUCATION_CURSOR,
                nextNobleCursor);
            for (int i = 0; i < nobleActorIds.Count; i++)
                AddCandidate(pCandidates, pKingdom,
                    FindActor(nobleActorIds[i]),
                    HistoricalSchoolElitePriority.UntitledNoble);
            pKingdom.data.get(
                LineageKeys.SCHOOL_DECLINED_NOBLE_EDUCATION_CURSOR,
                out long declinedNobleCursor, -1L);
            IReadOnlyList<long> declinedNobleActorIds = LineageArchiveReader.
                ReadLivingDeclinedNobleActorIds(pKingdom.id,
                    declinedNobleCursor,
                    HistoricalSchoolEliteEnrollmentRules.
                        MaxNobleArchiveRowsPerRealmYear,
                    out long nextDeclinedNobleCursor);
            pKingdom.data.set(
                LineageKeys.SCHOOL_DECLINED_NOBLE_EDUCATION_CURSOR,
                nextDeclinedNobleCursor);
            for (int i = 0; i < declinedNobleActorIds.Count; i++)
                AddCandidate(pCandidates, pKingdom,
                    FindActor(declinedNobleActorIds[i]),
                    HistoricalSchoolElitePriority.DeclinedNoble);
            AddCandidate(pCandidates, pKingdom, pKingdom.king,
                HistoricalSchoolElitePriority.Ruler);
            AddCandidate(pCandidates, pKingdom,
                HeirService.PeekRegisteredHeir(pKingdom),
                HistoricalSchoolElitePriority.Heir);

            IReadOnlyList<FeudatorySnapshot> feudatories =
                FeudatoryService.GetByKingdom(pKingdom.id);
            for (int i = 0; i < feudatories.Count; i++)
                AddCandidate(pCandidates, pKingdom,
                    FindActor(feudatories[i].PrinceActorId),
                    HistoricalSchoolElitePriority.FeudatoryPrince);

            List<CourtOfficerView> officers = CourtService.GetActiveOfficers(
                pKingdom,
                HistoricalSchoolEliteEnrollmentRules
                    .MaxCandidateAttemptsPerRealmPerYear);
            for (int i = 0; i < officers.Count; i++)
            {
                CourtOfficerView officer = officers[i];
                if (officer == null) continue;
                AddCandidate(pCandidates, pKingdom,
                    FindActor(officer.actor_id),
                    officer.layer == CourtOfficeLayer.City
                        ? HistoricalSchoolElitePriority.LocalOfficial
                        : HistoricalSchoolElitePriority.CentralOfficial);
            }

            IReadOnlyList<long> titled = NobleRankService
                .GetActiveTitleHolderIds(pKingdom.id,
                    HistoricalSchoolEliteEnrollmentRules
                        .MaxCandidateAttemptsPerRealmPerYear);
            for (int i = 0; i < titled.Count; i++)
                AddCandidate(pCandidates, pKingdom, FindActor(titled[i]),
                    HistoricalSchoolElitePriority.TitledNoble);

            CitiesByRealm.TryGetValue(pKingdom.id,
                out List<City> realmCities);
            int academyCount = AddAcademyCandidates(pKingdom, realmCities,
                pCandidates);
            int teacherCount = CountQualifiedTeachers(pKingdom);
            int candidateTarget = examinationEnabled
                ? CivilServiceExamService.CandidateTargetForRealm(pKingdom)
                : 0;
            int eligibleLocalCandidates = examinationEnabled
                ? CivilServiceExamCandidateQuery.
                    CountEligibleLocalForExamPipeline(pKingdom, _workYear,
                        candidateTarget)
                : 0;
            int baseJoinLimit = HistoricalSchoolEliteEnrollmentRules.
                RealmSuccessfulJoinLimit(teacherCount, academyCount);
            int joinLimit = HistoricalSchoolEliteEnrollmentRules.
                RealmSuccessfulJoinLimitForExamPipeline(examinationEnabled,
                    teacherCount, academyCount, eligibleLocalCandidates,
                    candidateTarget);
            RealmJoinLimits[pKingdom.id] = joinLimit;
            RealmBaseJoinLimits[pKingdom.id] = baseJoinLimit;
            AdmissionLimitsByRealm[pKingdom.id] = joinLimit;
            BaseAdmissionLimitsByRealm[pKingdom.id] = baseJoinLimit;
            SetAdmissionLimits(pKingdom, _workYear, baseJoinLimit, joinLimit);
        }

        private static void AddCandidate(
            List<HistoricalSchoolEliteCandidate> pCandidates,
            Kingdom pKingdom, Actor pActor,
            HistoricalSchoolElitePriority pPriority)
        {
            if (pActor?.data == null || pKingdom?.data == null) return;
            bool hasMembership = SchoolMembershipService.GetActive(
                pActor.data.id) != null;
            bool pending = SchoolMembershipService.IsJoinPending(
                pActor.data.id);
            if (!HistoricalSchoolEliteEnrollmentRules.NeedsEnrollment(
                    isValid: true, hasMembership, pending)) return;
            pCandidates.Add(new HistoricalSchoolEliteCandidate(
                pKingdom.id, pActor.data.id, pPriority, pCityId: -1L,
                pAge: SafeAge(pActor), pExamPipelineEligible:
                IsExamPipelineAdmissionEligible(pActor, pKingdom, pPriority)));
        }

        private static void TryQueue(HistoricalSchoolEliteCandidate pCandidate,
            int pYear)
        {
            bool academyCommoner = pCandidate.Priority ==
                                    HistoricalSchoolElitePriority.
                                        AcademyCommoner;
            if (academyCommoner &&
                QueuedAcademyCount(pCandidate.CityId) >=
                HistoricalSchoolEliteEnrollmentRules.
                    MaxCommonerAdmissionsPerAcademyYear) return;
            Kingdom kingdom = FindKingdom(pCandidate.KingdomId);
            Actor actor = FindActor(pCandidate.ActorId);
            bool valid = IsValidCandidate(actor, kingdom, pCandidate);
            bool examPipelineEligible = IsExamPipelineAdmissionEligible(actor,
                kingdom, pCandidate.Priority);
            if (!HistoricalSchoolEliteEnrollmentRules.CanUseAdmissionSlot(
                    QueuedCount(pCandidate.KingdomId),
                    RealmBaseJoinLimit(pCandidate.KingdomId),
                    RealmJoinLimit(pCandidate.KingdomId),
                    examPipelineEligible)) return;
            bool hasMembership = actor?.data != null &&
                                 SchoolMembershipService.GetActive(
                                     actor.data.id) != null;
            bool pending = actor?.data != null &&
                           SchoolMembershipService.IsJoinPending(
                               actor.data.id);
            if (!HistoricalSchoolEliteEnrollmentRules.NeedsEnrollment(
                    valid, hasMembership, pending))
            {
                if (!valid || hasMembership)
                    RemovePriority(pCandidate.KingdomId,
                        pCandidate.ActorId);
                return;
            }

            City residence = HistoricalAffiliationService.ResidenceCity(actor) ??
                             actor.city;
            if (residence?.data == null || residence.isRekt()) return;
            if (academyCommoner &&
                (residence.data.id != pCandidate.CityId ||
                 HistoricalSchoolAcademyService.FindUsable(residence) == null))
                return;
            if (!academyCommoner && HistoricalSchoolEducationJourneyService.
                    TryResumePending(actor))
            {
                RecordQueued(pCandidate);
                return;
            }
            TeacherSelection teacher = SelectTeacher(actor, residence,
                pCandidate.KingdomId, pYear,
                pLocalOnly: academyCommoner);
            if (!teacher.IsValid) return;
            City teacherResidence = HistoricalAffiliationService.
                ResidenceCity(teacher.Actor) ?? teacher.Actor.city;
            bool sameCity = teacherResidence?.data?.id == residence.data.id;
            bool sameRealm = teacherResidence?.kingdom?.data?.id ==
                             pCandidate.KingdomId;
            if (!HistoricalSchoolEducationRules.CanSelectTeacher(sameCity,
                    sameRealm, academyCommoner)) return;
            if (HistoricalSchoolEducationRules.RequiresJourney(sameCity,
                    sameRealm, academyCommoner))
            {
                bool begun = HistoricalSchoolEducationJourneyService.TryBegin(
                    actor, teacher.Actor, teacher.Membership, residence,
                    teacherResidence, pCandidate.KingdomId, pYear);
                if (begun) RecordQueued(pCandidate);
                return;
            }
            if (!HistoricalSchoolEducationRules.CanCommitAdmission(sameCity,
                    arrivedAtDestination: true, teacherValid: true)) return;
            bool queued = TryQueueAdmission(actor, teacher.Actor,
                teacher.Membership, residence, pCandidate.KingdomId, pYear);
            if (!queued) return;
            RecordQueued(pCandidate);
        }

        internal static bool TryQueueAdmission(Actor pActor, Actor pTeacher,
            SchoolMembershipRecord pTeacherMembership, City pEducationCity,
            long pKingdomId, int pSourceYear,
            Action<bool> pCompletion = null)
        {
            if (pActor?.data == null || pTeacher?.data == null ||
                pTeacherMembership == null || pEducationCity?.data == null)
                return false;
            int eventYear = Date.getCurrentYear();
            EnsureAdmissionReservationYear(eventYear);
            Kingdom kingdom = FindKingdom(pKingdomId);
            bool examPipelineEligible = IsExamPipelineAdmissionEligible(
                pActor, kingdom);
            if (!TryReserveAdmission(pKingdomId, examPipelineEligible))
                return false;
            if (!TryReserveTeacher(pTeacher.data.id))
            {
                ReleaseAdmission(pKingdomId, eventYear);
                return false;
            }
            bool canonical = HistoricalSchoolDescentService.IsCanonicalMaster(
                pTeacher);
            SchoolMembershipSource source = canonical
                ? SchoolMembershipSource.DirectDiscipleship
                : SchoolMembershipSource.LaterDiscipleship;
            string sourceId = "elite:" + pKingdomId + ":" + pSourceYear +
                              ":" + pActor.data.id + ":" +
                              pTeacher.data.id;
            bool queued = SchoolMembershipService.TryQueueJoin(pActor,
                pTeacherMembership.SchoolId, source, sourceId,
                pTeacher.data.id, pEducationCity.data.id,
                Math.Max(1, pTeacherMembership.Generation + 1), 0f,
                "disciple_joined", pTeacher.data.id, pKingdomId,
                eventYear, sourceId, 2, success =>
                {
                    try
                    {
                        OnCompleted(pActor, pTeacher, pEducationCity,
                            pKingdomId, pActor.data.id, success);
                    }
                    finally
                    {
                        ReleaseTeacher(pTeacher.data.id);
                        if (!success)
                            ReleaseAdmission(pKingdomId, eventYear);
                        pCompletion?.Invoke(success);
                    }
                });
            if (queued) return true;
            ReleaseTeacher(pTeacher.data.id);
            ReleaseAdmission(pKingdomId, eventYear);
            return false;
        }

        private static void RecordQueued(
            HistoricalSchoolEliteCandidate pCandidate)
        {
            QueuedByRealm[pCandidate.KingdomId] =
                QueuedCount(pCandidate.KingdomId) + 1;
            if (pCandidate.Priority == HistoricalSchoolElitePriority.
                    AcademyCommoner)
                QueuedByAcademyCity[pCandidate.CityId] =
                    QueuedAcademyCount(pCandidate.CityId) + 1;
        }

        private static void OnCompleted(Actor pActor, Actor pTeacher,
            City pResidence, long pKingdomId, long pActorId, bool pSuccess)
        {
            if (!pSuccess) return;
            RemovePriority(pKingdomId, pActorId);
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            CitySchoolSnapshotService.MarkDirty(pResidence);
            if (pActor?.data == null || pTeacher?.data == null) return;
            HistoryWriter.RecordPerson(pActor.data.id, pActor.kingdom,
                pActor.getName(), "school_disciple",
                HistoryText.Actor(pActor) +
                HistoryLocalizationRules.H("aw_hist_school_studied_under") +
                HistoryText.Actor(pTeacher), ChronicleCategory.LIFE);
        }

        private static TeacherSelection SelectTeacher(Actor pStudent,
            City pResidence, long pKingdomId, int pYear,
            bool pLocalOnly)
        {
            int schoolCount = CourtSchoolRegistry.All.Count;
            if (schoolCount == 0) return default;
            int start = PositiveModulo(pYear +
                                       (int)(pStudent.data.id % schoolCount),
                schoolCount);
            var schoolPopulation = new int[schoolCount];
            for (int index = 0; index < schoolCount; index++)
                schoolPopulation[index] = HistoricalSchoolRuntimeIndex.Instance.MemberCount(
                    CourtSchoolRegistry.All[index].Id);
            IReadOnlyList<int> schoolOrder =
                HistoricalSchoolLectureRules.BuildPopulationPriorityOrder(
                    schoolPopulation, start);

            TeacherSelection local = SelectTeacherPass(pStudent, pResidence,
                pKingdomId, pYear, schoolOrder, pRequireCity: true,
                pRequireRealm: true);
            if (local.IsValid || pLocalOnly) return local;
            TeacherSelection realm = SelectTeacherPass(pStudent, pResidence,
                pKingdomId, pYear, schoolOrder, pRequireCity: false,
                pRequireRealm: true);
            return realm;
        }

        private static TeacherSelection SelectTeacherPass(Actor pStudent,
            City pResidence, long pKingdomId, int pYear,
            IReadOnlyList<int> pSchoolOrder,
            bool pRequireCity, bool pRequireRealm)
        {
            HistoricalSchoolRuntimeIndex index =
                HistoricalSchoolRuntimeIndex.Instance;
            int schoolCount = CourtSchoolRegistry.All.Count;
            for (int offset = 0; offset < pSchoolOrder.Count; offset++)
            {
                int schoolIndex = pSchoolOrder[offset];
                if (schoolIndex < 0 || schoolIndex >= schoolCount) continue;
                CourtSchoolDefinition school = CourtSchoolRegistry.All[
                    schoolIndex];
                long[] teacherIds = pRequireCity
                    ? index.ResidentTeacherIds(pResidence.data.id, school.Id)
                    : index.TeacherIds(school.Id);
                int budget = Math.Min(
                    HistoricalSchoolEliteEnrollmentRules.MaxTeacherIdsPerSchool,
                    teacherIds.Length);
                if (budget <= 0) continue;
                int teacherStart = PositiveModulo(pYear + offset +
                                                  (int)(pStudent.data.id %
                                                        teacherIds.Length),
                    teacherIds.Length);
                for (int scanned = 0; scanned < budget; scanned++)
                {
                    Actor teacher = FindActor(teacherIds[
                        (teacherStart + scanned) % teacherIds.Length]);
                    SchoolMembershipRecord membership = teacher?.data == null
                        ? null
                        : SchoolMembershipService.GetActive(teacher.data.id);
                    if (!IsUsableTeacher(teacher, membership, school.Id,
                            pResidence, pKingdomId, pRequireCity,
                            pRequireRealm)) continue;
                    return new TeacherSelection(teacher, membership);
                }
            }
            return default;
        }

        private static bool IsUsableTeacher(Actor pTeacher,
            SchoolMembershipRecord pMembership, string pSchoolId,
            City pStudentResidence, long pKingdomId, bool pRequireCity,
            bool pRequireRealm)
        {
            if (pTeacher?.data == null || pMembership == null ||
                pMembership.SchoolId != pSchoolId ||
                !SchoolLineageService.IsQualifiedTeacher(pTeacher) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pTeacher) ||
                !HistoricalAffiliationService.IsAvailableForOffice(pTeacher) ||
                HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount(
                    pTeacher.data.id) >= SchoolLineageService.DirectDiscipleCap)
                return false;
            City teacherResidence =
                HistoricalAffiliationService.ResidenceCity(pTeacher) ??
                pTeacher.city;
            if (teacherResidence?.data == null || teacherResidence.isRekt())
                return false;
            if (pRequireCity)
                return teacherResidence.data.id == pStudentResidence.data.id;
            return !pRequireRealm ||
                   teacherResidence.kingdom?.data?.id == pKingdomId;
        }

        private static bool IsValidCandidate(Actor pActor, Kingdom pKingdom,
            HistoricalSchoolEliteCandidate pCandidate)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !pActor.isAlive() || pActor.isRekt() || !pActor.isAdult())
                return false;
            switch (pCandidate.Priority)
            {
                case HistoricalSchoolElitePriority.Ruler:
                    return pKingdom.king == pActor;
                case HistoricalSchoolElitePriority.Heir:
                    return HeirService.PeekRegisteredHeir(pKingdom) == pActor;
                case HistoricalSchoolElitePriority.FeudatoryPrince:
                    return FeudatoryService.TryGetByPrince(pActor.data.id,
                               out FeudatorySnapshot feudatory) &&
                           feudatory.EmpireKingdomId == pKingdom.id;
                case HistoricalSchoolElitePriority.TitledNoble:
                    NobleTitleSnapshot title = NobleRankService.ReadHot(pActor);
                    return title.IsActive && title.KingdomId == pKingdom.id;
                case HistoricalSchoolElitePriority.UntitledNoble:
                    return HistoricalSchoolEliteEnrollmentRules.
                        IsNobleCandidateEligible(valid: true, adult: true,
                            noble: IsNobleIdentity(pActor),
                            domestic: CourtAffiliationResolver.IsDomestic(
                                pActor, pKingdom));
                case HistoricalSchoolElitePriority.DeclinedNoble:
                    pActor.data.get(LineageKeys.LINEAGE_STATUS,
                        out string currentStatus, LineageStatus.NONE);
                    pActor.data.get(LineageKeys.EVER_NOBLE_BLOOD,
                        out bool everNoble, false);
                    pActor.data.get(LineageKeys.LINEAGE_ID,
                        out long lineageId, -1L);
                    return HistoricalSchoolEliteEnrollmentRules.
                        IsDeclinedNobleCandidateEligible(valid: true,
                            adult: true,
                            currentNoble: currentStatus ==
                                LineageStatus.NOBLE,
                            everNoble, lineageId,
                            domestic: CourtAffiliationResolver.IsDomestic(
                                pActor, pKingdom));
                case HistoricalSchoolElitePriority.CentralOfficial:
                    pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                        out long courtKingdomId, -1L);
                    return courtKingdomId == pKingdom.id;
                case HistoricalSchoolElitePriority.LocalOfficial:
                    pActor.data.get(LineageKeys.COURT_KINGDOM_ID,
                        out long localCourtKingdomId, -1L);
                    return localCourtKingdomId == pKingdom.id &&
                           IsNobleIdentity(pActor);
                case HistoricalSchoolElitePriority.AcademyCommoner:
                    return IsValidAcademyCommoner(pActor, pKingdom,
                        FindCity(pCandidate.CityId), pCandidate.CityId);
                default:
                    return false;
            }
        }

        private static int AddAcademyCandidates(Kingdom pKingdom,
            IReadOnlyList<City> pCities,
            List<HistoricalSchoolEliteCandidate> pCandidates)
        {
            if (pKingdom?.data == null || pCities == null) return 0;
            int academyCount = 0;
            for (int cityIndex = 0; cityIndex < pCities.Count; cityIndex++)
            {
                City city = pCities[cityIndex];
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != pKingdom ||
                    HistoricalSchoolAcademyService.FindUsable(city) == null)
                    continue;
                academyCount++;

                int residentCount = city.units?.Count ?? 0;
                if (residentCount <= 0) continue;
                city.data.get(LineageKeys.SCHOOL_ACADEMY_EDUCATION_CURSOR,
                    out int cursor, 0);
                int start = PositiveModulo(cursor, residentCount);
                int scanCount = Math.Min(residentCount,
                    HistoricalSchoolEliteEnrollmentRules.
                        MaxAcademyResidentsPerYear);
                var candidates = new List<AcademyCandidate>(scanCount);
                for (int offset = 0; offset < scanCount; offset++)
                {
                    Actor actor = null;
                    try
                    {
                        actor = city.units[(start + offset) % residentCount];
                    }
                    catch { }
                    if (!IsValidAcademyCommoner(actor, pKingdom, city,
                            city.data.id)) continue;
                    candidates.Add(new AcademyCandidate(actor,
                        HistoricalSchoolEliteEnrollmentRules.
                            AcademyCandidateScore(
                                SafeStat(actor, "intelligence"),
                                SafeStat(actor, "stewardship"),
                                SafeStat(actor, "diplomacy"))));
                }
                city.data.set(LineageKeys.SCHOOL_ACADEMY_EDUCATION_CURSOR,
                    (start + scanCount) % residentCount);
                candidates.Sort((left, right) =>
                {
                    int score = right.Score.CompareTo(left.Score);
                    return score != 0
                        ? score
                        : left.Actor.data.id.CompareTo(right.Actor.data.id);
                });
                int admissionCount = Math.Min(candidates.Count,
                    HistoricalSchoolEliteEnrollmentRules.
                        MaxCommonerAdmissionsPerAcademyYear);
                for (int i = 0; i < admissionCount; i++)
                    pCandidates.Add(new HistoricalSchoolEliteCandidate(
                        pKingdom.id, candidates[i].Actor.data.id,
                        HistoricalSchoolElitePriority.AcademyCommoner,
                        city.data.id, SafeAge(candidates[i].Actor),
                        pExamPipelineEligible:
                        IsExamPipelineAdmissionEligible(candidates[i].Actor,
                            pKingdom,
                            HistoricalSchoolElitePriority.AcademyCommoner)));
            }
            return academyCount;
        }

        private static int CountQualifiedTeachers(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            var seen = new HashSet<long>();
            HistoricalSchoolRuntimeIndex index =
                HistoricalSchoolRuntimeIndex.Instance;
            for (int schoolIndex = 0;
                 schoolIndex < CourtSchoolRegistry.All.Count; schoolIndex++)
            {
                string schoolId = CourtSchoolRegistry.All[schoolIndex].Id;
                long[] teacherIds = index.TeacherIds(schoolId);
                int budget = Math.Min(teacherIds.Length,
                    HistoricalSchoolEliteEnrollmentRules.
                        MaxTeacherIdsPerSchool);
                for (int i = 0; i < budget; i++)
                {
                    long actorId = teacherIds[i];
                    if (seen.Contains(actorId)) continue;
                    Actor teacher = FindActor(actorId);
                    City residence =
                        HistoricalAffiliationService.ResidenceCity(teacher) ??
                        teacher?.city;
                    if (teacher?.data == null ||
                        residence?.kingdom != pKingdom ||
                        !SchoolLineageService.IsQualifiedTeacher(teacher) ||
                        !HistoricalAffiliationService.
                            IsAvailableForOffice(teacher))
                        continue;
                    seen.Add(actorId);
                }
            }
            return seen.Count;
        }

        private static bool IsValidAcademyCommoner(Actor pActor,
            Kingdom pKingdom, City pCity, long pCandidateCityId)
        {
            City residence =
                HistoricalAffiliationService.ResidenceCity(pActor) ??
                pActor?.city;
            bool local = pCity?.data != null && !pCity.isRekt() &&
                         pCity.data.id == pCandidateCityId &&
                         residence?.data?.id == pCandidateCityId;
            bool noble = IsNobleIdentity(pActor) ||
                         ChronicleGate.IsNobleActor(pActor);
            bool hasMembership = pActor?.data != null &&
                                 SchoolMembershipService.GetActive(
                                     pActor.data.id) != null;
            bool pending = pActor?.data != null &&
                           SchoolMembershipService.IsJoinPending(
                               pActor.data.id);
            bool valid = pActor?.data != null && pKingdom?.data != null &&
                         pActor.isAlive() && !pActor.isRekt() &&
                         CourtAffiliationResolver.IsDomestic(pActor,
                             pKingdom);
            return HistoricalSchoolEliteEnrollmentRules.
                IsAcademyCommonerEligible(valid, pActor?.isAdult() == true,
                    local, noble,
                    pActor?.hasTrait(LineageKeys.TRAIT_SLAVE) == true,
                    pActor?.hasTrait("madness") == true,
                    hasMembership, pending,
                    pActor != null && HistoricalAffiliationService.
                        IsPresentForInfluence(pActor) &&
                    HistoricalAffiliationService.IsAvailableForOffice(
                        pActor));
        }

        private static bool IsNobleIdentity(Actor pActor)
        {
            return NobleIdentityService.IsNobleActor(pActor);
        }

        private static bool IsExamPipelineAdmissionEligible(Actor pActor,
            Kingdom pKingdom, HistoricalSchoolElitePriority pPriority)
        {
            return HistoricalSchoolEliteEnrollmentRules.
                       IsExamPipelineEducationPriority(pPriority) &&
                   IsExamPipelineAdmissionEligible(pActor, pKingdom);
        }

        private static bool IsExamPipelineAdmissionEligible(Actor pActor,
            Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null ||
                !CivilServiceQualificationService.HasExaminationSystem(
                    pKingdom) || !pActor.isSexMale() ||
                !pActor.isAlive() || pActor.isRekt() || !pActor.isAdult() ||
                pActor.isKing() ||
                HeirService.PeekRegisteredHeir(pKingdom) == pActor ||
                FeudatoryService.IsActivePrince(pActor) ||
                pActor.hasTrait(LineageKeys.TRAIT_SLAVE) ||
                pActor.hasTrait("madness")) return false;
            return CourtAffiliationResolver.IsDomestic(pActor, pKingdom) &&
                   HistoricalAffiliationService.IsPresentForInfluence(pActor) &&
                   HistoricalAffiliationService.IsAvailableForOffice(pActor);
        }

        private static int RealmJoinLimit(long pKingdomId)
        {
            return RealmJoinLimits.TryGetValue(pKingdomId, out int limit)
                ? limit
                : HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear;
        }

        private static int RealmBaseJoinLimit(long pKingdomId)
        {
            return RealmBaseJoinLimits.TryGetValue(pKingdomId, out int limit)
                ? limit
                : HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear;
        }

        private static int QueuedAcademyCount(long pCityId)
        {
            return pCityId >= 0 && QueuedByAcademyCity.TryGetValue(pCityId,
                out int count) ? count : 0;
        }

        private static void EnsureAdmissionReservationYear(int pYear)
        {
            if (_admissionReservationYear == pYear) return;
            AdmissionReservationsByRealm.Clear();
            AdmissionLimitsByRealm.Clear();
            BaseAdmissionLimitsByRealm.Clear();
            _admissionReservationYear = pYear;
        }

        private static bool TryReserveAdmission(long pKingdomId,
            bool pExamPipelineEligible)
        {
            if (pKingdomId < 0L) return false;
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom?.data == null || kingdom.isRekt()) return false;
            ReadAdmissionState(kingdom, _admissionReservationYear,
                out int persistedCount, out int persistedBaseLimit,
                out int persistedLimit);
            int runtimeCount = AdmissionReservationsByRealm.TryGetValue(
                pKingdomId, out int current) ? current : 0;
            int count = Math.Max(runtimeCount, persistedCount);
            int limit = AdmissionLimitsByRealm.TryGetValue(pKingdomId,
                out int configured)
                ? configured
                : persistedLimit;
            int baseLimit = BaseAdmissionLimitsByRealm.TryGetValue(pKingdomId,
                out int configuredBase)
                ? configuredBase
                : persistedBaseLimit;
            if (!HistoricalSchoolEliteEnrollmentRules.CanUseAdmissionSlot(
                    count, baseLimit, limit, pExamPipelineEligible))
                return false;
            AdmissionReservationsByRealm[pKingdomId] = count + 1;
            PersistAdmissionState(kingdom, _admissionReservationYear,
                count + 1, baseLimit, limit);
            return true;
        }

        private static void ReleaseAdmission(long pKingdomId,
            int pReservationYear)
        {
            if (_admissionReservationYear != pReservationYear) return;
            Kingdom kingdom = FindKingdom(pKingdomId);
            if (kingdom?.data == null) return;
            ReadAdmissionState(kingdom, pReservationYear,
                out int persistedCount, out int baseLimit, out int limit);
            int runtimeCount = AdmissionReservationsByRealm.TryGetValue(
                pKingdomId, out int count) ? count : 0;
            int next = Math.Max(runtimeCount, persistedCount) - 1;
            next = Math.Max(0, next);
            if (next == 0) AdmissionReservationsByRealm.Remove(pKingdomId);
            else AdmissionReservationsByRealm[pKingdomId] = next;
            PersistAdmissionState(kingdom, pReservationYear, next,
                baseLimit, limit);
        }

        private static void SetAdmissionLimits(Kingdom pKingdom, int pYear,
            int pBaseLimit, int pLimit)
        {
            if (pKingdom?.data == null || pYear < 0) return;
            ReadAdmissionState(pKingdom, pYear, out int count, out _, out _);
            int baseLimit = Math.Max(
                HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear,
                Math.Min(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmHardCap, pBaseLimit));
            int limit = Math.Max(
                baseLimit,
                Math.Min(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmHardCap, pLimit));
            BaseAdmissionLimitsByRealm[pKingdom.id] = baseLimit;
            AdmissionLimitsByRealm[pKingdom.id] = limit;
            if (count > 0)
                AdmissionReservationsByRealm[pKingdom.id] = count;
            PersistAdmissionState(pKingdom, pYear, count, baseLimit, limit);
        }

        private static void ReadAdmissionState(Kingdom pKingdom,
            int pYear, out int pCount, out int pBaseLimit, out int pLimit)
        {
            pCount = 0;
            pBaseLimit = HistoricalSchoolEliteEnrollmentRules.
                MaxSuccessfulJoinsPerRealmPerYear;
            pLimit = HistoricalSchoolEliteEnrollmentRules.
                MaxSuccessfulJoinsPerRealmPerYear;
            if (pKingdom?.data == null || pYear < 0) return;
            pKingdom.data.get(LineageKeys.SCHOOL_EDUCATION_ADMISSION_YEAR,
                out int storedYear, -1);
            if (storedYear != pYear) return;
            pKingdom.data.get(LineageKeys.SCHOOL_EDUCATION_ADMISSION_COUNT,
                out int storedCount, 0);
            pKingdom.data.get(
                LineageKeys.SCHOOL_EDUCATION_ADMISSION_BASE_LIMIT,
                out int storedBaseLimit,
                HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear);
            pKingdom.data.get(LineageKeys.SCHOOL_EDUCATION_ADMISSION_LIMIT,
                out int storedLimit,
                HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear);
            pCount = Math.Max(0, storedCount);
            pBaseLimit = Math.Max(
                HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear,
                Math.Min(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmHardCap, storedBaseLimit));
            pLimit = Math.Max(
                pBaseLimit,
                Math.Min(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmHardCap, storedLimit));
        }

        private static void PersistAdmissionState(Kingdom pKingdom,
            int pYear, int pCount, int pBaseLimit, int pLimit)
        {
            if (pKingdom?.data == null || pYear < 0) return;
            pKingdom.data.set(LineageKeys.SCHOOL_EDUCATION_ADMISSION_YEAR,
                pYear);
            pKingdom.data.set(LineageKeys.SCHOOL_EDUCATION_ADMISSION_COUNT,
                Math.Max(0, pCount));
            int baseLimit = Math.Max(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmPerYear,
                Math.Min(HistoricalSchoolEliteEnrollmentRules.
                    MaxSuccessfulJoinsPerRealmHardCap, pBaseLimit));
            pKingdom.data.set(
                LineageKeys.SCHOOL_EDUCATION_ADMISSION_BASE_LIMIT,
                baseLimit);
            pKingdom.data.set(LineageKeys.SCHOOL_EDUCATION_ADMISSION_LIMIT,
                Math.Max(baseLimit,
                    Math.Min(HistoricalSchoolEliteEnrollmentRules.
                        MaxSuccessfulJoinsPerRealmHardCap, pLimit)));
        }

        private static bool TryReserveTeacher(long pTeacherId)
        {
            if (pTeacherId < 0L) return false;
            int pending = PendingAdmissionsByTeacher.TryGetValue(pTeacherId,
                out int current) ? current : 0;
            int committed = HistoricalSchoolRuntimeIndex.Instance.
                DirectDiscipleCount(pTeacherId);
            if (!HistoricalSchoolEliteEnrollmentRules.CanReserveTeacher(
                    committed, pending,
                    SchoolLineageService.DirectDiscipleCap)) return false;
            PendingAdmissionsByTeacher[pTeacherId] = pending + 1;
            return true;
        }

        private static void ReleaseTeacher(long pTeacherId)
        {
            if (!PendingAdmissionsByTeacher.TryGetValue(pTeacherId,
                    out int count)) return;
            if (count <= 1) PendingAdmissionsByTeacher.Remove(pTeacherId);
            else PendingAdmissionsByTeacher[pTeacherId] = count - 1;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return Math.Max(0f, pActor?.stats?[pKey] ?? 0f); }
            catch { return 0f; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return Math.Max(0, pActor?.getAge() ?? 0); }
            catch { return int.MaxValue; }
        }

        private static int QueuedCount(long pKingdomId)
        {
            return QueuedByRealm.TryGetValue(pKingdomId, out int count)
                ? count
                : 0;
        }

        private static void RemovePriority(long pKingdomId, long pActorId)
        {
            if (!PriorityCandidatesByRealm.TryGetValue(pKingdomId,
                    out Dictionary<long,
                        HistoricalSchoolEliteCandidate> candidates)) return;
            candidates.Remove(pActorId);
            if (candidates.Count == 0)
                PriorityCandidatesByRealm.Remove(pKingdomId);
        }

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
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
