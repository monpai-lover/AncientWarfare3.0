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

        public static void ProcessYear(int pYear)
        {
            var teachers = new Dictionary<long, Actor>();
            Dictionary<long, int> directCounts = SchoolLineageService.BuildDirectDiscipleCounts();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (Actor actor in SchoolMembershipService.LivingMembers(school.Id))
                    if (SchoolLineageService.IsQualifiedTeacher(actor))
                        teachers[actor.data.id] = actor;
            Actor[] historical = teachers.Values
                .Where(HistoricalSchoolDescentService.IsCanonicalMaster)
                .OrderBy(p => p.data.id)
                .Take(MaxTeachersPerYear)
                .ToArray();
            int remaining = Math.Max(0, MaxTeachersPerYear - historical.Length);
            IEnumerable<Actor> later = teachers.Values
                .Where(p => !HistoricalSchoolDescentService.IsCanonicalMaster(p))
                .OrderBy(p => TeacherOrder(p.data.id, pYear))
                .Take(remaining);
            foreach (Actor teacher in historical.Concat(later))
                TeachInResidence(teacher, pYear, directCounts);
            ProcessExplicitActions(pYear);
        }

        private static void ProcessExplicitActions(int pYear)
        {
            var members = new Dictionary<long, Actor>();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (Actor actor in SchoolMembershipService.LivingMembers(school.Id))
                    if (actor?.data != null) members[actor.data.id] = actor;

            var snapshots = new Dictionary<long, CitySchoolSnapshot>();
            Dictionary<string, HashSet<long>> availableTeachers =
                BuildAvailableTeacherIndex();
            int actions = 0;
            foreach (Actor actor in members.Values.OrderBy(p => p.data.id))
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
                        rivalExposure, actionId)) actions++;
            }
            ProcessRediscoveries(pYear);
        }

        private static void ProcessRediscoveries(int pYear)
        {
            int rediscoveries = 0;
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
            {
                if (rediscoveries >= MaxRediscoveriesPerYear ||
                    SchoolMembershipService.LivingMembers(school.Id).Length > 0) continue;
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
                        if (!TryRediscover(actor, school.Id, work, actionId)) continue;
                        rediscoveries++;
                        break;
                    }
                    if (rediscoveries >= MaxRediscoveriesPerYear ||
                        SchoolMembershipService.LivingMembers(school.Id).Length > 0) break;
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
                snapshot = CitySchoolSnapshotService.GetSnapshot(pCity, pEnsureFresh: true);
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

        private static Dictionary<string, HashSet<long>> BuildAvailableTeacherIndex()
        {
            var result = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (Actor teacher in SchoolMembershipService.LivingMembers(school.Id))
                {
                    if (!SchoolLineageService.IsQualifiedTeacher(teacher) ||
                        !HistoricalAffiliationService.IsPresentForInfluence(teacher)) continue;
                    City city = HistoricalAffiliationService.ResidenceCity(teacher) ?? teacher.city;
                    long cityId = city?.data?.id ?? -1L;
                    if (cityId < 0) continue;
                    string key = TeacherResidenceKey(school.Id, cityId);
                    if (!result.TryGetValue(key, out HashSet<long> ids))
                    {
                        ids = new HashSet<long>();
                        result[key] = ids;
                    }
                    ids.Add(teacher.data.id);
                }
            return result;
        }

        private static string TeacherResidenceKey(string pSchoolId, long pCityId)
        {
            return (pSchoolId ?? "") + ":" + pCityId;
        }

        private static void TeachInResidence(Actor pTeacher, int pYear,
            IReadOnlyDictionary<long, int> pDirectCounts)
        {
            SchoolMembershipRecord teacherMembership =
                SchoolMembershipService.GetActive(pTeacher.data.id);
            if (teacherMembership == null) return;
            City residence = HistoricalAffiliationService.ResidenceCity(pTeacher) ?? pTeacher.city;
            if (residence?.data == null || residence.isRekt()) return;
            if (!HistoricalAffiliationService.IsPresentForInfluence(pTeacher)) return;

            RecordLecture(pTeacher, teacherMembership.SchoolId, residence, pYear);
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
                CitySchoolSnapshotService.MarkDirty(residence);
                recruited++;
                HistoricalSchoolStore.RecordSchoolEvent("disciple_joined", candidate.data.id,
                    pTeacher.data.id, teacherMembership.SchoolId, residence.data.id,
                    residence.kingdom?.data?.id ?? -1L, pYear, sourceId, 2,
                    World.world?.getCurWorldTime() ?? 0d);
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
            int pYearsWithoutOwnTeacher, float pRivalExposure, string pActionId)
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
            CitySchoolSnapshotService.MarkDirty(city);

            HistoricalSchoolStore.RecordSchoolEvent("school_conversion", pActor.data.id, -1,
                pTargetSchoolId, city.data.id, city.kingdom?.data?.id ?? -1L,
                Date.getCurrentYear(), pActionId, 2, World.world?.getCurWorldTime() ?? 0d);
            HistoryWriter.RecordPerson(pActor.data.id,
                HistoricalAffiliationService.HomeKingdom(pActor) ?? pActor.kingdom,
                pActor.getName(), "school_conversion",
                "Converted from " + current.SchoolId + " to " + pTargetSchoolId,
                ChronicleCategory.SOCIAL);
            return true;
        }

        public static bool TryRediscover(Actor pReader, string pSchoolId, string pWorkKey,
            string pActionId)
        {
            if (pReader?.data == null || !pReader.isAlive() || pReader.isRekt() ||
                HistoricalSchoolDescentService.IsCanonicalMaster(pReader) ||
                string.IsNullOrWhiteSpace(pWorkKey) || string.IsNullOrWhiteSpace(pActionId) ||
                SchoolMembershipService.GetActive(pReader.data.id) != null ||
                !HistoricalAffiliationService.IsPresentForInfluence(pReader)) return false;
            int livingMembers = SchoolMembershipService.LivingMembers(pSchoolId).Length;
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
            CitySchoolSnapshotService.MarkDirty(city);

            HistoricalSchoolStore.RecordSchoolEvent("school_rediscovery", pReader.data.id, -1,
                pSchoolId, city.data.id, city.kingdom?.data?.id ?? -1L,
                Date.getCurrentYear(), pWorkKey, 3, World.world?.getCurWorldTime() ?? 0d);
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

        private static void RecordLecture(Actor pTeacher, string pSchoolId, City pCity,
            int pYear)
        {
            HistoricalSchoolStore.RecordSchoolEvent("lecture", pTeacher.data.id, -1,
                pSchoolId, pCity.data.id, pCity.kingdom?.data?.id ?? -1L, pYear,
                pTeacher.data.name ?? "", 1, World.world?.getCurWorldTime() ?? 0d);
            HistoricalSchoolContent.AnnounceLecture(pTeacher, pCity);
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

        private static long TeacherOrder(long pActorId, int pYear)
        {
            unchecked
            {
                long value = pActorId * 6364136223846793005L + pYear * 1442695040888963407L;
                return value ^ value >> 33;
            }
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
