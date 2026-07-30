using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.schools
{
    internal static class SchoolLineageService
    {
        public const int DirectDiscipleCap = 8;
        private static readonly HistoricalSchoolTravelReservationBook
            ItinerantReservations = new HistoricalSchoolTravelReservationBook(
                HistoricalSchoolRules.MaxNonHistoricalItinerantsPerSchool);
        private static readonly Dictionary<long, long> SuccessorByTeacher =
            new Dictionary<long, long>();
        private static readonly HistoricalSchoolTransientIdGate ProcessingTeacherDeaths =
            new HistoricalSchoolTransientIdGate();

        public static void LoadState()
        {
            ItinerantReservations.Clear();
            SuccessorByTeacher.Clear();
            ProcessingTeacherDeaths.Clear();
            foreach (KeyValuePair<long, long> item in HistoricalSchoolStore.LoadLineageSuccessors())
                if (item.Key >= 0 && item.Value >= 0) SuccessorByTeacher[item.Key] = item.Value;
            for (int bucket = 0; bucket < 4; bucket++)
            {
                foreach (long actorId in
                         HistoricalSchoolRuntimeIndex.Instance.TravelEligibleIds(bucket))
                {
                    HistoricalSchoolAffiliationSnapshot state =
                        HistoricalAffiliationService.Get(actorId);
                    if (state?.LifecycleState != HistoricalSchoolLifecycleState.Travelling &&
                        state?.LifecycleState != HistoricalSchoolLifecycleState.Voyage) continue;
                    Actor actor = FindActor(actorId);
                    if (actor?.data == null) continue;
                    string schoolId =
                        SchoolMembershipService.GetSchool(actorId);
                    if (HistoricalSchoolTravelReservationRestoreRules.ShouldUseExamTravelerReservation(
                            activeTravel: true,
                            qualifiedTeacher: IsQualifiedTeacher(actor)))
                        TryReserveExamTraveler(actor, schoolId);
                    else
                        TryReserveItinerant(actor, schoolId);
                }
            }
        }

        public static void ClearRuntime()
        {
            ItinerantReservations.Clear();
            SuccessorByTeacher.Clear();
            ProcessingTeacherDeaths.Clear();
        }

        public static bool TryReserveItinerant(Actor pActor, string pSchoolId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                string.IsNullOrWhiteSpace(pSchoolId) || CourtSchoolRegistry.Find(pSchoolId) == null)
                return false;
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return true;
            if (!IsQualifiedTeacher(pActor)) return false;
            SchoolMembershipRecord membership = SchoolMembershipService.GetActive(pActor.data.id);
            if (membership == null || !string.Equals(membership.SchoolId, pSchoolId,
                    StringComparison.Ordinal)) return false;
            return ItinerantReservations.TryReserve(pSchoolId, pActor.data.id);
        }

        public static bool TryReserveExamTraveler(Actor pActor,
            string pSchoolId)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt() ||
                string.IsNullOrWhiteSpace(pSchoolId) ||
                CourtSchoolRegistry.Find(pSchoolId) == null)
                return false;
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor))
                return TryReserveItinerant(pActor, pSchoolId);
            SchoolMembershipRecord membership =
                SchoolMembershipService.GetActive(pActor.data.id);
            if (membership == null || !membership.Active ||
                !membership.IsValid ||
                !string.Equals(membership.SchoolId, pSchoolId,
                    StringComparison.Ordinal)) return false;
            return ItinerantReservations.TryReserve(pSchoolId,
                pActor.data.id);
        }

        public static void ReleaseItinerant(Actor pActor)
        {
            if (pActor?.data == null) return;
            ItinerantReservations.Release(pActor.data.id);
        }

        public static int ItinerantReservationCount(string pSchoolId)
        {
            return ItinerantReservations.CountForSchool(pSchoolId);
        }

        public static Actor SuccessorFor(Actor pTeacher)
        {
            if (pTeacher?.data == null ||
                !SuccessorByTeacher.TryGetValue(pTeacher.data.id, out long successorId))
                return null;
            Actor successor = FindActor(successorId);
            return successor?.data != null && successor.isAlive() && !successor.isRekt()
                ? successor
                : null;
        }

        public static bool IsQualifiedTeacher(Actor pActor)
        {
            if (pActor?.data == null || !pActor.isAlive() || pActor.isRekt()) return false;
            if (HistoricalSchoolDescentService.IsCanonicalMaster(pActor)) return true;
            return WasQualifiedTeacherAtDeath(
                SchoolMembershipService.GetActive(pActor.data.id));
        }

        public static bool WasQualifiedTeacherAtDeath(SchoolMembershipRecord pMembership)
        {
            return pMembership != null && pMembership.Active && pMembership.IsValid &&
                   (pMembership.Standing == HistoricalSchoolStanding.Teacher ||
                    pMembership.Standing == HistoricalSchoolStanding.Leader ||
                    pMembership.Standing == HistoricalSchoolStanding.CanonicalMaster);
        }

        public static int DirectDiscipleCount(long pTeacherActorId)
        {
            return BuildDirectDiscipleCounts().TryGetValue(pTeacherActorId, out int count)
                ? count
                : 0;
        }

        public static Dictionary<long, int> BuildDirectDiscipleCounts()
        {
            var result = new Dictionary<long, int>();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (long actorId in SchoolMembershipService.Members(school.Id))
                {
                    SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actorId);
                    if (membership == null ||
                        (membership.Source != SchoolMembershipSource.DirectDiscipleship &&
                         membership.Source != SchoolMembershipSource.LaterDiscipleship) ||
                        membership.TeacherActorId < 0) continue;
                    result.TryGetValue(membership.TeacherActorId, out int count);
                    result[membership.TeacherActorId] = count + 1;
                }
            return result;
        }

        public static Actor SelectSuccessor(Actor pTeacher)
        {
            if (pTeacher?.data == null) return null;
            var actors = new Dictionary<long, Actor>();
            var candidates = new List<SchoolLineageCandidate>();
            Dictionary<long, int> directCounts = BuildDirectDiscipleCounts();
            foreach (CourtSchoolDefinition school in CourtSchoolRegistry.All)
                foreach (long actorId in SchoolMembershipService.Members(school.Id))
                {
                    SchoolMembershipRecord membership = SchoolMembershipService.GetActive(actorId);
                    if (membership?.TeacherActorId != pTeacher.data.id) continue;
                    Actor actor = World.world?.units?.get(actorId);
                    if (actor?.data == null) continue;
                    actors[actorId] = actor;
                    bool directDisciple =
                        membership.Source == SchoolMembershipSource.DirectDiscipleship ||
                        membership.Source == SchoolMembershipSource.LaterDiscipleship;
                    candidates.Add(new SchoolLineageCandidate(actorId, actor.isAlive() &&
                        !actor.isRekt(),
                        directDisciple,
                        membership.Reputation, SafeLearning(actor), 0,
                        directCounts.TryGetValue(actorId, out int followerCount)
                            ? followerCount
                            : 0));
                }
            SchoolLineageCandidate selected = HistoricalSchoolRules.SelectLineageSuccessor(
                candidates);
            return selected != null && actors.TryGetValue(selected.ActorId, out Actor result)
                ? result
                : null;
        }

        public static void OnTeacherDeath(Actor pTeacher)
        {
            long teacherId = pTeacher?.data?.id ?? -1L;
            if (teacherId < 0 || SuccessorByTeacher.ContainsKey(teacherId) ||
                !ProcessingTeacherDeaths.TryBegin(teacherId)) return;
            bool persistenceQueued = false;
            try
            {
                ReleaseItinerant(pTeacher);
                Actor successor = SelectSuccessor(pTeacher);
                if (successor?.data == null) return;
                SchoolMembershipRecord membership =
                    SchoolMembershipService.GetActive(successor.data.id);
                City residence = HistoricalAffiliationService.ResidenceCity(successor) ??
                                 successor.city;
                persistenceQueued = HistoricalSchoolWriteBufferService.TryEnqueue(
                    new LineageSuccessorWriteOperation(teacherId, successor.data.id,
                        membership?.SchoolId ?? "", residence?.data?.id ?? -1L,
                        residence?.kingdom?.data?.id ??
                        successor.kingdom?.data?.id ?? -1L,
                        Date.getCurrentYear(), successor.data.name ?? "",
                        World.world?.getCurWorldTime() ?? 0d));
            }
            finally
            {
                if (!persistenceQueued) ProcessingTeacherDeaths.Complete(teacherId);
            }
        }

        private sealed class LineageSuccessorWriteOperation :
            IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation
        {
            private readonly long _teacherId;
            private readonly long _successorId;
            private readonly string _schoolId;
            private readonly long _cityId;
            private readonly long _kingdomId;
            private readonly int _year;
            private readonly string _successorName;
            private readonly double _worldTime;

            public LineageSuccessorWriteOperation(long pTeacherId, long pSuccessorId,
                string pSchoolId, long pCityId, long pKingdomId, int pYear,
                string pSuccessorName, double pWorldTime)
            {
                _teacherId = pTeacherId;
                _successorId = pSuccessorId;
                _schoolId = pSchoolId ?? "";
                _cityId = pCityId;
                _kingdomId = pKingdomId;
                _year = pYear;
                _successorName = pSuccessorName ?? "";
                _worldTime = pWorldTime;
                OperationKey = "lineage-successor:v1:teacher:" + _teacherId +
                               ":successor:" + _successorId;
            }

            public string OperationKey { get; }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return DetachBackgroundWrite().Execute(pDb, pTransaction);
            }

            public IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                return new LineageSuccessorBackgroundWrite(OperationKey,
                    _teacherId, _successorId, _schoolId, _cityId,
                    _kingdomId, _year, _successorName, _worldTime);
            }

            public void AfterCommit(HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                try
                {
                    if (pOutcome == HistoricalSchoolTeachingPersistenceOutcome.Committed ||
                        pOutcome == HistoricalSchoolTeachingPersistenceOutcome.Replayed)
                        SuccessorByTeacher[_teacherId] = _successorId;
                }
                finally
                {
                    ProcessingTeacherDeaths.Complete(_teacherId);
                }
            }

            public void OnCleanFailure()
            {
                ProcessingTeacherDeaths.Complete(_teacherId);
            }
        }

        private sealed class LineageSuccessorBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly string _operationKey;
            private readonly long _teacherId;
            private readonly long _successorId;
            private readonly string _schoolId;
            private readonly long _cityId;
            private readonly long _kingdomId;
            private readonly int _year;
            private readonly string _successorName;
            private readonly double _worldTime;

            public LineageSuccessorBackgroundWrite(string pOperationKey,
                long pTeacherId, long pSuccessorId, string pSchoolId,
                long pCityId, long pKingdomId, int pYear,
                string pSuccessorName, double pWorldTime)
            {
                _operationKey = pOperationKey ?? "";
                _teacherId = pTeacherId;
                _successorId = pSuccessorId;
                _schoolId = pSchoolId ?? "";
                _cityId = pCityId;
                _kingdomId = pKingdomId;
                _year = pYear;
                _successorName = pSuccessorName ?? "";
                _worldTime = pWorldTime;
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                return HistoricalSchoolStore.RecordSchoolEventInTransaction(
                    pDb, pTransaction, _operationKey, "lineage_successor",
                    _successorId, _teacherId, _schoolId, _cityId, _kingdomId,
                    _year, _successorName, 3, _worldTime);
            }
        }

        private static float SafeLearning(Actor pActor)
        {
            try { return Math.Max(0f, pActor.stats?["intelligence"] ?? 0f); }
            catch { return 0f; }
        }

        private static Actor FindActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }
    }
}
