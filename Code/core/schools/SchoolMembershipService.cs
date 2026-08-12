using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
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

        private sealed class MembershipWriteEvent
        {
            public string EventType;
            public long TargetActorId;
            public long CityId;
            public long KingdomId;
            public int Year;
            public string Payload;
            public int Importance;
            public double WorldTime;
        }

        private static readonly SchoolMembershipBook Memberships = new SchoolMembershipBook();
        private static readonly Queue<long> PendingDeathRetries = new Queue<long>();
        private static readonly HashSet<long> QueuedDeathRetries = new HashSet<long>();
        private static readonly Dictionary<long, PendingSchoolDeath> PendingDeathsByActor =
            new Dictionary<long, PendingSchoolDeath>();
        private const int MaxDeathRetryQueueScan = 16;
        private static long _deathRetryFrame;
        private static int _standingWorkYear = -1;
        private static int _completedStandingYear = -1;
        private static long[] _duePromotionActorIds = Array.Empty<long>();
        private static int _promotionActorIndex;
        private static int _leaderSchoolIndex;
        private static readonly HashSet<string> PendingLeaderSchools =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<long> PendingMembershipActors =
            new HashSet<long>();
        private static readonly ConcurrentDictionary<long, long> MembershipWriteEpochs =
            new ConcurrentDictionary<long, long>();
        private static long _nextMembershipWriteEpoch;
        private static long _nextReservedMembershipId = -1L;

        public static long Version => Memberships.Version;
        internal static int PendingDeathCount => PendingDeathsByActor.Count;

        public static string GetSchool(long pActorId)
        {
            return Memberships.GetSchool(pActorId);
        }

        public static SchoolMembershipRecord GetActive(long pActorId)
        {
            return Memberships.GetActive(pActorId);
        }

        internal static bool IsJoinPending(long pActorId)
        {
            return pActorId >= 0 && PendingMembershipActors.Contains(pActorId);
        }

        public static bool ApplyReputationDelta(long pActorId, float pDelta)
        {
            if (pActorId < 0 || IsJoinPending(pActorId) ||
                float.IsNaN(pDelta) || float.IsInfinity(pDelta)) return false;
            SchoolMembershipRecord oldRecord = Memberships.GetActive(pActorId);
            if (oldRecord == null || !Memberships.UpdateReputation(pActorId, pDelta))
                return false;
            SchoolMembershipRecord nextRecord = Memberships.GetActive(pActorId);
            HistoricalSchoolRevisionService.ApplyMembershipChange(oldRecord, nextRecord);
            RefreshRuntimeIndex(pActorId);
            return true;
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
            long membershipId = ReserveMembershipId();
            if (membershipId < 0) return null;
            int year = Date.getCurrentYear();
            var record = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                SchoolMembershipSource.HistoricalDescent, pMasterId, -1, pCityId,
                pGeneration, Math.Max(0f, pInitialReputation), year,
                pLoyaltyUntilYear: LoyaltyUntil(year));
            return record.IsValid ? record : null;
        }

        internal static bool AdoptCommittedHistoricalDescent(Actor pActor,
            SchoolMembershipRecord pRecord)
        {
            if (pActor?.data == null || pRecord == null || !pRecord.IsValid ||
                !pRecord.Active || pRecord.ActorId != pActor.data.id ||
                pRecord.Source != SchoolMembershipSource.HistoricalDescent) return false;
            SchoolMembershipRecord existing = Memberships.GetActive(pRecord.ActorId);
            bool added = false;
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
            else
            {
                added = true;
            }
            if (added)
                HistoricalSchoolRevisionService.ApplyMembershipChange(null, pRecord);
            RefreshRuntimeIndex(pRecord.ActorId);
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
                IsJoinPending(pActor.data.id) ||
                CourtSchoolRegistry.Find(pSchoolId) == null || string.IsNullOrWhiteSpace(pSourceId))
                return false;
            SchoolMembershipRecord existing = Memberships.GetActive(pActor.data.id);
            if (existing != null)
                return existing.SchoolId == pSchoolId && existing.Source == pSource &&
                       existing.SourceId == pSourceId;

            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
                return false;
            long membershipId = ReserveMembershipId();
            if (membershipId < 0) return false;
            int year = Date.getCurrentYear();
            var record = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                pSource, pSourceId, pTeacherActorId, pCityId, pGeneration,
                Math.Max(0f, pInitialReputation), year,
                pLoyaltyUntilYear: LoyaltyUntil(year));
            if (!record.IsValid || !HistoricalSchoolStore.InsertMembership(record, WorldTime()))
                return false;
            if (!Memberships.TryJoin(record))
            {
                LoadIndexes();
                return false;
            }
            HistoricalSchoolRevisionService.ApplyMembershipChange(null, record);
            RefreshRuntimeIndex(record.ActorId);
            bool needsTravelAffiliation =
                pSource != SchoolMembershipSource.HistoricalDescent;
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
                IsJoinPending(pActor.data.id) ||
                CourtSchoolRegistry.Find(pSchoolId) == null || string.IsNullOrWhiteSpace(pSourceId))
                return false;
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null || current.Source == SchoolMembershipSource.HistoricalDescent ||
                current.SchoolId == pSchoolId) return false;
            if (!HistoricalAffiliationService.EnsureMemberAffiliation(pActor, pCityId))
                return false;
            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
                return false;
            long membershipId = ReserveMembershipId();
            if (membershipId < 0) return false;
            int year = Date.getCurrentYear();
            var replacement = new SchoolMembershipRecord(membershipId, pActor.data.id, pSchoolId,
                SchoolMembershipSource.ExplicitConversion, pSourceId, -1, pCityId, 0,
                current.Reputation, year,
                pLoyaltyUntilYear: LoyaltyUntil(year));
            if (!HistoricalSchoolStore.ConvertMembership(current, replacement, year, WorldTime()))
                return false;
            if (!Memberships.TryConvert(pActor.data.id, replacement, year, out _))
            {
                LoadIndexes();
                return false;
            }
            HistoricalSchoolRevisionService.ApplyMembershipChange(current, replacement);
            RefreshRuntimeIndex(pActor.data.id);
            Project(pActor, pSchoolId);
            return true;
        }

        internal static bool TryQueueJoin(Actor pActor, string pSchoolId,
            SchoolMembershipSource pSource, string pSourceId, long pTeacherActorId,
            long pCityId, int pGeneration, float pInitialReputation,
            string pEventType, long pEventTargetActorId, long pKingdomId,
            int pEventYear, string pPayload, int pImportance,
            Action<bool> pCompletion = null)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                CourtSchoolRegistry.Find(pSchoolId) == null ||
                string.IsNullOrWhiteSpace(pSourceId) ||
                string.IsNullOrWhiteSpace(pEventType) ||
                Memberships.GetActive(pActor.data.id) != null ||
                !PendingMembershipActors.Add(pActor.data.id)) return false;
            long writeEpoch = BeginMembershipWrite(pActor.data.id);
            if (!HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
            {
                ReleaseMembershipWrite(pActor.data.id, writeEpoch);
                return false;
            }
            long membershipId = ReserveMembershipId();
            int year = Date.getCurrentYear();
            var record = new SchoolMembershipRecord(membershipId, pActor.data.id,
                pSchoolId, pSource, pSourceId, pTeacherActorId, pCityId, pGeneration,
                Math.Max(0f, pInitialReputation), year,
                pLoyaltyUntilYear: LoyaltyUntil(year));
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id) == null &&
                pSource != SchoolMembershipSource.HistoricalDescent
                    ? HistoricalAffiliationService.PrepareMemberAffiliation(
                        pActor, pCityId, year)
                    : null;
            if (membershipId < 0 || !record.IsValid ||
                (pSource != SchoolMembershipSource.HistoricalDescent &&
                 HistoricalAffiliationService.Get(pActor.data.id) == null &&
                 affiliation == null))
            {
                ReleaseMembershipWrite(pActor.data.id, writeEpoch);
                return false;
            }
            var eventData = new MembershipWriteEvent
            {
                EventType = pEventType,
                TargetActorId = pEventTargetActorId,
                CityId = pCityId,
                KingdomId = pKingdomId,
                Year = pEventYear,
                Payload = pPayload ?? "",
                Importance = Math.Max(0, pImportance),
                WorldTime = WorldTime()
            };
            var operation = new MembershipJoinWriteOperation(pActor, record,
                affiliation, eventData, writeEpoch, pCompletion);
            if (HistoricalSchoolWriteBufferService.TryEnqueue(operation)) return true;
            ReleaseMembershipWrite(pActor.data.id, writeEpoch);
            return false;
        }

        internal static bool TryQueueConversion(Actor pActor, string pSchoolId,
            string pSourceId, long pCityId, string pEventType,
            long pEventTargetActorId, long pKingdomId, int pEventYear,
            string pPayload, int pImportance, Action<bool> pCompletion = null)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                CourtSchoolRegistry.Find(pSchoolId) == null ||
                string.IsNullOrWhiteSpace(pSourceId) ||
                string.IsNullOrWhiteSpace(pEventType) ||
                !PendingMembershipActors.Add(pActor.data.id)) return false;
            long writeEpoch = BeginMembershipWrite(pActor.data.id);
            SchoolMembershipRecord current = Memberships.GetActive(pActor.data.id);
            if (current == null || current.Source ==
                    SchoolMembershipSource.HistoricalDescent ||
                current.SchoolId == pSchoolId ||
                !HistoricalSchoolDescentService.FlushPendingDescentsForSchoolWrite())
            {
                ReleaseMembershipWrite(pActor.data.id, writeEpoch);
                return false;
            }
            int year = Date.getCurrentYear();
            long membershipId = ReserveMembershipId();
            var replacement = new SchoolMembershipRecord(membershipId,
                pActor.data.id, pSchoolId, SchoolMembershipSource.ExplicitConversion,
                pSourceId, -1L, pCityId, 0, current.Reputation, year,
                pLoyaltyUntilYear: LoyaltyUntil(year));
            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActor.data.id) == null
                    ? HistoricalAffiliationService.PrepareMemberAffiliation(
                        pActor, pCityId, year)
                    : null;
            if (membershipId < 0 || !replacement.IsValid ||
                (HistoricalAffiliationService.Get(pActor.data.id) == null &&
                 affiliation == null))
            {
                ReleaseMembershipWrite(pActor.data.id, writeEpoch);
                return false;
            }
            var eventData = new MembershipWriteEvent
            {
                EventType = pEventType,
                TargetActorId = pEventTargetActorId,
                CityId = pCityId,
                KingdomId = pKingdomId,
                Year = pEventYear,
                Payload = pPayload ?? "",
                Importance = Math.Max(0, pImportance),
                WorldTime = WorldTime()
            };
            var operation = new MembershipConversionWriteOperation(pActor, current,
                replacement, affiliation, eventData, writeEpoch, pCompletion);
            if (HistoricalSchoolWriteBufferService.TryEnqueue(operation)) return true;
            ReleaseMembershipWrite(pActor.data.id, writeEpoch);
            return false;
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
            HistoricalSchoolRevisionService.ApplyMembershipChange(replacement, pOriginal);
            RefreshRuntimeIndex(pActor.data.id);
            Project(pActor, pOriginal.SchoolId);
            return true;
        }

        public static SchoolDeathOutcome OnDeath(Actor pActor, bool pDestroy = false)
        {
            if (pActor?.data == null) return SchoolDeathOutcome.NotApplicable;
            InvalidateMembershipWrite(pActor.data.id);
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
            QueueDeathRetry(pending, pDestroy);
            return SchoolDeathOutcome.Failed;
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
                    RequestLeaderElection(current);
                    HistoricalSchoolRevisionService.ApplyMembershipChange(current, null);
                    HistoricalSchoolRuntimeIndex.Instance.Remove(pActor.data.id);
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
            try { HistoricalSchoolTaskLeaseService.ReleaseActor(pActor.data.id); }
            catch (Exception error)
            {
                ModClass.LogWarning("Committed school death task lease cleanup failed: " +
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
            return ActorDeathArchiveRules.RetryDelayFrames(pAttempts);
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
            HistoricalSchoolRevisionService.ApplyMembershipChange(current, null);
            HistoricalSchoolRuntimeIndex.Instance.Remove(pActor.data.id);
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
            return HistoricalSchoolRuntimeIndex.Instance.MemberCount(
                pSchoolId);
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

        internal static bool ProcessStandingFrame(int pYear)
        {
            if (pYear < 0 || _completedStandingYear == pYear) return true;
            if (_standingWorkYear != pYear)
            {
                _standingWorkYear = pYear;
                _duePromotionActorIds =
                    HistoricalSchoolRuntimeIndex.Instance.PromotionDueIds(pYear);
                _promotionActorIndex = 0;
                _leaderSchoolIndex = 0;
            }

            if (_promotionActorIndex < _duePromotionActorIds.Length)
            {
                long actorId = _duePromotionActorIds[_promotionActorIndex++];
                TryPromoteTeacher(actorId, pYear);
                return false;
            }

            if (_leaderSchoolIndex < CourtSchoolRegistry.All.Count)
            {
                string schoolId = CourtSchoolRegistry.All[_leaderSchoolIndex++].Id;
                EnsureSchoolLeader(schoolId);
                PendingLeaderSchools.Remove(schoolId);
                return false;
            }

            _completedStandingYear = pYear;
            _standingWorkYear = -1;
            _duePromotionActorIds = Array.Empty<long>();
            return true;
        }

        private static void TryPromoteTeacher(long pActorId, int pYear)
        {
            SchoolMembershipRecord current = Memberships.GetActive(pActorId);
            if (current == null) return;
            HistoricalSchoolStanding nextStanding =
                HistoricalSchoolStandingRules.ResolvePromotion(
                    current.Standing, Math.Max(0, pYear - current.StartYear),
                    current.Reputation);
            if (nextStanding == current.Standing) return;
            SchoolMembershipRecord next =
                current.WithStanding(nextStanding);
            if (!HistoricalSchoolStore.UpdateMembershipStanding(
                    current, next, WorldTime())) return;
            AdoptCommittedStanding(current, nextStanding);
        }

        internal static bool TryPromoteContinuityTeacher(long pActorId, int pYear)
        {
            SchoolMembershipRecord current = Memberships.GetActive(pActorId);
            Actor actor = FindLivingActor(pActorId);
            if (current == null || actor == null ||
                !HistoricalSchoolRecoveryRules.ShouldPromoteContinuityTeacher(
                    HistoricalSchoolRuntimeIndex.Instance.MemberCount(current.SchoolId),
                    HistoricalSchoolRuntimeIndex.Instance.TeacherCount(current.SchoolId),
                    current.Standing,
                    HistoricalAffiliationService.IsPresentForInfluence(actor),
                    Math.Max(0, pYear - current.StartYear), current.Reputation))
                return false;
            SchoolMembershipRecord next =
                current.WithStanding(HistoricalSchoolStanding.Teacher);
            return HistoricalSchoolStore.UpdateMembershipStanding(
                       current, next, WorldTime()) &&
                   AdoptCommittedStanding(current,
                       HistoricalSchoolStanding.Teacher);
        }

        private static void EnsureSchoolLeader(string pSchoolId)
        {
            if (string.IsNullOrEmpty(pSchoolId)) return;
            long[] teacherIds =
                HistoricalSchoolRuntimeIndex.Instance.TeacherIds(pSchoolId);
            var candidates = new List<HistoricalSchoolLeaderCandidate>(teacherIds.Length);
            var currentLeaders = new List<long>();
            bool canonicalPresent = false;

            foreach (long actorId in teacherIds)
            {
                SchoolMembershipRecord membership = Memberships.GetActive(actorId);
                if (membership == null || membership.SchoolId != pSchoolId) continue;
                Actor actor = FindLivingActor(actorId);
                bool available = actor != null &&
                    HistoricalAffiliationService.IsPresentForInfluence(actor);
                if (membership.Standing == HistoricalSchoolStanding.CanonicalMaster &&
                    available)
                    canonicalPresent = true;
                if (membership.Standing == HistoricalSchoolStanding.Leader)
                    currentLeaders.Add(actorId);
                if (membership.Standing == HistoricalSchoolStanding.Teacher ||
                    membership.Standing == HistoricalSchoolStanding.Leader)
                    candidates.Add(new HistoricalSchoolLeaderCandidate(
                        actorId, membership.StartYear, membership.Standing, available));
            }

            long selected = canonicalPresent
                ? -1L
                : HistoricalSchoolStandingRules.SelectLeaderActorId(candidates);
            bool exact = selected < 0
                ? currentLeaders.Count == 0
                : currentLeaders.Count == 1 && currentLeaders[0] == selected;
            if (exact) return;
            if (!HistoricalSchoolStore.UpdateSchoolLeader(
                    pSchoolId, selected, WorldTime())) return;

            foreach (long actorId in currentLeaders)
                if (actorId != selected)
                    AdoptCommittedStanding(actorId, HistoricalSchoolStanding.Teacher);
            if (selected >= 0)
                AdoptCommittedStanding(selected, HistoricalSchoolStanding.Leader);
        }

        private static bool AdoptCommittedStanding(
            SchoolMembershipRecord pExpected,
            HistoricalSchoolStanding pStanding)
        {
            if (pExpected == null) return false;
            return AdoptCommittedStanding(pExpected.ActorId, pStanding);
        }

        private static bool AdoptCommittedStanding(
            long pActorId,
            HistoricalSchoolStanding pStanding)
        {
            SchoolMembershipRecord current = Memberships.GetActive(pActorId);
            if (current == null) return false;
            if (!Memberships.ReplaceStanding(
                    pActorId, current.MembershipId, pStanding,
                    out SchoolMembershipRecord previous,
                    out SchoolMembershipRecord next))
            {
                LoadIndexes();
                return Memberships.GetActive(pActorId)?.Standing == pStanding;
            }
            if (!ReferenceEquals(previous, next))
            {
                HistoricalSchoolRevisionService.ApplyMembershipChange(previous, next);
                RefreshRuntimeIndex(pActorId);
            }
            return true;
        }

        private static void RequestLeaderElection(SchoolMembershipRecord pMembership)
        {
            if (pMembership == null || string.IsNullOrEmpty(pMembership.SchoolId)) return;
            if (pMembership.Standing == HistoricalSchoolStanding.Teacher ||
                pMembership.Standing == HistoricalSchoolStanding.Leader ||
                pMembership.Standing == HistoricalSchoolStanding.CanonicalMaster)
                PendingLeaderSchools.Add(pMembership.SchoolId);
        }

        private static Actor FindLivingActor(long pActorId)
        {
            Actor actor = FindActor(pActorId);
            return actor?.data != null && actor.isAlive() && !actor.isRekt()
                ? actor
                : null;
        }

        private static Actor FindActor(long pActorId)
        {
            try
            {
                return pActorId >= 0
                    ? World.world?.units?.get(pActorId)
                    : null;
            }
            catch
            {
                return null;
            }
        }

        internal static void RefreshRuntimeIndex(long pActorId)
        {
            SchoolMembershipRecord membership = Memberships.GetActive(pActorId);
            Actor actor = FindActor(pActorId);
            bool actorExists = actor?.data != null;
            bool actorAlive = actorExists && actor.isAlive();
            bool actorWrecked = !actorExists || actor.isRekt();
            if (!HistoricalSchoolRuntimeMembershipRules.ShouldIndex(
                    membership != null && membership.Active, actorExists,
                    actorAlive, actorWrecked))
            {
                HistoricalSchoolRuntimeIndex.Instance.Remove(pActorId);
                return;
            }

            HistoricalSchoolAffiliationSnapshot affiliation =
                HistoricalAffiliationService.Get(pActorId);
            long residenceCityId = affiliation?.ResidenceCityId ?? membership.CityId;
            bool travelling = affiliation != null &&
                (affiliation.LifecycleState == HistoricalSchoolLifecycleState.Travelling ||
                 affiliation.LifecycleState == HistoricalSchoolLifecycleState.Voyage);
            bool travelEligible = affiliation != null &&
                HistoricalAffiliationService.IsTravelEligible(actor);
            bool directDiscipleship =
                membership.Source == SchoolMembershipSource.DirectDiscipleship ||
                membership.Source == SchoolMembershipSource.LaterDiscipleship;
            HistoricalSchoolRuntimeIndex.Instance.Upsert(
                new HistoricalSchoolIndexEntry(
                    pActorId,
                    membership.SchoolId,
                    residenceCityId,
                    ResolveStanding(membership),
                    HistoricalSchoolRevisionService.IsPresent(affiliation),
                    travelling,
                    affiliation?.ServiceKingdomId ?? -1L,
                    HistoricalSchoolRules.TravelBucket(pActorId),
                    PromotionDueYear(membership),
                    travelEligible,
                    membership.TeacherActorId,
                    directDiscipleship));
        }

        private static HistoricalSchoolStanding ResolveStanding(
            SchoolMembershipRecord pMembership)
        {
            return pMembership.Standing;
        }

        private static int PromotionDueYear(SchoolMembershipRecord pMembership)
        {
            return pMembership == null
                ? -1
                : HistoricalSchoolStandingRules.PromotionDueYear(
                    pMembership.Standing, pMembership.StartYear);
        }

        private abstract class MembershipWriteOperation :
            IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation
        {
            protected MembershipWriteOperation(Actor pActor,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, long pWriteEpoch,
                Action<bool> pCompletion)
            {
                Actor = pActor;
                ActorId = pActor?.data?.id ?? -1L;
                Affiliation = pAffiliation;
                Event = pEvent;
                WriteEpoch = pWriteEpoch;
                Completion = pCompletion;
            }

            protected Actor Actor { get; }
            protected long ActorId { get; }
            protected HistoricalSchoolAffiliationSnapshot Affiliation { get; }
            protected MembershipWriteEvent Event { get; }
            protected long WriteEpoch { get; }
            private Action<bool> Completion { get; }
            private bool _compensationQueued;

            public abstract string OperationKey { get; }
            public abstract HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction);
            public abstract IHistoricalSchoolBackgroundWrite DetachBackgroundWrite();

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                if (!CanProjectCommittedMembership())
                {
                    if (TryQueueCompensation()) return;
                    throw new InvalidOperationException(
                        "committed school membership compensation queue is unavailable");
                }
                try
                {
                    HistoricalSchoolStore.InvalidateTeachingCommit(Event.CityId);
                    if (!Adopt())
                        throw new InvalidOperationException(
                            "committed school membership adoption failed");
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Committed school membership projection failed: " +
                                        error.Message);
                    throw;
                }
                Complete(true);
            }

            public void OnCleanFailure()
            {
                Complete(false);
            }

            protected abstract SchoolMembershipRecord CommittedMembership { get; }
            protected virtual SchoolMembershipRecord OriginalMembership => null;

            private bool CanProjectCommittedMembership()
            {
                return IsMembershipWriteCurrent(ActorId, WriteEpoch) &&
                       CanPersistPendingActor(Actor, ActorId);
            }

            private bool TryQueueCompensation()
            {
                if (_compensationQueued) return true;
                var compensation = new MembershipCompensationWriteOperation(
                    Actor, CopyMembership(OriginalMembership),
                    CopyMembership(CommittedMembership), Event?.Year ?? -1,
                    Event?.WorldTime ?? 0d, WriteEpoch, Complete);
                if (!HistoricalSchoolWriteBufferService.TryEnqueue(compensation))
                    return false;
                _compensationQueued = true;
                return true;
            }

            private void Complete(bool pSuccess)
            {
                ReleaseMembershipWrite(ActorId, WriteEpoch);
                try { Completion?.Invoke(pSuccess); }
                catch (Exception error)
                {
                    ModClass.LogWarning("School membership completion failed: " +
                                        error.Message);
                }
            }

            protected HistoricalSchoolTeachingPersistenceOutcome PersistAffiliation(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction,
                HistoricalSchoolTeachingPersistenceOutcome pCurrent)
            {
                if (Affiliation == null) return pCurrent;
                HistoricalSchoolTeachingPersistenceOutcome next =
                    HistoricalSchoolStore.EnsureMemberAffiliationInTransaction(pDb,
                        pTransaction, Affiliation, Event.WorldTime);
                return MergeAfterWrite(pCurrent, next);
            }

            protected HistoricalSchoolTeachingPersistenceOutcome PersistEvent(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction,
                SchoolMembershipRecord pMembership,
                HistoricalSchoolTeachingPersistenceOutcome pCurrent)
            {
                HistoricalSchoolTeachingPersistenceOutcome next =
                    HistoricalSchoolStore.RecordSchoolEventInTransaction(pDb,
                        pTransaction, "school-membership-event:" + OperationKey,
                        Event.EventType, pMembership.ActorId, Event.TargetActorId,
                        pMembership.SchoolId, Event.CityId, Event.KingdomId,
                        Event.Year, Event.Payload, Event.Importance, Event.WorldTime);
                return MergeAfterWrite(pCurrent, next);
            }

            protected bool AdoptAffiliation()
            {
                return Affiliation == null ||
                       HistoricalAffiliationService.AdoptCommittedMemberAffiliation(
                           Affiliation);
            }

            protected abstract bool Adopt();

            protected static HistoricalSchoolAffiliationSnapshot CopyAffiliation(
                HistoricalSchoolAffiliationSnapshot pAffiliation)
            {
                if (pAffiliation == null) return null;
                return new HistoricalSchoolAffiliationSnapshot(
                    pAffiliation.ActorId, pAffiliation.HomeKingdomId,
                    pAffiliation.HomeKingdomName, pAffiliation.HometownCityId,
                    pAffiliation.ResidenceCityId,
                    pAffiliation.PreviousResidenceCityId,
                    pAffiliation.DestinationCityId,
                    pAffiliation.ServiceKingdomId,
                    pAffiliation.LifecycleState,
                    pAffiliation.ServiceStartYear,
                    pAffiliation.ServiceEndYear,
                    pAffiliation.LastTravelYear,
                    pAffiliation.TravelWaitStartYear,
                    pAffiliation.VoyageStartYear,
                    pAffiliation.VoyageArrivalYear,
                    pAffiliation.TransportFailures);
            }

            protected static SchoolMembershipRecord CopyMembership(
                SchoolMembershipRecord pRecord)
            {
                if (pRecord == null) return null;
                return new SchoolMembershipRecord(pRecord.MembershipId,
                    pRecord.ActorId, pRecord.SchoolId, pRecord.Source,
                    pRecord.SourceId, pRecord.TeacherActorId, pRecord.CityId,
                    pRecord.Generation, pRecord.Reputation, pRecord.StartYear,
                    pRecord.EndYear, pRecord.Active, pRecord.EndReason,
                    pRecord.Standing, pRecord.LoyaltyUntilYear);
            }

            protected static MembershipWriteEvent CopyEvent(
                MembershipWriteEvent pEvent)
            {
                if (pEvent == null) return null;
                return new MembershipWriteEvent
                {
                    EventType = pEvent.EventType ?? "",
                    TargetActorId = pEvent.TargetActorId,
                    CityId = pEvent.CityId,
                    KingdomId = pEvent.KingdomId,
                    Year = pEvent.Year,
                    Payload = pEvent.Payload ?? "",
                    Importance = pEvent.Importance,
                    WorldTime = pEvent.WorldTime
                };
            }

            private static HistoricalSchoolTeachingPersistenceOutcome MergeAfterWrite(
                HistoricalSchoolTeachingPersistenceOutcome pCurrent,
                HistoricalSchoolTeachingPersistenceOutcome pNext)
            {
                if (pCurrent == HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                    pNext == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                    return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
                if (pNext == HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                    return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
                return pCurrent == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                       pNext == HistoricalSchoolTeachingPersistenceOutcome.Committed
                    ? HistoricalSchoolTeachingPersistenceOutcome.Committed
                    : HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            }
        }

        private sealed class MembershipJoinWriteOperation : MembershipWriteOperation
        {
            private readonly SchoolMembershipRecord _record;

            public MembershipJoinWriteOperation(Actor pActor,
                SchoolMembershipRecord pRecord,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, long pWriteEpoch,
                Action<bool> pCompletion)
                : base(pActor, pAffiliation, pEvent, pWriteEpoch, pCompletion)
            {
                _record = pRecord;
            }

            public override string OperationKey => "membership-join:" +
                _record.ActorId + ":" + _record.SourceId;
            protected override SchoolMembershipRecord CommittedMembership => _record;

            public override HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                if (!CanPersistPendingActor(Actor, _record.ActorId))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                SchoolMembershipRecord active = Memberships.GetActive(_record.ActorId);
                if (active != null && !SameMembershipRecord(active, _record))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    HistoricalSchoolStore.InsertMembershipInTransaction(pDb,
                        pTransaction, _record, Event.WorldTime);
                if (outcome != HistoricalSchoolTeachingPersistenceOutcome.Committed &&
                    outcome != HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                    return outcome;
                outcome = PersistAffiliation(pDb, pTransaction, outcome);
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                    return outcome;
                return PersistEvent(pDb, pTransaction, _record, outcome);
            }

            public override IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                bool valid = CanPersistPendingActor(Actor, _record.ActorId);
                SchoolMembershipRecord active = Memberships.GetActive(_record.ActorId);
                valid &= active == null || SameMembershipRecord(active, _record);
                return new MembershipJoinBackgroundWrite(valid, WriteEpoch,
                    CopyMembership(_record), CopyAffiliation(Affiliation),
                    CopyEvent(Event), OperationKey);
            }

            protected override bool Adopt()
            {
                if (Actor?.data == null || _record.ActorId != Actor.data.id) return false;
                SchoolMembershipRecord existing = Memberships.GetActive(_record.ActorId);
                bool added = false;
                if (existing == null)
                {
                    if (!Memberships.TryJoin(_record))
                    {
                        LoadIndexes();
                        existing = Memberships.GetActive(_record.ActorId);
                    }
                    else
                    {
                        existing = _record;
                        added = true;
                    }
                }
                if (!SameMembershipRecord(existing, _record) || !AdoptAffiliation())
                    return false;
                if (added)
                    HistoricalSchoolRevisionService.ApplyMembershipChange(null, _record);
                RefreshRuntimeIndex(_record.ActorId);
                Project(Actor, _record.SchoolId);
                return true;
            }
        }

        private sealed class MembershipConversionWriteOperation : MembershipWriteOperation
        {
            private readonly SchoolMembershipRecord _current;
            private readonly SchoolMembershipRecord _replacement;

            public MembershipConversionWriteOperation(Actor pActor,
                SchoolMembershipRecord pCurrent,
                SchoolMembershipRecord pReplacement,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, long pWriteEpoch,
                Action<bool> pCompletion)
                : base(pActor, pAffiliation, pEvent, pWriteEpoch, pCompletion)
            {
                _current = pCurrent;
                _replacement = pReplacement;
            }

            public override string OperationKey => "membership-convert:" +
                _replacement.ActorId + ":" + _replacement.SourceId;
            protected override SchoolMembershipRecord CommittedMembership => _replacement;
            protected override SchoolMembershipRecord OriginalMembership => _current;

            public override HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                if (!CanPersistPendingActor(Actor, _replacement.ActorId))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                SchoolMembershipRecord active = Memberships.GetActive(
                    _replacement.ActorId);
                if (!SameMembershipRecord(active, _current) &&
                    !SameMembershipRecord(active, _replacement))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                HistoricalSchoolTeachingPersistenceOutcome outcome =
                    HistoricalSchoolStore.ConvertMembershipInTransaction(pDb,
                        pTransaction, _current, _replacement,
                        _replacement.StartYear, Event.WorldTime);
                if (outcome != HistoricalSchoolTeachingPersistenceOutcome.Committed &&
                    outcome != HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                    return outcome;
                outcome = PersistAffiliation(pDb, pTransaction, outcome);
                if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                    return outcome;
                return PersistEvent(pDb, pTransaction, _replacement, outcome);
            }

            public override IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                bool valid = CanPersistPendingActor(Actor, _replacement.ActorId);
                SchoolMembershipRecord active = Memberships.GetActive(
                    _replacement.ActorId);
                valid &= SameMembershipRecord(active, _current) ||
                         SameMembershipRecord(active, _replacement);
                return new MembershipConversionBackgroundWrite(valid, WriteEpoch,
                    CopyMembership(_current), CopyMembership(_replacement),
                    CopyAffiliation(Affiliation), CopyEvent(Event), OperationKey);
            }

            protected override bool Adopt()
            {
                if (Actor?.data == null || _replacement.ActorId != Actor.data.id)
                    return false;
                SchoolMembershipRecord active = Memberships.GetActive(_replacement.ActorId);
                bool changed = false;
                if (!SameMembershipRecord(active, _replacement))
                {
                    if (!SameMembershipRecord(active, _current) ||
                        !Memberships.TryConvert(_replacement.ActorId, _replacement,
                            _replacement.StartYear, out _))
                    {
                        LoadIndexes();
                        active = Memberships.GetActive(_replacement.ActorId);
                    }
                    else
                    {
                        active = _replacement;
                        changed = true;
                    }
                }
                if (!SameMembershipRecord(active, _replacement) || !AdoptAffiliation())
                    return false;
                if (changed)
                    HistoricalSchoolRevisionService.ApplyMembershipChange(_current,
                        _replacement);
                RefreshRuntimeIndex(_replacement.ActorId);
                Project(Actor, _replacement.SchoolId);
                return true;
            }
        }

        private abstract class MembershipBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly bool _valid;
            private readonly long _writeEpoch;
            private readonly HistoricalSchoolAffiliationSnapshot _affiliation;
            private readonly MembershipWriteEvent _event;
            private readonly string _operationKey;

            protected MembershipBackgroundWrite(bool pValid, long pWriteEpoch,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, string pOperationKey)
            {
                _valid = pValid;
                _writeEpoch = pWriteEpoch;
                _affiliation = pAffiliation;
                _event = pEvent;
                _operationKey = pOperationKey ?? "";
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                if (!_valid || !IsMembershipWriteCurrent(
                        EventMembership?.ActorId ?? -1L, _writeEpoch))
                    return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
                HistoricalSchoolTeachingPersistenceOutcome outcome = PersistMembership(
                    pDb, pTransaction);
                if (outcome != HistoricalSchoolTeachingPersistenceOutcome.Committed &&
                    outcome != HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                    return outcome;
                if (_affiliation != null)
                {
                    outcome = MergeAfterWrite(outcome,
                        HistoricalSchoolStore.EnsureMemberAffiliationInTransaction(
                            pDb, pTransaction, _affiliation, _event.WorldTime));
                    if (outcome == HistoricalSchoolTeachingPersistenceOutcome.Unknown)
                        return outcome;
                }
                SchoolMembershipRecord membership = EventMembership;
                outcome = MergeAfterWrite(outcome,
                    HistoricalSchoolStore.RecordSchoolEventInTransaction(pDb,
                        pTransaction, "school-membership-event:" + _operationKey,
                        _event.EventType, membership.ActorId,
                        _event.TargetActorId, membership.SchoolId, _event.CityId,
                        _event.KingdomId, _event.Year, _event.Payload,
                        _event.Importance, _event.WorldTime));
                if (outcome != HistoricalSchoolTeachingPersistenceOutcome.Committed &&
                    outcome != HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                    return outcome;
                return IsMembershipWriteCurrent(membership.ActorId, _writeEpoch)
                    ? outcome
                    : HistoricalSchoolTeachingPersistenceOutcome.Unknown;
            }

            protected abstract SchoolMembershipRecord EventMembership { get; }
            protected double WorldTime => _event.WorldTime;
            protected abstract HistoricalSchoolTeachingPersistenceOutcome
                PersistMembership(System.Data.SQLite.SQLiteConnection pDb,
                    System.Data.SQLite.SQLiteTransaction pTransaction);

            private static HistoricalSchoolTeachingPersistenceOutcome MergeAfterWrite(
                HistoricalSchoolTeachingPersistenceOutcome pCurrent,
                HistoricalSchoolTeachingPersistenceOutcome pNext)
            {
                if (pCurrent == HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                    pNext == HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                    pNext == HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                    return HistoricalSchoolTeachingPersistenceOutcome.Unknown;
                return pCurrent == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                       pNext == HistoricalSchoolTeachingPersistenceOutcome.Committed
                    ? HistoricalSchoolTeachingPersistenceOutcome.Committed
                    : HistoricalSchoolTeachingPersistenceOutcome.Replayed;
            }
        }

        private sealed class MembershipJoinBackgroundWrite :
            MembershipBackgroundWrite
        {
            private readonly SchoolMembershipRecord _record;

            public MembershipJoinBackgroundWrite(bool pValid, long pWriteEpoch,
                SchoolMembershipRecord pRecord,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, string pOperationKey)
                : base(pValid, pWriteEpoch, pAffiliation, pEvent, pOperationKey)
            {
                _record = pRecord;
            }

            protected override SchoolMembershipRecord EventMembership => _record;

            protected override HistoricalSchoolTeachingPersistenceOutcome
                PersistMembership(System.Data.SQLite.SQLiteConnection pDb,
                    System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return HistoricalSchoolStore.InsertMembershipInTransaction(pDb,
                    pTransaction, _record, WorldTime);
            }
        }

        private sealed class MembershipConversionBackgroundWrite :
            MembershipBackgroundWrite
        {
            private readonly SchoolMembershipRecord _current;
            private readonly SchoolMembershipRecord _replacement;

            public MembershipConversionBackgroundWrite(bool pValid, long pWriteEpoch,
                SchoolMembershipRecord pCurrent,
                SchoolMembershipRecord pReplacement,
                HistoricalSchoolAffiliationSnapshot pAffiliation,
                MembershipWriteEvent pEvent, string pOperationKey)
                : base(pValid, pWriteEpoch, pAffiliation, pEvent, pOperationKey)
            {
                _current = pCurrent;
                _replacement = pReplacement;
            }

            protected override SchoolMembershipRecord EventMembership => _replacement;

            protected override HistoricalSchoolTeachingPersistenceOutcome
                PersistMembership(System.Data.SQLite.SQLiteConnection pDb,
                    System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return HistoricalSchoolStore.ConvertMembershipInTransaction(pDb,
                    pTransaction, _current, _replacement,
                    _replacement.StartYear, WorldTime);
            }
        }

        private sealed class MembershipCompensationWriteOperation :
            IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation
        {
            private readonly Actor _actor;
            private readonly SchoolMembershipRecord _original;
            private readonly SchoolMembershipRecord _committed;
            private readonly int _year;
            private readonly double _worldTime;
            private readonly long _writeEpoch;
            private readonly Action<bool> _completion;

            public MembershipCompensationWriteOperation(Actor pActor,
                SchoolMembershipRecord pOriginal,
                SchoolMembershipRecord pCommitted, int pYear, double pWorldTime,
                long pWriteEpoch, Action<bool> pCompletion)
            {
                _actor = pActor;
                _original = pOriginal;
                _committed = pCommitted;
                _year = Math.Max(pCommitted?.StartYear ?? 0, pYear);
                _worldTime = Math.Max(0d, pWorldTime);
                _writeEpoch = pWriteEpoch;
                _completion = pCompletion;
            }

            public string OperationKey => "membership-compensation:" +
                (_committed?.ActorId ?? -1L) + ":" +
                (_committed?.MembershipId ?? -1L);

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return CompensateMembershipInTransaction(pDb, pTransaction,
                    _original, _committed, _year, _worldTime);
            }

            public IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                return new MembershipCompensationBackgroundWrite(
                    CopyMembershipForCompensation(_original),
                    CopyMembershipForCompensation(_committed), _year, _worldTime);
            }

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                SchoolMembershipRecord active = _committed == null
                    ? null
                    : Memberships.GetActive(_committed.ActorId);
                if (_original == null && SameMembershipRecord(active, _committed))
                {
                    Memberships.CloseExpected(_committed.ActorId,
                        _committed.MembershipId, _year,
                        "invalidated_before_projection", out _);
                    HistoricalSchoolRevisionService.ApplyMembershipChange(
                        _committed, null);
                }
                else if (_original != null &&
                         SameMembershipRecord(active, _committed))
                {
                    Memberships.RollbackConvert(_committed.ActorId, _original,
                        _committed);
                    HistoricalSchoolRevisionService.ApplyMembershipChange(
                        _committed, _original);
                }
                if (_committed != null)
                {
                    RefreshRuntimeIndex(_committed.ActorId);
                    if (_actor?.data != null)
                        Project(_actor, GetSchool(_committed.ActorId));
                }
                _completion?.Invoke(false);
            }

            public void OnCleanFailure()
            {
                ModClass.LogWarning("School membership compensation failed: actor=" +
                                    (_committed?.ActorId ?? -1L) + " epoch=" +
                                    _writeEpoch);
                _completion?.Invoke(false);
            }
        }

        private sealed class MembershipCompensationBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly SchoolMembershipRecord _original;
            private readonly SchoolMembershipRecord _committed;
            private readonly int _year;
            private readonly double _worldTime;

            public MembershipCompensationBackgroundWrite(
                SchoolMembershipRecord pOriginal,
                SchoolMembershipRecord pCommitted, int pYear, double pWorldTime)
            {
                _original = pOriginal;
                _committed = pCommitted;
                _year = pYear;
                _worldTime = pWorldTime;
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return CompensateMembershipInTransaction(pDb, pTransaction,
                    _original, _committed, _year, _worldTime);
            }
        }

        private static HistoricalSchoolTeachingPersistenceOutcome
            CompensateMembershipInTransaction(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction,
                SchoolMembershipRecord pOriginal,
                SchoolMembershipRecord pCommitted, int pYear, double pWorldTime)
        {
            if (pCommitted == null)
                return HistoricalSchoolTeachingPersistenceOutcome.CleanFailure;
            return pOriginal == null
                ? HistoricalSchoolStore.CloseMembershipInTransaction(pDb,
                    pTransaction, pCommitted, Math.Max(pCommitted.StartYear, pYear),
                    "invalidated_before_projection", pWorldTime)
                : HistoricalSchoolStore.RollbackConversionInTransaction(pDb,
                    pTransaction, pOriginal, pCommitted,
                    Math.Max(pCommitted.StartYear, pYear), pWorldTime);
        }

        private static SchoolMembershipRecord CopyMembershipForCompensation(
            SchoolMembershipRecord pRecord)
        {
            if (pRecord == null) return null;
            return new SchoolMembershipRecord(pRecord.MembershipId,
                pRecord.ActorId, pRecord.SchoolId, pRecord.Source,
                pRecord.SourceId, pRecord.TeacherActorId, pRecord.CityId,
                pRecord.Generation, pRecord.Reputation, pRecord.StartYear,
                pRecord.EndYear, pRecord.Active, pRecord.EndReason,
                pRecord.Standing, pRecord.LoyaltyUntilYear);
        }

        private static long ReserveMembershipId()
        {
            long databaseNext = HistoricalSchoolStore.NextMembershipId();
            if (databaseNext < 0) return -1L;
            if (_nextReservedMembershipId < databaseNext)
                _nextReservedMembershipId = databaseNext;
            if (_nextReservedMembershipId == long.MaxValue) return -1L;
            return _nextReservedMembershipId++;
        }

        public static void LoadIndexes()
        {
            ResetStandingWork();
            if (HistoricalSchoolWriteBufferService.Count == 0)
            {
                PendingMembershipActors.Clear();
                MembershipWriteEpochs.Clear();
                _nextReservedMembershipId = -1L;
            }
            Memberships.Clear();
            HistoricalSchoolRuntimeIndex.Instance.ClearMembers();
            HistoricalSchoolRevisionService.Clear();
            var duplicates = new List<SchoolMembershipRecord>();
            foreach (SchoolMembershipRecord record in HistoricalSchoolStore.LoadActiveMemberships())
            {
                if (!Memberships.TryJoin(record))
                {
                    duplicates.Add(record);
                    continue;
                }
                HistoricalSchoolRevisionService.ApplyMembershipChange(null, record);
                RefreshRuntimeIndex(record.ActorId);
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
            ResetStandingWork();
            PendingMembershipActors.Clear();
            MembershipWriteEpochs.Clear();
            _nextReservedMembershipId = -1L;
            Memberships.Clear();
            HistoricalSchoolRuntimeIndex.Instance.ClearMembers();
            HistoricalSchoolRevisionService.Clear();
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
                   pFirst.EndReason == pSecond.EndReason &&
                   pFirst.Standing == pSecond.Standing &&
                   pFirst.LoyaltyUntilYear == pSecond.LoyaltyUntilYear;
        }

        private static bool CanPersistPendingActor(Actor pActor,
            long pExpectedActorId)
        {
            return SchoolMembershipPersistenceRules.CanPersistPendingActor(
                pActor?.data != null,
                pActor?.isAlive() == true,
                pActor?.isRekt() != false,
                pExpectedActorId,
                pActor?.data?.id ?? -1L);
        }

        private static long BeginMembershipWrite(long pActorId)
        {
            long epoch = Interlocked.Increment(ref _nextMembershipWriteEpoch);
            if (epoch <= 0)
            {
                Interlocked.Exchange(ref _nextMembershipWriteEpoch, 1L);
                epoch = 1L;
            }
            MembershipWriteEpochs[pActorId] = epoch;
            return epoch;
        }

        private static bool IsMembershipWriteCurrent(long pActorId, long pEpoch)
        {
            return pActorId >= 0 && pEpoch > 0 &&
                   MembershipWriteEpochs.TryGetValue(pActorId, out long current) &&
                   current == pEpoch;
        }

        private static void InvalidateMembershipWrite(long pActorId)
        {
            if (pActorId < 0) return;
            MembershipWriteEpochs.TryRemove(pActorId, out _);
        }

        private static void ReleaseMembershipWrite(long pActorId, long pEpoch)
        {
            if (pActorId < 0) return;
            if (MembershipWriteEpochs.TryGetValue(pActorId, out long current))
            {
                if (current != pEpoch) return;
                MembershipWriteEpochs.TryRemove(pActorId, out _);
            }
            PendingMembershipActors.Remove(pActorId);
        }

        private static int LoyaltyUntil(int pYear)
        {
            return pYear > int.MaxValue -
                   HistoricalSchoolStandingRules.ConversionLoyaltyYears
                ? int.MaxValue
                : pYear + HistoricalSchoolStandingRules.ConversionLoyaltyYears;
        }

        private static void ResetStandingWork()
        {
            _standingWorkYear = -1;
            _completedStandingYear = -1;
            _duePromotionActorIds = Array.Empty<long>();
            _promotionActorIndex = 0;
            _leaderSchoolIndex = 0;
            PendingLeaderSchools.Clear();
        }

        private static double WorldTime()
        {
            return World.world?.getCurWorldTime() ?? 0d;
        }
    }
}
