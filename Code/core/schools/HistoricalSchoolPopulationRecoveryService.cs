using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolPopulationRecoveryService
    {
        private sealed class SchoolWork
        {
            public string SchoolId;
            public int RegistryIndex;
            public int LivingMembers;
        }

        private static readonly List<SchoolWork> Work = new List<SchoolWork>(16);
        private static readonly HashSet<string> PendingSchools =
            new HashSet<string>(StringComparer.Ordinal);
        private static int _workYear = -1;
        private static int _completedYear = -1;
        private static int _workIndex;

        public static void ClearRuntime()
        {
            Work.Clear();
            PendingSchools.Clear();
            _workYear = -1;
            _completedYear = -1;
            _workIndex = 0;
        }

        public static bool ProcessYearFrame(int pYear)
        {
            if (pYear < 0 || pYear == _completedYear) return true;
            if (_workYear != pYear) BeginYear(pYear);
            if (_workIndex >= Work.Count)
            {
                FinishYear(pYear);
                return true;
            }

            SchoolWork work = Work[_workIndex++];
            TryRecoverSchool(work, pYear);
            if (_workIndex < Work.Count) return false;
            FinishYear(pYear);
            return true;
        }

        private static void BeginYear(int pYear)
        {
            Work.Clear();
            _workYear = pYear;
            _workIndex = 0;
            int schoolCount = CourtSchoolRegistry.All.Count;
            int rotation = schoolCount <= 0 ? 0 : pYear % schoolCount;
            for (int index = 0; index < schoolCount; index++)
            {
                string schoolId = CourtSchoolRegistry.All[index].Id;
                int livingMembers = HistoricalSchoolRuntimeIndex.Instance
                    .MemberCount(schoolId);
                if (livingMembers <= 0 || livingMembers >=
                    HistoricalSchoolRecoveryRules.MinimumLivingMembers) continue;
                Work.Add(new SchoolWork
                {
                    SchoolId = schoolId,
                    RegistryIndex = index,
                    LivingMembers = livingMembers
                });
            }
            Work.Sort((left, right) =>
            {
                int byPopulation = left.LivingMembers.CompareTo(right.LivingMembers);
                if (byPopulation != 0) return byPopulation;
                int leftOrder = PositiveModulo(left.RegistryIndex - rotation, schoolCount);
                int rightOrder = PositiveModulo(right.RegistryIndex - rotation, schoolCount);
                return leftOrder.CompareTo(rightOrder);
            });
            int budget = HistoricalSchoolRecoveryRules.SchoolWorkBudget(Work.Count);
            if (Work.Count > budget) Work.RemoveRange(budget, Work.Count - budget);
        }

        private static void FinishYear(int pYear)
        {
            Work.Clear();
            _workIndex = 0;
            _workYear = -1;
            _completedYear = pYear;
        }

        private static void TryRecoverSchool(SchoolWork pWork, int pYear)
        {
            if (pWork == null || string.IsNullOrEmpty(pWork.SchoolId) ||
                PendingSchools.Contains(pWork.SchoolId)) return;
            HistoricalSchoolRuntimeIndex index = HistoricalSchoolRuntimeIndex.Instance;
            int livingMembers = index.MemberCount(pWork.SchoolId);
            int teacherCount = index.TeacherCount(pWork.SchoolId);
            if (teacherCount <= 0)
            {
                TryPromoteContinuityTeacher(pWork, pYear, livingMembers);
                return;
            }
            if (!HistoricalSchoolRecoveryRules.NeedsRecruitment(livingMembers,
                    teacherCount, pPendingRecruitment: false)) return;

            Actor teacher = SelectTeacher(pWork, pYear);
            if (teacher?.data == null) return;
            SchoolMembershipRecord teacherMembership =
                SchoolMembershipService.GetActive(teacher.data.id);
            City residence = HistoricalAffiliationService.ResidenceCity(teacher) ??
                             teacher.city;
            if (teacherMembership == null || residence?.data == null ||
                residence.isRekt()) return;
            Actor candidate = SelectCandidate(pWork, pYear, teacher, residence);
            if (candidate?.data == null) return;

            bool historicalTeacher =
                HistoricalSchoolDescentService.IsCanonicalMaster(teacher);
            SchoolMembershipSource source = historicalTeacher
                ? SchoolMembershipSource.DirectDiscipleship
                : SchoolMembershipSource.LaterDiscipleship;
            string sourceId = "continuity:" + pWork.SchoolId + ":year:" + pYear +
                              ":teacher:" + teacher.data.id + ":candidate:" +
                              candidate.data.id;
            PendingSchools.Add(pWork.SchoolId);
            bool queued = SchoolMembershipService.TryQueueJoin(candidate,
                pWork.SchoolId, source, sourceId, teacher.data.id,
                residence.data.id, Math.Max(1, teacherMembership.Generation + 1),
                HistoricalSchoolStandingRules.TeacherReputation,
                "disciple_joined", teacher.data.id,
                residence.kingdom?.data?.id ?? -1L, pYear, sourceId, 2,
                success =>
                {
                    PendingSchools.Remove(pWork.SchoolId);
                    if (!success) return;
                    CitySchoolSnapshotService.MarkDirty(residence);
                    HistoryWriter.RecordPerson(candidate.data.id,
                        candidate.kingdom, candidate.getName(), "school_disciple",
                        HistoryText.Actor(candidate) +
                        HistoryLocalizationRules.H("aw_hist_school_studied_under") +
                        HistoryText.Actor(teacher), ChronicleCategory.LIFE);
                });
            if (!queued) PendingSchools.Remove(pWork.SchoolId);
        }

        private static Actor SelectTeacher(SchoolWork pWork, int pYear)
        {
            long[] teacherIds = HistoricalSchoolRuntimeIndex.Instance
                .TeacherIds(pWork.SchoolId);
            int budget = Math.Min(
                HistoricalSchoolRecoveryRules.MaxTeachersPerSchoolAttempt,
                teacherIds.Length);
            int start = HistoricalSchoolRecoveryRules.CandidateStart(
                pYear, pWork.RegistryIndex, teacherIds.Length);
            for (int offset = 0; offset < budget; offset++)
            {
                int candidateIndex = HistoricalSchoolRecoveryRules.CandidateIndex(
                    start, offset, teacherIds.Length);
                Actor actor = FindActor(teacherIds[candidateIndex]);
                SchoolMembershipRecord membership = actor?.data == null
                    ? null
                    : SchoolMembershipService.GetActive(actor.data.id);
                if (actor?.data == null || !actor.isAlive() || actor.isRekt() ||
                    membership?.SchoolId != pWork.SchoolId ||
                    !HistoricalAffiliationService.IsPresentForInfluence(actor) ||
                    !HistoricalAffiliationService.IsAvailableForOffice(actor) ||
                    HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount(
                        actor.data.id) >= SchoolLineageService.DirectDiscipleCap)
                    continue;
                City residence = HistoricalAffiliationService.ResidenceCity(actor) ??
                                 actor.city;
                if (residence?.data != null && !residence.isRekt()) return actor;
            }
            return null;
        }

        private static Actor SelectCandidate(SchoolWork pWork, int pYear,
            Actor pTeacher, City pResidence)
        {
            long[] candidateIds = HistoricalSchoolRecruitCandidateCache.Get(
                pResidence, pTeacher, pYear);
            int budget = HistoricalSchoolRecoveryRules.CandidateWorkBudget(
                candidateIds.Length);
            int start = HistoricalSchoolRecoveryRules.CandidateStart(
                pYear, pWork.RegistryIndex, candidateIds.Length);
            for (int offset = 0; offset < budget; offset++)
            {
                int candidateIndex = HistoricalSchoolRecoveryRules.CandidateIndex(
                    start, offset, candidateIds.Length);
                Actor candidate = FindActor(candidateIds[candidateIndex]);
                if (candidate?.data == null || !candidate.isAlive() ||
                    candidate.isRekt() || candidate.isBaby() ||
                    SchoolMembershipService.GetActive(candidate.data.id) != null)
                    continue;
                City residence = HistoricalAffiliationService.ResidenceCity(candidate) ??
                                 candidate.city;
                if (residence?.data?.id == pResidence.data.id) return candidate;
            }
            return null;
        }

        private static void TryPromoteContinuityTeacher(SchoolWork pWork,
            int pYear, int pLivingMembers)
        {
            long[] memberIds = HistoricalSchoolRuntimeIndex.Instance
                .MemberIds(pWork.SchoolId);
            int budget = HistoricalSchoolRecoveryRules.CandidateWorkBudget(
                memberIds.Length);
            int start = HistoricalSchoolRecoveryRules.CandidateStart(
                pYear, pWork.RegistryIndex, memberIds.Length);
            for (int offset = 0; offset < budget; offset++)
            {
                int candidateIndex = HistoricalSchoolRecoveryRules.CandidateIndex(
                    start, offset, memberIds.Length);
                long actorId = memberIds[candidateIndex];
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(actorId);
                Actor actor = FindActor(actorId);
                bool present = actor?.data != null && actor.isAlive() &&
                               !actor.isRekt() &&
                               HistoricalAffiliationService.IsPresentForInfluence(actor);
                if (membership == null || !HistoricalSchoolRecoveryRules
                        .ShouldPromoteContinuityTeacher(pLivingMembers, 0,
                            membership.Standing, present,
                            Math.Max(0, pYear - membership.StartYear),
                            membership.Reputation)) continue;
                SchoolMembershipService.TryPromoteContinuityTeacher(actorId, pYear);
                return;
            }
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
    }
}
