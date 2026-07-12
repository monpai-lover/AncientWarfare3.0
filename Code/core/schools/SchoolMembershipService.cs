using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class SchoolMembershipService
    {
        private static readonly SchoolMembershipBook Memberships = new SchoolMembershipBook();

        public static long Version => Memberships.Version;

        public static string GetSchool(long pActorId)
        {
            return Memberships.GetSchool(pActorId);
        }

        public static SchoolMembershipRecord GetActive(long pActorId)
        {
            return Memberships.GetActive(pActorId);
        }

        public static bool ApplyReputationDelta(long pActorId, float pDelta)
        {
            if (pActorId < 0 || float.IsNaN(pDelta) || float.IsInfinity(pDelta)) return false;
            return Memberships.UpdateReputation(pActorId, pDelta);
        }

        internal static SchoolMembershipRecord PrepareHistoricalDescent(Actor pActor,
            string pSchoolId, string pMasterId, long pCityId, int pGeneration,
            float pInitialReputation = 0f)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                CourtSchoolRegistry.Find(pSchoolId) == null ||
                string.IsNullOrWhiteSpace(pMasterId) || pCityId < 0 || pGeneration < 0 ||
                Memberships.GetActive(pActor.data.id) != null) return null;
            long membershipId = HistoricalSchoolStore.NextMembershipId();
            if (membershipId < 0) return null;
            var record = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                SchoolMembershipSource.HistoricalDescent, pMasterId, -1, pCityId,
                pGeneration, Math.Max(0f, pInitialReputation), Date.getCurrentYear());
            return record.IsValid ? record : null;
        }

        internal static bool AdoptCommittedHistoricalDescent(Actor pActor,
            SchoolMembershipRecord pRecord)
        {
            if (pActor?.data == null || pRecord == null || !pRecord.IsValid ||
                !pRecord.Active || pRecord.ActorId != pActor.data.id ||
                pRecord.Source != SchoolMembershipSource.HistoricalDescent) return false;
            if (!Memberships.TryJoin(pRecord))
            {
                LoadIndexes();
                if (!SameMembershipRecord(Memberships.GetActive(pRecord.ActorId), pRecord))
                {
                    ModClass.LogWarning("Committed historical descent membership conflict: actor=" +
                                        pRecord.ActorId + " membership=" +
                                        pRecord.MembershipId);
                    return false;
                }
            }
            try
            {
                Project(pActor, pRecord.SchoolId);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed historical descent projection failed: " +
                                    error.Message);
                return false;
            }
            return true;
        }

        public static bool TryJoin(Actor pActor, string pSchoolId,
            SchoolMembershipSource pSource, string pSourceId, long pTeacherActorId,
            long pCityId, int pGeneration, float pInitialReputation = 0f)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                CourtSchoolRegistry.Find(pSchoolId) == null || string.IsNullOrWhiteSpace(pSourceId))
                return false;
            SchoolMembershipRecord existing = Memberships.GetActive(pActor.data.id);
            if (existing != null)
                return existing.SchoolId == pSchoolId && existing.Source == pSource &&
                       existing.SourceId == pSourceId;

            long membershipId = HistoricalSchoolStore.NextMembershipId();
            if (membershipId < 0) return false;
            int year = Date.getCurrentYear();
            var record = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                pSource, pSourceId, pTeacherActorId, pCityId, pGeneration,
                Math.Max(0f, pInitialReputation), year);
            if (!record.IsValid || !HistoricalSchoolStore.InsertMembership(record, WorldTime()))
                return false;
            if (!Memberships.TryJoin(record))
            {
                LoadIndexes();
                return false;
            }
            bool needsTravelAffiliation = pSource != SchoolMembershipSource.HistoricalDescent &&
                (HistoricalSchoolDescentService.IsCanonicalMaster(pActor) ||
                 SchoolLineageService.IsQualifiedTeacher(pActor));
            if (needsTravelAffiliation &&
                !HistoricalAffiliationService.EnsureMemberAffiliation(pActor, pCityId))
            {
                if (!RollbackJoin(pActor, pSourceId))
                    ModClass.LogWarning("School membership affiliation rollback failed");
                return false;
            }
            Project(pActor, pSchoolId);
            return true;
        }

        public static bool TryConvert(Actor pActor, string pSchoolId, string pSourceId,
            long pCityId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                CourtSchoolRegistry.Find(pSchoolId) == null || string.IsNullOrWhiteSpace(pSourceId))
                return false;
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null || current.Source == SchoolMembershipSource.HistoricalDescent ||
                current.SchoolId == pSchoolId) return false;
            if ((HistoricalSchoolDescentService.IsCanonicalMaster(pActor) ||
                 SchoolLineageService.IsQualifiedTeacher(pActor)) &&
                !HistoricalAffiliationService.EnsureMemberAffiliation(pActor, pCityId))
                return false;
            long membershipId = HistoricalSchoolStore.NextMembershipId();
            if (membershipId < 0) return false;
            int year = Date.getCurrentYear();
            var replacement = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                SchoolMembershipSource.ExplicitConversion, pSourceId, -1, pCityId, 0,
                current.Reputation, year);
            if (!HistoricalSchoolStore.ConvertMembership(current, replacement, year, WorldTime()))
                return false;
            if (!Memberships.TryConvert(pActor.data.id, replacement, year, out _))
            {
                LoadIndexes();
                return false;
            }
            Project(pActor, pSchoolId);
            return true;
        }

        internal static bool RollbackConversion(Actor pActor,
            SchoolMembershipRecord pOriginal)
        {
            if (pActor?.data == null || pOriginal == null ||
                pOriginal.ActorId != pActor.data.id) return false;
            SchoolMembershipRecord replacement = Memberships.GetActive(pActor.data.id);
            if (replacement == null || replacement.Source !=
                SchoolMembershipSource.ExplicitConversion) return false;
            if (!HistoricalSchoolStore.RollbackConversion(pOriginal, replacement,
                    WorldTime()))
            {
                LoadIndexes();
                return false;
            }
            if (!Memberships.RollbackConvert(pActor.data.id, pOriginal, replacement))
            {
                LoadIndexes();
                return false;
            }
            Project(pActor, pOriginal.SchoolId);
            return true;
        }

        public static void OnDeath(Actor pActor)
        {
            if (pActor?.data == null) return;
            SchoolLineageService.ReleaseItinerant(pActor);
            HistoricalAffiliationService.MarkDead(pActor);
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null)
            {
                Project(pActor, CourtSchoolId.None);
                return;
            }
            int year = Date.getCurrentYear();
            if (!HistoricalSchoolStore.CloseMembership(current, year, "death", WorldTime()))
                return;
            Memberships.Close(pActor.data.id, year, "death", out _);
            Project(pActor, CourtSchoolId.None);
        }

        internal static bool RollbackJoin(Actor pActor, string pSourceId)
        {
            if (pActor?.data == null) return false;
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null || current.SourceId != (pSourceId ?? "")) return false;
            if (!HistoricalSchoolStore.DeleteMembership(current))
            {
                LoadIndexes();
                return false;
            }
            if (!Memberships.RollbackJoin(pActor.data.id))
            {
                LoadIndexes();
                return false;
            }
            Project(pActor, CourtSchoolId.None);
            return true;
        }

        public static Actor[] LivingMembers(string pSchoolId)
        {
            IReadOnlyList<long> members = Memberships.Members(pSchoolId);
            var result = new List<Actor>(members.Count);
            foreach (long actorId in members)
            {
                Actor actor = World.world?.units?.get(actorId);
                if (actor?.data != null && actor.isAlive() && !actor.isRekt()) result.Add(actor);
            }
            return result.ToArray();
        }

        public static int Count(string pSchoolId)
        {
            return Memberships.Members(pSchoolId).Count;
        }

        public static long[] Members(string pSchoolId)
        {
            IReadOnlyList<long> members = Memberships.Members(pSchoolId);
            var result = new long[members.Count];
            for (int i = 0; i < members.Count; i++) result[i] = members[i];
            return result;
        }

        public static void LoadIndexes()
        {
            Memberships.Clear();
            var duplicates = new List<SchoolMembershipRecord>();
            foreach (SchoolMembershipRecord record in HistoricalSchoolStore.LoadActiveMemberships())
            {
                if (!Memberships.TryJoin(record)) duplicates.Add(record);
            }
            foreach (SchoolMembershipRecord duplicate in duplicates)
                HistoricalSchoolStore.CloseMembership(duplicate, Date.getCurrentYear(),
                    "duplicate_active_repair", WorldTime());

            try
            {
                if (World.world?.units != null)
                    foreach (Actor actor in World.world.units)
                        if (actor?.data != null) Project(actor, GetSchool(actor.data.id),
                            pPreserveHistoricalMasterId: true);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("SchoolMembershipService projection repair failed: " +
                                    error.Message);
            }
            CitySchoolSnapshotService.Clear();
        }

        public static void ClearRuntime()
        {
            Memberships.Clear();
            CitySchoolSnapshotService.Clear();
        }

        private static void Project(Actor pActor, string pSchoolId,
            bool pPreserveHistoricalMasterId = false)
        {
            if (pActor?.data == null) return;
            string school = CourtSchoolRegistry.Find(pSchoolId) == null
                ? CourtSchoolId.None
                : pSchoolId;
            SchoolMembershipRecord record = Memberships.GetActive(pActor.data.id);
            pActor.data.set(LineageKeys.COURT_SCHOOL, school);
            pActor.data.set(LineageKeys.SCHOOL_MEMBERSHIP_ID, record?.MembershipId ?? -1L);
            pActor.data.set(LineageKeys.SCHOOL_MEMBERSHIP_SOURCE,
                record?.Source.ToString() ?? "");
            string historicalMasterId = record?.Source ==
                SchoolMembershipSource.HistoricalDescent ? record.SourceId : "";
            if (string.IsNullOrEmpty(historicalMasterId) && pPreserveHistoricalMasterId)
            {
                pActor.data.get(LineageKeys.SCHOOL_MASTER_ID, out string preservedMasterId, "");
                if (HistoricalSchoolMasterRegistry.Find(preservedMasterId) != null)
                    historicalMasterId = preservedMasterId;
            }
            pActor.data.set(LineageKeys.SCHOOL_MASTER_ID, historicalMasterId);
            foreach (CourtSchoolDefinition definition in CourtSchoolRegistry.All)
            {
                string traitId = definition.TraitId;
                if (string.IsNullOrEmpty(traitId)) continue;
                if (definition.Id == school)
                {
                    if (!pActor.hasTrait(traitId)) pActor.addTrait(traitId);
                }
                else if (pActor.hasTrait(traitId))
                    pActor.removeTrait(traitId);
            }
            CitySchoolSnapshotService.MarkActorDirty(pActor);
            CitySchoolSnapshotService.MarkDirty(HistoricalAffiliationService.ResidenceCity(pActor));
        }

        private static bool SameMembershipRecord(SchoolMembershipRecord pFirst,
            SchoolMembershipRecord pSecond)
        {
            return pFirst != null && pSecond != null &&
                   pFirst.MembershipId == pSecond.MembershipId &&
                   pFirst.ActorId == pSecond.ActorId &&
                   pFirst.SchoolId == pSecond.SchoolId && pFirst.Source == pSecond.Source &&
                   pFirst.SourceId == pSecond.SourceId &&
                   pFirst.TeacherActorId == pSecond.TeacherActorId &&
                   pFirst.CityId == pSecond.CityId &&
                   pFirst.Generation == pSecond.Generation &&
                   pFirst.Reputation.Equals(pSecond.Reputation) &&
                   pFirst.StartYear == pSecond.StartYear &&
                   pFirst.EndYear == pSecond.EndYear && pFirst.Active == pSecond.Active &&
                   pFirst.EndReason == pSecond.EndReason;
        }

        private static double WorldTime()
        {
            return World.world?.getCurWorldTime() ?? 0d;
        }
    }
}
