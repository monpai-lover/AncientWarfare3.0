using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class SchoolMembershipService
    {
        private sealed class PendingSchoolDeath
        {
            public PendingSchoolDeath(Actor pActor, SchoolMembershipRecord pMembership,
                HistoricalSchoolAffiliationSnapshot pCachedAffiliation,
                HistoricalSchoolMasterDefinition pMaster, bool pWasQualifiedTeacher,
                City pRuntimeCity, int pDeathYear, long pDeathCityId, string pDeathCause,
                double pPersistenceTime)
            {
                Actor = pActor;
                Membership = pMembership;
                CachedAffiliation = pCachedAffiliation;
                Master = pMaster;
                WasQualifiedTeacher = pWasQualifiedTeacher;
                RuntimeCity = pRuntimeCity;
                DeathYear = pDeathYear;
                DeathCityId = pDeathCityId;
                DeathCause = pDeathCause ?? "death";
                PersistenceTime = pPersistenceTime;
            }

            public Actor Actor { get; }
            public SchoolMembershipRecord Membership { get; }
            public HistoricalSchoolAffiliationSnapshot CachedAffiliation { get; set; }
            public HistoricalSchoolMasterDefinition Master { get; }
            public bool WasQualifiedTeacher { get; }
            public City RuntimeCity { get; }
            public int DeathYear { get; }
            public long DeathCityId { get; set; }
            public string DeathCause { get; }
            public double PersistenceTime { get; }
            public bool Uncertain { get; set; }
            public bool DestroyRequested { get; set; }
            public int Attempts { get; set; }
            public long ReadyFrame { get; set; }
        }

        private static readonly SchoolMembershipBook Memberships = new SchoolMembershipBook();
        private static readonly Queue<long> PendingDeathRetries = new Queue<long>();
        private static readonly HashSet<long> QueuedDeathRetries = new HashSet<long>();
        private static readonly Dictionary<long, PendingSchoolDeath> PendingDeathsByActor =
            new Dictionary<long, PendingSchoolDeath>();
        private const int MaxDeathRetryBackoffFrames = 240;
        private const int MaxDeathRetryQueueScan = 16;
        private static long _deathRetryFrame;

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
            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
                return null;
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
            SchoolMembershipRecord existing = Memberships.GetActive(pRecord.ActorId);
            if (existing != null)
            {
                if (!SameMembershipRecord(existing, pRecord))
                {
                    ModClass.LogWarning("Committed historical descent membership conflict: actor=" +
                                        pRecord.ActorId + " membership=" +
                                        pRecord.MembershipId);
                    return false;
                }
            }
            else if (!Memberships.TryJoin(pRecord))
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

            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
                return false;
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
            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
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

        public static SchoolDeathOutcome OnDeath(Actor pActor, bool pDestroy = false)
        {
            if (pActor?.data == null) return SchoolDeathOutcome.NotApplicable;
            if (PendingDeathsByActor.TryGetValue(pActor.data.id,
                    out PendingSchoolDeath existingPending))
            {
                QueueDeathRetry(existingPending, pDestroy);
                return SchoolDeathOutcome.Failed;
            }
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null) return SchoolDeathOutcome.NotApplicable;
            if (!current.Active || !current.IsValid || current.ActorId != pActor.data.id)
                return SchoolDeathOutcome.Failed;

            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id);
            bool historicalMaster = current.Source ==
                                    SchoolMembershipSource.HistoricalDescent;
            HistoricalSchoolMasterDefinition master = historicalMaster
                ? HistoricalSchoolMasterRegistry.Find(current.SourceId)
                : null;
            bool wasQualifiedTeacher = historicalMaster ||
                SchoolLineageService.WasQualifiedTeacherAtDeath(current);
            City city = HistoricalAffiliationService.ResidenceCity(pActor) ?? pActor.city;
            pActor.data.get(LineageKeys.DEATH_CAUSE, out string cause, "death");
            int year = Date.getCurrentYear();
            var pending = new PendingSchoolDeath(pActor, current, affiliation, master,
                wasQualifiedTeacher, city, year, city?.data?.id ?? -1L, cause,
                WorldTime());
            SchoolDeathOutcome outcome = PersistPendingDeath(pending,
                pReconcileUnknown: false);
            if (outcome != SchoolDeathOutcome.Committed)
                QueueDeathRetry(pending, pDestroy);
            return outcome;
        }

        private static SchoolDeathOutcome PersistPendingDeath(PendingSchoolDeath pending,
            bool pReconcileUnknown)
        {
            HistoricalSchoolAffiliationSnapshot authoritativeAffiliation;
            SchoolPersistenceOutcome persistenceOutcome;
            long effectiveDeathCityId = pending.DeathCityId;
            if (pReconcileUnknown)
                persistenceOutcome = HistoricalSchoolStore.ReconcileSchoolDeath(
                    pending.Membership, pending.CachedAffiliation, pending.Master,
                    pending.DeathYear, pending.DeathCityId, pending.DeathCause,
                    pending.PersistenceTime, out authoritativeAffiliation);
            else
                persistenceOutcome = HistoricalSchoolStore.CommitSchoolDeath(
                    pending.Membership, pending.CachedAffiliation, pending.Master,
                    pending.DeathYear, pending.DeathCityId, pending.DeathCause,
                    pending.PersistenceTime, out authoritativeAffiliation,
                    out effectiveDeathCityId);
            pending.DeathCityId = effectiveDeathCityId;
            if (authoritativeAffiliation != null)
                pending.CachedAffiliation = authoritativeAffiliation;
            if (pReconcileUnknown &&
                persistenceOutcome == SchoolPersistenceOutcome.CleanFailure)
            {
                persistenceOutcome = HistoricalSchoolStore.CommitSchoolDeath(
                    pending.Membership, pending.CachedAffiliation, pending.Master,
                    pending.DeathYear, pending.DeathCityId, pending.DeathCause,
                    pending.PersistenceTime, out authoritativeAffiliation,
                    out effectiveDeathCityId);
                pending.DeathCityId = effectiveDeathCityId;
                if (authoritativeAffiliation != null)
                    pending.CachedAffiliation = authoritativeAffiliation;
            }
            if (persistenceOutcome != SchoolPersistenceOutcome.Committed)
            {
                pending.Uncertain = persistenceOutcome == SchoolPersistenceOutcome.Unknown;
                return SchoolDeathOutcome.Failed;
            }
            return ApplyCommittedDeath(pending, authoritativeAffiliation);
        }

        private static SchoolDeathOutcome ApplyCommittedDeath(PendingSchoolDeath pending,
            HistoricalSchoolAffiliationSnapshot committedAffiliation)
        {
            Actor pActor = pending.Actor;
            SchoolMembershipRecord current = pending.Membership;
            if (committedAffiliation != null)
            {
                try
                {
                    if (!HistoricalAffiliationService.AdoptCommittedDeath(committedAffiliation))
                    {
                        ModClass.LogWarning(
                            "Committed school death affiliation adopt rejected: actor=" +
                            pActor.data.id);
                        ReloadAffiliationAfterCommittedDeath();
                    }
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed school death affiliation adopt failed: " +
                                        error.Message);
                    ReloadAffiliationAfterCommittedDeath();
                }
            }
            City committedCity = HistoricalAffiliationService.ResidenceCity(pActor) ??
                                 pending.RuntimeCity;
            try
            {
                if (!Memberships.CloseExpected(pActor.data.id, current.MembershipId,
                        pending.DeathYear,
                        "death", out _))
                {
                    ModClass.LogWarning(
                        "Committed school death membership adopt failed: actor=" +
                        pActor.data.id + " membership=" + current.MembershipId);
                    ReloadMembershipAfterCommittedDeath();
                }
                else
                {
                    try { Project(pActor, CourtSchoolId.None); }
                    catch (Exception error)
                    {
                        ModClass.LogWarning("Committed school death projection failed: " +
                                            error.Message);
                    }
                }
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death membership adopt threw: " +
                                    error.Message);
                ReloadMembershipAfterCommittedDeath();
            }
            try { HistoricalSchoolTravelService.OnCommittedDeath(pActor); }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death travel cleanup failed: " +
                                    error.Message);
            }
            try { HistoricalSchoolActivityQueue.CancelActor(pActor, pRestoreActor: false); }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death activity cleanup failed: " +
                                    error.Message);
            }
            try { CourtService.ClearGuestOfficerAfterDeath(pActor, committedAffiliation); }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death guest cleanup failed: " +
                                    error.Message);
            }
            if (pending.WasQualifiedTeacher)
            {
                try { SchoolLineageService.OnTeacherDeath(pActor); }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed school teacher death history failed: " +
                                        error.Message);
                }
            }
            if (pending.Master != null)
            {
                try
                {
                    HistoricalSchoolDescentService.OnCommittedDeath(pActor, pending.Master,
                        committedCity);
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed historical school master death history failed: " +
                                        error.Message);
                }
            }
            return SchoolDeathOutcome.Committed;
        }

        private static void ReloadAffiliationAfterCommittedDeath()
        {
            try
            {
                HistoricalAffiliationService.LoadState();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death affiliation reload failed: " +
                                    error.Message);
            }
        }

        private static void ReloadMembershipAfterCommittedDeath()
        {
            try
            {
                LoadIndexes();
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death membership reload failed: " +
                                    error.Message);
            }
        }

        internal static void ProcessDeathRetries()
        {
            ProcessDeathRetries(pIgnoreBackoff: false);
        }

        private static void ProcessDeathRetries(bool pIgnoreBackoff)
        {
            _deathRetryFrame++;
            long actorId = -1L;
            int scanBudget = Math.Min(PendingDeathRetries.Count, MaxDeathRetryQueueScan);
            while (scanBudget-- > 0)
            {
                long candidate = PendingDeathRetries.Dequeue();
                if (!QueuedDeathRetries.Contains(candidate)) continue;
                if (!PendingDeathsByActor.TryGetValue(candidate,
                        out PendingSchoolDeath candidatePending)) continue;
                if (!pIgnoreBackoff && candidatePending.ReadyFrame > _deathRetryFrame)
                {
                    PendingDeathRetries.Enqueue(candidate);
                    continue;
                }
                QueuedDeathRetries.Remove(candidate);
                actorId = candidate;
                break;
            }
            if (actorId < 0) return;

            if (!PendingDeathsByActor.TryGetValue(actorId,
                    out PendingSchoolDeath pending) || pending.Actor?.data == null ||
                pending.Actor.data.id != actorId)
            {
                ClearDeathRetry(actorId, pCancelQueued: true);
                ReloadMembershipAfterCommittedDeath();
                return;
            }

            pending.Attempts++;
            SchoolDeathOutcome outcome;
            try
            {
                outcome = PersistPendingDeath(pending,
                    pReconcileUnknown: pending.Uncertain);
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Deferred school death retry failed: " + error.Message);
                outcome = SchoolDeathOutcome.Failed;
            }
            if (outcome != SchoolDeathOutcome.Committed)
                QueueDeathRetry(pending, pending.DestroyRequested);

            bool queuedAgain = QueuedDeathRetries.Contains(actorId);
            if (outcome == SchoolDeathOutcome.Committed || !queuedAgain)
            {
                ClearDeathRetry(actorId, pCancelQueued: true);
                if (pending.DestroyRequested)
                    HistoricalSchoolActorDestroyQueue.Queue(pending.Actor,
                        "Committed school death destroy requeue failed");
                return;
            }
            pending.ReadyFrame = _deathRetryFrame +
                DeathRetryBackoffFrames(pending.Attempts);
        }

        internal static bool FlushDeathRetriesForSave()
        {
            int budget = Math.Min(64, Math.Max(8, QueuedDeathRetries.Count * 8));
            while (QueuedDeathRetries.Count > 0 && budget-- > 0)
                ProcessDeathRetries(pIgnoreBackoff: true);
            return QueuedDeathRetries.Count == 0 && PendingDeathsByActor.Count == 0;
        }

        private static void QueueDeathRetry(PendingSchoolDeath pending, bool pDestroy)
        {
            if (pending?.Actor?.data == null || pending.Actor.data.id < 0) return;
            long actorId = pending.Actor.data.id;
            PendingDeathsByActor[actorId] = pending;
            if (pDestroy) pending.DestroyRequested = true;
            if (pending.ReadyFrame <= 0) pending.ReadyFrame = _deathRetryFrame;
            if (QueuedDeathRetries.Add(actorId)) PendingDeathRetries.Enqueue(actorId);
        }

        internal static bool ShouldDeferDestroy(Actor pActor)
        {
            if (pActor?.data == null ||
                !PendingDeathsByActor.TryGetValue(pActor.data.id,
                    out PendingSchoolDeath pending) ||
                !ReferenceEquals(pending.Actor, pActor)) return false;
            pending.DestroyRequested = true;
            return true;
        }

        private static int DeathRetryBackoffFrames(int pAttempts)
        {
            int exponent = Math.Max(0, Math.Min(8, pAttempts - 1));
            return Math.Min(MaxDeathRetryBackoffFrames, 1 << exponent);
        }

        private static void ClearDeathRetry(long pActorId, bool pCancelQueued)
        {
            if (pCancelQueued) QueuedDeathRetries.Remove(pActorId);
            PendingDeathsByActor.Remove(pActorId);
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

        public static int LivingCount(string pSchoolId)
        {
            int count = 0;
            foreach (long actorId in Memberships.Members(pSchoolId))
            {
                Actor actor = World.world?.units?.get(actorId);
                if (actor?.data != null && actor.isAlive() && !actor.isRekt()) count++;
            }
            return count;
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

        internal static IEnumerable<SchoolMembershipRecord> ActiveMemberships()
        {
            return Memberships.ActiveRecords();
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
            PendingDeathRetries.Clear();
            QueuedDeathRetries.Clear();
            PendingDeathsByActor.Clear();
            _deathRetryFrame = 0L;
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
