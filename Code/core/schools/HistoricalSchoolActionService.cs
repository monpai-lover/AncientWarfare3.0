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
        private const int CandidateScanLimit = 48;
        private const int MaxTeachersPerYear = 192;
        private const int MaxExplicitActionsPerYear = 8;
        private const int MaxRediscoveriesPerYear = 4;
        private const int MaxInstitutionFoundingsPerYear = 4;
        private static int _lastProcessedYear = -1;

        public static void ClearRuntime()
        {
            _lastProcessedYear = -1;
        }

        public static void ProcessYear(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            if (pYear < 0 || pYear == _lastProcessedYear || pMembers == null) return;
            _lastProcessedYear = pYear;

            IReadOnlyDictionary<long, int> directCounts = pMembers.DirectDiscipleCounts;
            Actor[] teachers = pMembers.QualifiedTeachers(pYear, MaxTeachersPerYear).ToArray();
            Actor[] historical = teachers
                .Where(HistoricalSchoolDescentService.IsCanonicalMaster)
                .ToArray();
            foreach (Actor teacher in teachers)
                TeachInResidence(teacher, pYear, directCounts, pMembers);
            ProcessExplicitActions(pYear, pMembers);
            ProcessInstitutionFounding(pYear, historical);
        }

        private static void ProcessInstitutionFounding(int pYear,
            IEnumerable<Actor> pHistoricalTeachers)
        {
            int budget = MaxInstitutionFoundingsPerYear;
            if (pHistoricalTeachers == null) return;
            foreach (Actor teacher in pHistoricalTeachers.OrderBy(p => p.data.id))
            {
                if (budget <= 0) break;
                if (teacher?.data == null || !teacher.isAlive() || teacher.isRekt() ||
                    !HistoricalSchoolDescentService.IsCanonicalMaster(teacher)) continue;
                HistoricalSchoolMasterDefinition definition =
                    HistoricalSchoolDescentService.DefinitionFor(teacher);
                City city = HistoricalAffiliationService.ResidenceCity(teacher);
                if (definition == null || city?.data == null || city.isRekt() ||
                    !HistoricalAffiliationService.IsPresentForInfluence(teacher)) continue;

                // This is the same durable hook used by debates.  A lecture emitted by
                // TeachInResidence above is the minimum evidence for the first foundation.
                if (!HistoricalSchoolStore.TryFoundInstitution(definition, teacher.data.id,
                        city.data.id, pYear, World.world?.getCurWorldTime() ?? 0d)) continue;
                budget--;
            }
        }

        private static void ProcessExplicitActions(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            var snapshots = new Dictionary<long, CitySchoolSnapshot>();
            Dictionary<string, HashSet<long>> availableTeachers =
                pMembers.BuildAvailableTeacherIndex();
            int actions = 0;
            foreach (Actor actor in pMembers.LivingMembers())
            {
                if (actions >= MaxExplicitActionsPerYear) break;
                if (HistoricalSchoolDescentService.IsCanonicalMaster(actor)) continue;
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(actor.data.id);
                City city = HistoricalAffiliationService.ResidenceCity(actor) ?? actor.city;
                if (membership == null || city?.data == null || city.isRekt() ||
                    !HistoricalAffiliationService.IsPresentForInfluence(actor)) continue;
                if (!TrySelectRivalSchool(city, membership.SchoolId, snapshots,
                        out string targetSchool, out float rivalExposure)) continue;
                int yearsWithoutTeacher = YearsWithoutOwnTeacher(actor, membership, pYear,
                    availableTeachers);
                if (!HistoricalSchoolRules.CanExplicitlyConvert(false, yearsWithoutTeacher,
                        rivalExposure, true)) continue;
                string actionId = "ai_rival_conversion:" + pYear + ":" + actor.data.id +
                    ":" + targetSchool;
                if (TryExplicitConversion(actor, targetSchool, yearsWithoutTeacher,
                        rivalExposure, actionId, pYear, pMembers)) actions++;
            }
            ProcessRediscoveries(pYear, pMembers);
        }

        private static void ProcessRediscoveries(int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            int rediscoveries = 0;
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
            {
                if (rediscoveries >= MaxRediscoveriesPerYear ||
                    pMembers.LivingCount(school.Id) > 0) continue;
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
                    foreach (Actor actor in CandidateResidents(city, null))
                    {
                        string actionId = "ai_rediscovery:" + pYear + ":" + school.Id +
                            ":" + actor.data.id;
                        if (!TryRediscover(actor, school.Id, work, actionId, pYear,
                                pMembers)) continue;
                        rediscoveries++;
                        break;
                    }
                    if (rediscoveries >= MaxRediscoveriesPerYear ||
                        pMembers.LivingCount(school.Id) > 0) break;
                }
            }
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

        private static void TeachInResidence(Actor pTeacher, int pYear,
            IReadOnlyDictionary<long, int> pDirectCounts,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            SchoolMembershipRecord teacherMembership =
                SchoolMembershipService.GetActive(pTeacher.data.id);
            if (teacherMembership == null) return;
            City residence = HistoricalAffiliationService.ResidenceCity(pTeacher) ?? pTeacher.city;
            if (residence?.data == null || residence.isRekt()) return;
            if (!HistoricalAffiliationService.IsPresentForInfluence(pTeacher)) return;

            if (!RecordLecture(pTeacher, teacherMembership.SchoolId, residence, pYear))
                return;
            // Lectures make the teaching public; the separate persuasion event records
            // the master's attempt to influence the resident state's policy circle.
            RecordPersuasion(pTeacher, teacherMembership.SchoolId, residence, pYear);
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher))
                RecordHistoricalWork(pTeacher, pYear);
            int directCount = 0;
            if (pDirectCounts != null)
                pDirectCounts.TryGetValue(pTeacher.data.id, out directCount);
            int annualLimit = HistoricalSchoolRules.AnnualDirectDiscipleLimit(
                pTeacher.data.id, pYear);
            int recruited = 0;
            foreach (Actor candidate in CandidateResidents(residence, pTeacher))
            {
                if (recruited >= annualLimit) break;
                bool alreadyMember = SchoolMembershipService.GetActive(candidate.data.id) != null;
                if (!HistoricalSchoolRules.CanRecruitDisciple(pRealActor: true,
                        pAlive: candidate.isAlive() && !candidate.isRekt(),
                        pSameResidence: true, alreadyMember, directCount + recruited,
                        SchoolLineageService.DirectDiscipleCap)) continue;
                bool historicalTeacher = HistoricalSchoolDescentService.IsCanonicalMaster(pTeacher);
                SchoolMembershipSource source = historicalTeacher
                    ? SchoolMembershipSource.DirectDiscipleship
                    : SchoolMembershipSource.LaterDiscipleship;
                int generation = Math.Max(1, teacherMembership.Generation + 1);
                string sourceId = "teacher:" + pTeacher.data.id + ":year:" + pYear +
                    ":candidate:" + candidate.data.id;
                if (!SchoolMembershipService.TryJoin(candidate, teacherMembership.SchoolId,
                        source, sourceId, pTeacher.data.id, residence.data.id, generation,
                        pInitialReputation: Math.Max(10f, CandidateScore(candidate) * 0.1f)))
                    continue;
                if (!HistoricalSchoolStore.RecordSchoolEvent("disciple_joined",
                    candidate.data.id,
                    pTeacher.data.id, teacherMembership.SchoolId, residence.data.id,
                    residence.kingdom?.data?.id ?? -1L, pYear, sourceId, 2,
                    World.world?.getCurWorldTime() ?? 0d))
                {
                    if (!SchoolMembershipService.RollbackJoin(candidate, sourceId))
                        ModClass.LogWarning("Historical school disciple rollback failed");
                    CitySchoolSnapshotService.MarkDirty(residence);
                    continue;
                }
                SchoolMembershipRecord joined =
                    SchoolMembershipService.GetActive(candidate.data.id);
                if (joined == null ||
                    !pMembers.ApplyMembershipChange(null, joined, candidate))
                    ModClass.LogWarning("Annual school member snapshot missed disciple join");
                CitySchoolSnapshotService.MarkDirty(residence);
                recruited++;
                HistoryWriter.RecordPerson(candidate.data.id, candidate.kingdom,
                    candidate.getName(), "school_disciple", candidate.getName() +
                    " studied under " + pTeacher.getName(), ChronicleCategory.LIFE);
            }
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
            if (string.Equals(current.SchoolId, pTargetSchoolId, StringComparison.Ordinal))
                return false;
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            if (city?.data == null || city.isRekt()) return false;
            if (!SchoolMembershipService.TryConvert(pActor, pTargetSchoolId,
                    "conversion:" + pActionId + ":actor:" + pActor.data.id,
                    city.data.id)) return false;
            int eventYear = pYear >= 0 ? pYear : Date.getCurrentYear();
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
                HistoricalSchoolAnnualMemberSnapshotBuilder.Build());
        }

        private static bool TryRediscover(Actor pReader, string pSchoolId, string pWorkKey,
            string pActionId, int pYear,
            HistoricalSchoolAnnualMemberSnapshot<Actor> pMembers)
        {
            if (pReader?.data == null || !pReader.isAlive() || pReader.isRekt() ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pReader) ||
                string.IsNullOrWhiteSpace(pWorkKey) || string.IsNullOrWhiteSpace(pActionId) ||
                SchoolMembershipService.GetActive(pReader.data.id) != null ||
                !HistoricalAffiliationService.IsPresentForInfluence(pReader)) return false;
            int livingMembers = pMembers?.LivingCount(pSchoolId) ?? 0;
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
            if (joined == null || pMembers == null ||
                !pMembers.ApplyMembershipChange(null, joined, pReader))
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

        private static IEnumerable<Actor> CandidateResidents(City pCity, Actor pTeacher)
        {
            var result = new List<Actor>();
            int seen = 0;
            try
            {
                foreach (Actor actor in pCity.units)
                {
                    if (++seen > CandidateScanLimit * 4) break;
                    if (actor?.data == null || actor == pTeacher || !actor.isAlive() ||
                        actor.isRekt() || actor.isBaby()) continue;
                    result.Add(actor);
                    if (result.Count >= CandidateScanLimit) break;
                }
            }
            catch { }
            return result.OrderByDescending(CandidateScore).ThenBy(p => p.data.id);
        }

        private static bool RecordLecture(Actor pTeacher, string pSchoolId, City pCity,
            int pYear)
        {
            if (!HistoricalSchoolStore.RecordSchoolEvent("lecture", pTeacher.data.id, -1,
                pSchoolId, pCity.data.id, pCity.kingdom?.data?.id ?? -1L, pYear,
                pTeacher.data.name ?? "", 1, World.world?.getCurWorldTime() ?? 0d))
                return false;
            HistoricalSchoolContent.AnnounceLecture(pTeacher, pCity);
            return true;
        }

        private static bool RecordPersuasion(Actor pTeacher, string pSchoolId, City pCity,
            int pYear)
        {
            if (pTeacher?.data == null || pCity?.data == null) return false;
            long targetActorId = pCity.kingdom?.king?.data?.id ?? -1L;
            string targetName = pCity.kingdom?.king?.getName() ?? "";
            string payload = (pTeacher.getName() ?? "") + "|" + targetName;
            return HistoricalSchoolStore.RecordSchoolEvent("persuasion", pTeacher.data.id,
                targetActorId, pSchoolId, pCity.data.id,
                pCity.kingdom?.data?.id ?? -1L, pYear, payload, 1,
                World.world?.getCurWorldTime() ?? 0d);
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
