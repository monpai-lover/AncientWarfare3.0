using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using life.taxi;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolEducationJourneyService
    {
        public const int MaxLoadRecoveryActorsPerFrame = 16;
        private const long TravelTaskLeaseFrames = 7200L;
        private static readonly Dictionary<long, WorldTile> ActiveTargets =
            new Dictionary<long, WorldTile>();
        private static readonly Dictionary<long, MaritimeJourney>
            MaritimeJourneys = new Dictionary<long, MaritimeJourney>();
        private static List<City> _loadRecoveryCities;
        private static int _loadRecoveryCityIndex;
        private static int _loadRecoveryActorIndex;

        private readonly struct JourneyState
        {
            public JourneyState(long pStudentId, long pTeacherId,
                string pSchoolId, long pDestinationCityId, long pKingdomId,
                int pStartYear, int pRetryCount)
            {
                StudentId = pStudentId;
                TeacherId = pTeacherId;
                SchoolId = pSchoolId ?? "";
                DestinationCityId = pDestinationCityId;
                KingdomId = pKingdomId;
                StartYear = pStartYear;
                RetryCount = pRetryCount;
            }

            public long StudentId { get; }
            public long TeacherId { get; }
            public string SchoolId { get; }
            public long DestinationCityId { get; }
            public long KingdomId { get; }
            public int StartYear { get; }
            public int RetryCount { get; }
            public bool IsValid => StudentId >= 0L && TeacherId >= 0L &&
                                   !string.IsNullOrEmpty(SchoolId) &&
                                   DestinationCityId >= 0L && KingdomId >= 0L &&
                                   StartYear >= 0;
        }

        private sealed class MaritimeJourney
        {
            public Actor Actor;
            public TaxiRequest Request;
            public long DestinationCityId;
            public int TargetTileId;
        }

        public static bool TryBegin(Actor pStudent, Actor pTeacher,
            SchoolMembershipRecord pTeacherMembership, City pResidence,
            City pDestination, long pKingdomId, int pStartYear)
        {
            if (!IsUsable(pStudent) || !IsLivingCity(pResidence) ||
                !IsLivingCity(pDestination) || pTeacherMembership == null ||
                pResidence.data.id == pDestination.data.id) return false;
            if (!HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(
                    pDestination)) return false;
            if (!TryValidateTeacher(pTeacher, pTeacherMembership.SchoolId,
                    pDestination, pKingdomId, out _)) return false;
            if (!IsDomesticStudent(pStudent, pKingdomId) ||
                !HistoricalSchoolEducationRules.RequiresJourney(
                    sameCity: false, sameRealm: true,
                    academyCommoner: false)) return false;

            JourneyState current = Read(pStudent);
            if (current.IsValid &&
                (current.TeacherId != pTeacher.data.id ||
                 current.SchoolId != pTeacherMembership.SchoolId ||
                 current.DestinationCityId != pDestination.data.id ||
                 current.KingdomId != pKingdomId))
                ClearState(pStudent);

            JourneyState desired = new JourneyState(pStudent.data.id,
                pTeacher.data.id, pTeacherMembership.SchoolId,
                pDestination.data.id, pKingdomId,
                current.IsValid ? current.StartYear : pStartYear,
                current.IsValid ? current.RetryCount : 0);
            Write(pStudent, desired);
            if (EnsureTravelTask(pStudent, desired)) return true;
            IncrementRetry(pStudent, desired);
            return false;
        }

        public static void BeginLoadRecovery()
        {
            _loadRecoveryCities = new List<City>();
            _loadRecoveryCityIndex = 0;
            _loadRecoveryActorIndex = 0;
            try
            {
                if (World.world?.cities == null) return;
                foreach (City city in World.world.cities)
                    if (IsLivingCity(city)) _loadRecoveryCities.Add(city);
                _loadRecoveryCities.Sort((left, right) =>
                    left.data.id.CompareTo(right.data.id));
            }
            catch
            {
                _loadRecoveryCities.Clear();
            }
        }

        public static bool ProcessLoadRecoveryFrame()
        {
            if (_loadRecoveryCities == null) return false;
            int processed = 0;
            int budget = HistoricalSchoolEducationRules.
                LoadRecoveryBatchCount(MaxLoadRecoveryActorsPerFrame);
            while (_loadRecoveryCityIndex < _loadRecoveryCities.Count &&
                   processed < budget)
            {
                City city = _loadRecoveryCities[_loadRecoveryCityIndex];
                int actorCount = IsLivingCity(city)
                    ? city.units?.Count ?? 0
                    : 0;
                if (_loadRecoveryActorIndex >= actorCount)
                {
                    _loadRecoveryCityIndex++;
                    _loadRecoveryActorIndex = 0;
                    processed++;
                    continue;
                }
                Actor actor = null;
                int actorIndex = _loadRecoveryActorIndex++;
                try { actor = city.units[actorIndex]; }
                catch { }
                processed++;
                if (Read(actor).IsValid) TryResumePending(actor);
            }
            if (_loadRecoveryCityIndex < _loadRecoveryCities.Count)
                return processed > 0;
            _loadRecoveryCities = null;
            _loadRecoveryCityIndex = 0;
            _loadRecoveryActorIndex = 0;
            return processed > 0;
        }

        public static bool TryResumePending(Actor pStudent)
        {
            JourneyState state = Read(pStudent);
            if (!state.IsValid) return false;
            if (!TryValidateState(pStudent, state, out _, out _, out _))
            {
                ClearState(pStudent);
                return false;
            }
            if (EnsureTravelTask(pStudent, state)) return true;
            IncrementRetry(pStudent, state);
            return false;
        }

        public static bool TryPreparePhysicalTravel(Actor pActor,
            out WorldTile pTarget)
        {
            pTarget = null;
            JourneyState state = Read(pActor);
            if (!TryValidateState(pActor, state, out _, out City destination,
                    out _))
            {
                ClearState(pActor);
                return false;
            }
            if (!HistoricalSchoolTaskLeaseService.IsCurrent(pActor.data.id,
                    ActivityId(pActor.data.id),
                    HistoricalSchoolContent.EducationTravelTaskId))
                return false;
            if (!ActiveTargets.TryGetValue(pActor.data.id, out pTarget) ||
                pTarget?.zone?.city != destination)
            {
                IncrementRetryAndRelease(pActor, state);
                pTarget = null;
                return false;
            }
            if (TryBeginMaritimeTravel(pActor, state, destination, pTarget))
            {
                pTarget = null;
                return false;
            }
            return true;
        }

        public static bool TryCompletePhysicalArrival(Actor pActor)
        {
            JourneyState state = Read(pActor);
            if (!TryValidateState(pActor, state, out Actor teacher,
                    out City destination,
                    out SchoolMembershipRecord teacherMembership))
            {
                ClearState(pActor);
                return false;
            }
            ActiveTargets.TryGetValue(pActor.data.id, out WorldTile target);
            bool arrived = target?.data != null &&
                           pActor.current_tile?.data != null &&
                           (pActor.current_tile.zone?.city == destination ||
                            Toolbox.SquaredDistTile(pActor.current_tile,
                                target) <= 4);
            bool canCommit = HistoricalSchoolEducationRules.
                CanCommitAdmission(sameCity: false,
                    arrivedAtDestination: arrived,
                    teacherValid: teacherMembership != null);
            if (!canCommit)
            {
                IncrementRetryAndRelease(pActor, state);
                return false;
            }

            ReleaseTravelTask(pActor.data.id);
            CancelMaritimeJourney(pActor.data.id, pActor);
            bool queued = HistoricalSchoolEliteEnrollmentService.
                TryQueueAdmission(pActor, teacher, teacherMembership,
                    destination, state.KingdomId, state.StartYear,
                    success => OnAdmissionCompleted(pActor, state, success));
            if (queued) return true;
            IncrementRetry(pActor, state);
            return false;
        }

        public static bool TryResumeAfterDisembark(Actor pActor)
        {
            if (pActor?.data == null ||
                !MaritimeJourneys.TryGetValue(pActor.data.id,
                    out MaritimeJourney maritime)) return false;
            JourneyState state = Read(pActor);
            City destination = FindCity(maritime.DestinationCityId);
            WorldTile target = FindTile(maritime.TargetTileId);
            bool valid = TryValidateState(pActor, state, out _, out _, out _);
            bool reachedIsland = false;
            try
            {
                reachedIsland = valid && target?.data != null &&
                                pActor.current_tile?.isSameIsland(target) == true;
            }
            catch { }
            MaritimeJourneys.Remove(pActor.data.id);
            if (!valid)
            {
                ClearState(pActor);
                return false;
            }
            if (!reachedIsland || destination?.data == null)
            {
                IncrementRetryAndRelease(pActor, state);
                return false;
            }
            if (EnsureTravelTask(pActor, state)) return true;
            IncrementRetryAndRelease(pActor, state);
            return false;
        }

        public static void CancelExpiredLease(
            HistoricalSchoolTaskLease pLease)
        {
            if (!string.Equals(pLease.TaskId,
                    HistoricalSchoolContent.EducationTravelTaskId,
                    StringComparison.Ordinal)) return;
            Actor actor = FindActor(pLease.ActorId);
            JourneyState state = Read(actor);
            bool ownsTaxi = MaritimeJourneys.ContainsKey(pLease.ActorId);
            bool journeyValid = TryValidateState(actor, state,
                out _, out _, out _);
            if (HistoricalSchoolEducationRules.ShouldRenewVoyageLease(
                    actorInsideBoat: actor?.is_inside_boat == true,
                    ownsTaxiJourney: ownsTaxi,
                    journeyValid))
            {
                long frame = HistoricalSchoolActivityQueue.CurrentFrame;
                if (HistoricalSchoolTaskLeaseService.TryHold(pLease,
                        frame, frame + TravelTaskLeaseFrames)) return;
            }
            ActiveTargets.Remove(pLease.ActorId);
            CancelMaritimeJourney(pLease.ActorId, actor);
            if (!IsUsable(actor) || !state.IsValid) return;
            if (actor.isTask(HistoricalSchoolContent.EducationTravelTaskId))
                actor.cancelAllBeh();
            IncrementRetry(actor, state);
        }

        public static void OnCommittedDeath(Actor pActor)
        {
            if (pActor?.data == null) return;
            CancelMaritimeJourney(pActor.data.id, pActor);
            ReleaseTravelTask(pActor.data.id);
            ClearPersistedState(pActor);
        }

        public static void ClearRuntime()
        {
            var journeys = new List<MaritimeJourney>(
                MaritimeJourneys.Values);
            MaritimeJourneys.Clear();
            ActiveTargets.Clear();
            _loadRecoveryCities = null;
            _loadRecoveryCityIndex = 0;
            _loadRecoveryActorIndex = 0;
            for (int i = 0; i < journeys.Count; i++)
            {
                TaxiRequest request = journeys[i]?.Request;
                if (request == null) continue;
                try
                {
                    ArmyRtsTransportProductionService.Cancel(request);
                    if (TaxiManager.list.Contains(request))
                        TaxiManager.cancelRequest(request);
                }
                catch { }
            }
        }

        private static bool EnsureTravelTask(Actor pActor,
            JourneyState pState)
        {
            if (!TryValidateState(pActor, pState, out _,
                    out City destination, out _)) return false;
            string activityId = ActivityId(pActor.data.id);
            if (HistoricalSchoolTaskLeaseService.TryGet(pActor.data.id,
                    out HistoricalSchoolTaskLease existing))
            {
                bool current = existing.ActivityId == activityId &&
                               existing.TaskId == HistoricalSchoolContent.
                                   EducationTravelTaskId &&
                               existing.CityId == destination.data.id;
                if (!current) return false;
                if (ActiveTargets.TryGetValue(pActor.data.id,
                        out WorldTile existingTarget) &&
                    existingTarget?.zone?.city == destination &&
                    pActor.isTask(HistoricalSchoolContent.
                        EducationTravelTaskId)) return true;
                HistoricalSchoolTaskLeaseService.ReleaseExact(
                    pActor.data.id, activityId);
            }
            WorldTile target = DestinationTile(destination, pActor,
                pState.SchoolId);
            if (target?.data == null) return false;
            long frame = HistoricalSchoolActivityQueue.CurrentFrame;
            if (!HistoricalSchoolTaskLeaseService.TrySchedule(pActor,
                    activityId,
                    HistoricalSchoolContent.EducationTravelTaskId,
                    pState.SchoolId, destination.data.id, activityId,
                    target, frame, frame + TravelTaskLeaseFrames))
                return false;
            ActiveTargets[pActor.data.id] = target;
            return true;
        }

        private static bool TryBeginMaritimeTravel(Actor pActor,
            JourneyState pState, City pDestination, WorldTile pTarget)
        {
            if (pActor.current_tile?.data == null || pTarget?.data == null ||
                pActor.is_inside_boat) return false;
            bool sameIsland;
            try { sameIsland = pActor.current_tile.isSameIsland(pTarget); }
            catch { return false; }
            if (sameIsland) return false;

            if (MaritimeJourneys.TryGetValue(pActor.data.id,
                    out MaritimeJourney existing))
            {
                if (existing.DestinationCityId == pDestination.data.id &&
                    existing.TargetTileId == pTarget.data.tile_id)
                {
                    Kingdom existingKingdom = FindKingdom(pState.KingdomId);
                    ArmyRtsTransportProductionService.Request(existingKingdom,
                        existing.Request);
                    return true;
                }
                CancelMaritimeJourney(pActor.data.id, pActor);
            }

            Kingdom kingdom = FindKingdom(pState.KingdomId);
            if (kingdom?.data == null) return false;
            ReleaseUnownedTaxiRequest(pActor);
            try
            {
                var request = new TaxiRequest(pActor, kingdom,
                    pActor.current_tile, pTarget);
                TaxiManager.list.Add(request);
                ArmyRtsTransportProductionService.Request(kingdom, request);
                MaritimeJourneys[pActor.data.id] = new MaritimeJourney
                {
                    Actor = pActor,
                    Request = request,
                    DestinationCityId = pDestination.data.id,
                    TargetTileId = pTarget.data.tile_id
                };
                pActor.setNotMoving();
                return true;
            }
            catch
            {
                MaritimeJourneys.Remove(pActor.data.id);
                return false;
            }
        }

        private static bool TryValidateState(Actor pStudent,
            JourneyState pState, out Actor pTeacher, out City pDestination,
            out SchoolMembershipRecord pTeacherMembership)
        {
            pTeacher = null;
            pDestination = null;
            pTeacherMembership = null;
            if (!pState.IsValid || !IsUsable(pStudent) ||
                pState.StudentId != pStudent.data.id ||
                !IsDomesticStudent(pStudent, pState.KingdomId) ||
                SchoolMembershipService.GetActive(pStudent.data.id) != null)
                return false;
            pTeacher = FindActor(pState.TeacherId);
            pDestination = FindCity(pState.DestinationCityId);
            return TryValidateTeacher(pTeacher, pState.SchoolId,
                pDestination, pState.KingdomId, out pTeacherMembership);
        }

        private static bool TryValidateTeacher(Actor pTeacher,
            string pSchoolId, City pDestination, long pKingdomId,
            out SchoolMembershipRecord pMembership)
        {
            pMembership = pTeacher?.data == null ? null :
                SchoolMembershipService.GetActive(pTeacher.data.id);
            if (!IsUsable(pTeacher) || !IsLivingCity(pDestination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(
                    pDestination) ||
                pMembership == null || pMembership.SchoolId != pSchoolId ||
                !SchoolLineageService.IsQualifiedTeacher(pTeacher) ||
                !HistoricalAffiliationService.IsPresentForInfluence(pTeacher) ||
                !HistoricalAffiliationService.IsAvailableForOffice(pTeacher) ||
                HistoricalSchoolRuntimeIndex.Instance.DirectDiscipleCount(
                    pTeacher.data.id) >= SchoolLineageService.DirectDiscipleCap)
                return false;
            City teacherResidence = HistoricalAffiliationService.
                ResidenceCity(pTeacher) ?? pTeacher.city;
            return teacherResidence?.data?.id == pDestination.data.id &&
                   teacherResidence.kingdom?.data?.id == pKingdomId;
        }

        private static bool IsDomesticStudent(Actor pStudent,
            long pKingdomId)
        {
            Kingdom kingdom = FindKingdom(pKingdomId);
            return kingdom?.data != null && !kingdom.isRekt() &&
                   CourtAffiliationResolver.IsDomestic(pStudent, kingdom);
        }

        private static void OnAdmissionCompleted(Actor pActor,
            JourneyState pState, bool pSuccess)
        {
            if (pActor?.data == null) return;
            if (pSuccess)
            {
                ClearState(pActor);
                return;
            }
            JourneyState current = Read(pActor);
            if (current.IsValid && current.TeacherId == pState.TeacherId &&
                current.SchoolId == pState.SchoolId)
                IncrementRetry(pActor, current);
        }

        private static void IncrementRetryAndRelease(Actor pActor,
            JourneyState pState)
        {
            if (pActor?.data == null) return;
            CancelMaritimeJourney(pActor.data.id, pActor);
            ReleaseTravelTask(pActor.data.id);
            IncrementRetry(pActor, pState);
        }

        private static void IncrementRetry(Actor pActor,
            JourneyState pState)
        {
            if (pActor?.data == null || !pState.IsValid) return;
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_RETRY_COUNT,
                Math.Max(0, pState.RetryCount) + 1);
        }

        private static JourneyState Read(Actor pActor)
        {
            if (pActor?.data == null) return default;
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_STUDENT_ID,
                out long studentId, -1L);
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_TEACHER_ID,
                out long teacherId, -1L);
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_SCHOOL_ID,
                out string schoolId, "");
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_DESTINATION_CITY_ID,
                out long destinationCityId, -1L);
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_KINGDOM_ID,
                out long kingdomId, -1L);
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_START_YEAR,
                out int startYear, -1);
            pActor.data.get(LineageKeys.SCHOOL_EDUCATION_RETRY_COUNT,
                out int retryCount, 0);
            return new JourneyState(studentId, teacherId, schoolId,
                destinationCityId, kingdomId, startYear, retryCount);
        }

        private static void Write(Actor pActor, JourneyState pState)
        {
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_STUDENT_ID,
                pState.StudentId);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_TEACHER_ID,
                pState.TeacherId);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_SCHOOL_ID,
                pState.SchoolId);
            pActor.data.set(
                LineageKeys.SCHOOL_EDUCATION_DESTINATION_CITY_ID,
                pState.DestinationCityId);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_KINGDOM_ID,
                pState.KingdomId);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_START_YEAR,
                pState.StartYear);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_RETRY_COUNT,
                pState.RetryCount);
        }

        private static void ClearState(Actor pActor)
        {
            if (pActor?.data == null) return;
            CancelMaritimeJourney(pActor.data.id, pActor);
            ReleaseTravelTask(pActor.data.id);
            ClearPersistedState(pActor);
        }

        private static void ClearPersistedState(Actor pActor)
        {
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_STUDENT_ID, -1L);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_TEACHER_ID, -1L);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_SCHOOL_ID, "");
            pActor.data.set(
                LineageKeys.SCHOOL_EDUCATION_DESTINATION_CITY_ID, -1L);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_START_YEAR, -1);
            pActor.data.set(LineageKeys.SCHOOL_EDUCATION_RETRY_COUNT, 0);
        }

        private static WorldTile DestinationTile(City pCity, Actor pActor,
            string pSchoolId)
        {
            return HistoricalSchoolVenueProvider.TryFind(pCity, pActor,
                pSchoolId, HistoricalSchoolVenueKind.TravelArrival,
                out WorldTile target, out _, out _) ? target : null;
        }

        private static void ReleaseTravelTask(long pActorId)
        {
            ActiveTargets.Remove(pActorId);
            HistoricalSchoolTaskLeaseService.ReleaseExact(pActorId,
                ActivityId(pActorId));
        }

        private static void CancelMaritimeJourney(long pActorId,
            Actor pActor)
        {
            if (!MaritimeJourneys.TryGetValue(pActorId,
                    out MaritimeJourney journey)) return;
            MaritimeJourneys.Remove(pActorId);
            TaxiRequest request = journey.Request;
            if (request == null) return;
            try
            {
                Actor actor = pActor ?? journey.Actor;
                if (actor?.data != null && request.hasActor(actor))
                    request.embarkToBoat(actor);
                if (request.countActors() == 0 &&
                    TaxiManager.list.Contains(request))
                {
                    ArmyRtsTransportProductionService.Cancel(request);
                    TaxiManager.cancelRequest(request);
                }
            }
            catch { }
        }

        private static void ReleaseUnownedTaxiRequest(Actor pActor)
        {
            TaxiRequest request;
            try { request = TaxiManager.getRequestForActor(pActor); }
            catch { return; }
            if (request == null) return;
            try
            {
                request.embarkToBoat(pActor);
                if (request.countActors() == 0)
                {
                    ArmyRtsTransportProductionService.Cancel(request);
                    TaxiManager.cancelRequest(request);
                }
            }
            catch { }
        }

        private static string ActivityId(long pActorId)
        {
            return "education_travel:" + pActorId;
        }

        private static bool IsUsable(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() &&
                   !pActor.isRekt() && !pActor.isBaby();
        }

        private static bool IsLivingCity(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom?.data != null && !pCity.kingdom.isRekt();
        }

        private static Actor FindActor(long pId)
        {
            try { return pId >= 0L ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return pId >= 0L ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static Kingdom FindKingdom(long pId)
        {
            try { return pId >= 0L ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static WorldTile FindTile(int pId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pId >= 0 && pId < tiles.Length
                ? tiles[pId]
                : null;
        }
    }
}
