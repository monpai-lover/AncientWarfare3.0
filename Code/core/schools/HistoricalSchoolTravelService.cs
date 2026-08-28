using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;
using life.taxi;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTravelService
    {
        public const int MaxDestinationCandidates = 16;
        private const int MaxIndexedCities = 64;
        private const int MinResidenceYears = 8;
        private const int MaxPendingArrivalRetries = 512;
        private const long TravelTaskLeaseFrames = 3600L;
        private static readonly int[] BucketOffsets = new int[4];
        private static readonly Dictionary<long, WorldTile> ActiveTravelTargets =
            new Dictionary<long, WorldTile>();
        private static readonly Dictionary<long, MaritimeTravelState>
            MaritimeTravelStates =
                new Dictionary<long, MaritimeTravelState>();
        private static readonly HashSet<long> PendingArrivalActorIds =
            new HashSet<long>();
        private static readonly HistoricalSchoolJourneyArrivalRetryQueue<
            JourneyArrivalWriteOperation> PendingArrivalRetries =
            new HistoricalSchoolJourneyArrivalRetryQueue<
                JourneyArrivalWriteOperation>(MaxPendingArrivalRetries);
        private static QuarterWork _pendingQuarter;
        private static IReadOnlyList<City> _indexedCities;
        private static int _indexedCitiesYear = -1;

        private sealed class QuarterWork
        {
            public int Bucket;
            public int Start;
            public int Count;
            public int Processed;
            public int Year;
            public long[] ActorIds;
        }

        private sealed class MaritimeTravelState
        {
            public Actor Actor;
            public TaxiRequest Request;
            public long DestinationCityId;
            public int TargetTileId;
        }

        public static void ClearRuntime()
        {
            ClearMaritimeTravels();
            Array.Clear(BucketOffsets, 0, BucketOffsets.Length);
            ActiveTravelTargets.Clear();
            PendingArrivalActorIds.Clear();
            PendingArrivalRetries.Clear();
            HistoricalSchoolJourneyArrivalRevision.Clear();
            _pendingQuarter = null;
            _indexedCities = null;
            _indexedCitiesYear = -1;
        }

        public static void ProcessQuarter(int pQuarterKey)
        {
            int year = Date.getCurrentYear();
            int bucket = ((pQuarterKey % 4) + 4) % 4;
            long[] actorIds = HistoricalSchoolRuntimeIndex.Instance.TravelEligibleIds(bucket);
            if (actorIds.Length == 0)
            {
                _pendingQuarter = null;
                return;
            }
            int start = PositiveModulo(BucketOffsets[bucket], actorIds.Length);
            _pendingQuarter = new QuarterWork
            {
                Bucket = bucket,
                Start = start,
                Count = HistoricalSchoolSchedulerRules.QuarterlyTravelWorkCount(
                    actorIds.Length),
                Year = year,
                ActorIds = actorIds
            };
        }

        public static bool ProcessFrame()
        {
            if (ProcessPendingArrivalRetry()) return true;
            QuarterWork work = _pendingQuarter;
            if (work?.ActorIds == null || work.Processed >= work.Count)
            {
                _pendingQuarter = null;
                return false;
            }

            int offset = work.Processed++;
            BucketOffsets[work.Bucket] = (work.Start + work.Processed) %
                                         work.ActorIds.Length;
            if (work.Processed >= work.Count) _pendingQuarter = null;
            try
            {
                long actorId = work.ActorIds[
                    (work.Start + offset) % work.ActorIds.Length];
                HistoricalSchoolAffiliationSnapshot state =
                    HistoricalAffiliationService.Get(actorId);
                if (state == null || state.LifecycleState ==
                    HistoricalSchoolLifecycleState.Dead) return true;
                if (state.LifecycleState == HistoricalSchoolLifecycleState.Voyage)
                {
                    CompleteDueVoyage(state, work.Year);
                    return true;
                }
                DestinationPreparation prepared = PrepareDestination(state,
                    work.Year);
                if (prepared == null) return true;
                TryChooseDestination(prepared.Actor, prepared.State,
                    IndexedCities(work.Year), work.Year);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school travel frame failed: " +
                                    error.Message);
                return true;
            }
        }

        public static void InvalidateCityIndex()
        {
            HistoricalSchoolJourneyArrivalRevision.MarkDestinationsChanged();
            _indexedCities = null;
            _indexedCitiesYear = -1;
        }

        public static bool TryPreparePhysicalTravel(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor) || state == null || IsServingOrBound(pActor, state) ||
                state.LifecycleState != HistoricalSchoolLifecycleState.Travelling ||
                !HistoricalSchoolTaskLeaseService.IsCurrent(pActor.data.id,
                    TravelActivityId(pActor.data.id), HistoricalSchoolContent.TravelTaskId))
                return false;
            City destination = FindCity(state.DestinationCityId);
            if (!IsLivingCity(destination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(destination))
            {
                CancelMaritimeTravel(pActor.data.id, pActor);
                HistoricalAffiliationService.CancelTravel(pActor);
                SchoolLineageService.ReleaseItinerant(pActor);
                ReleaseTravelTask(pActor.data.id);
                return false;
            }
            if (!ActiveTravelTargets.TryGetValue(pActor.data.id, out pTarget) ||
                pTarget?.zone?.city != destination)
            {
                ReportImmediatePathFailure(pActor);
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
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor) || state == null ||
                state.LifecycleState != HistoricalSchoolLifecycleState.Travelling) return false;
            City destination = FindCity(state.DestinationCityId);
            City previousResidence = FindCity(state.ResidenceCityId);
            if (!IsLivingCity(destination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(destination))
            {
                CancelMaritimeTravel(pActor.data.id, pActor);
                HistoricalAffiliationService.CancelTravel(pActor);
                ReleaseTravelTask(pActor.data.id);
                SchoolLineageService.ReleaseItinerant(pActor);
                return false;
            }
            ActiveTravelTargets.TryGetValue(pActor?.data?.id ?? -1L, out WorldTile tile);
            if (tile == null || pActor.current_tile == null ||
                Toolbox.SquaredDistTile(pActor.current_tile, tile) > 4)
            {
                HistoricalAffiliationService.RegisterTransportFailure(pActor,
                    Date.getCurrentYear());
                ReleaseTravelTask(pActor.data.id);
                return false;
            }
            int year = Date.getCurrentYear();
            if (TryQueueArrival(pActor, previousResidence, destination,
                    state, year)) return true;
            ReleaseTravelTask(pActor.data.id);
            SchoolLineageService.ReleaseItinerant(pActor);
            HistoricalAffiliationService.RegisterTransportFailure(pActor,
                year);
            return false;
        }

        public static void OnCommittedDeath(Actor pActor)
        {
            if (pActor?.data == null) return;
            CancelMaritimeTravel(pActor.data.id, pActor);
            try { pActor.finishStatusEffect(HistoricalSchoolContent.VoyageStatusId); }
            catch { }
            HistoricalSchoolTaskLeaseService.ReleaseActor(pActor.data.id);
            ActiveTravelTargets.Remove(pActor.data.id);
            PendingArrivalActorIds.Remove(pActor.data.id);
            PendingArrivalRetries.Remove(pActor.data.id);
            SchoolLineageService.ReleaseItinerant(pActor);
        }

        public static void ReportImmediatePathFailure(Actor pActor)
        {
            if (pActor?.data == null ||
                MaritimeTravelStates.ContainsKey(pActor.data.id) ||
                !pActor.isTask(HistoricalSchoolContent.TravelTaskId) ||
                !HistoricalSchoolTaskLeaseService.TryGet(pActor.data.id,
                    out HistoricalSchoolTaskLease lease) ||
                lease.TaskId != HistoricalSchoolContent.TravelTaskId)
                return;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor.data.id);
            if (state?.LifecycleState == HistoricalSchoolLifecycleState.Travelling)
            {
                ReleaseTravelTask(pActor.data.id);
                HistoricalAffiliationService.RegisterTransportFailure(pActor,
                    Date.getCurrentYear());
            }
        }

        public static void CancelExpiredLease(HistoricalSchoolTaskLease pLease)
        {
            if (!string.Equals(pLease.TaskId, HistoricalSchoolContent.TravelTaskId,
                    StringComparison.Ordinal)) return;
            Actor actor = FindActor(pLease.ActorId);
            if (actor?.is_inside_boat == true &&
                MaritimeTravelStates.ContainsKey(pLease.ActorId))
                return;
            ActiveTravelTargets.Remove(pLease.ActorId);
            CancelMaritimeTravel(pLease.ActorId, actor);
            if (!IsUsable(actor)) return;
            if (actor.isTask(HistoricalSchoolContent.TravelTaskId)) actor.cancelAllBeh();
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pLease.ActorId);
            if (state?.LifecycleState == HistoricalSchoolLifecycleState.Travelling)
                HistoricalAffiliationService.RegisterTransportFailure(actor,
                    Date.getCurrentYear());
        }

        public static bool TryInviteToCity(Actor pActor, City pDestination,
            int pYear)
        {
            if (!IsUsable(pActor) || !IsLivingCity(pDestination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(pDestination))
                return false;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor.data.id);
            if (state == null || state.ServiceKingdomId >= 0L ||
                (state.LifecycleState !=
                     HistoricalSchoolLifecycleState.AtHome &&
                 state.LifecycleState !=
                     HistoricalSchoolLifecycleState.Resident) ||
                state.ResidenceCityId == pDestination.data.id)
                return false;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            if (!SchoolLineageService.TryReserveExamTraveler(pActor, school))
                return false;
            if (!HistoricalAffiliationService.TryBeginTravel(pActor,
                    pDestination.data.id, pYear))
            {
                SchoolLineageService.ReleaseItinerant(pActor);
                return false;
            }
            pActor.finishStatusEffect(HistoricalSchoolContent.GuestStatusId);
            if (EnsureTravelTask(pActor)) return true;

            HistoricalAffiliationService.CancelTravel(pActor);
            ReleaseTravelTask(pActor.data.id);
            SchoolLineageService.ReleaseItinerant(pActor);
            return false;
        }

        private static void TryChooseDestination(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState, IReadOnlyList<City> pCities, int pYear)
        {
            City residence = FindCity(pState.ResidenceCityId) ?? pActor.city;
            WorldTile origin = residence?.getTile() ?? pActor.current_tile;
            if (origin == null) return;
            string school = SchoolMembershipService.GetSchool(pActor.data.id);
            HistoricalSchoolMasterDefinition master =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            int probeCount = HistoricalSchoolSchedulerRules.
                DestinationTileProbeCount(pCities.Count);
            IEnumerable<City> probeCities = pCities
                .Where(city => IsLivingCity(city) &&
                    HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(city) &&
                    city.data.id != pState.ResidenceCityId)
                .OrderBy(city => HistoricalSchoolRules.StableTravelCandidateOrder(
                    pActor.data.id, city.data.id))
                .Take(probeCount);
            var cheapCandidates = new List<TravelCityTarget>(probeCount);
            foreach (City city in probeCities)
            {
                WorldTile target = DestinationTile(city, pActor, school);
                if (target == null) continue;
                cheapCandidates.Add(new TravelCityTarget(city, target));
            }
            HistoricalSchoolTravelCandidate[] candidates =
                HistoricalSchoolRules.BuildStableTravelCandidateWindow(
                    pActor.data.id, cheapCandidates, candidate => candidate.City.data.id,
                    candidate => BuildTravelCandidate(candidate, school, master,
                        residence, origin), MaxDestinationCandidates);
            var context = new HistoricalSchoolTravelContext(pActor.data.id,
                pState.ResidenceCityId, pState.PreviousResidenceCityId,
                pState.LastTravelYear, pYear, pState.ServiceKingdomId >= 0);
            HistoricalSchoolTravelCandidate selected =
                HistoricalSchoolRules.SelectTravelDestination(context, candidates,
                    MaxDestinationCandidates);
            if (selected == null || !SchoolLineageService.TryReserveItinerant(pActor, school))
                return;
            if (!HistoricalAffiliationService.TryBeginTravel(pActor, selected.CityId, pYear))
            {
                SchoolLineageService.ReleaseItinerant(pActor);
                return;
            }
            pActor.finishStatusEffect(HistoricalSchoolContent.GuestStatusId);
            EnsureTravelTask(pActor);
        }

        private static DestinationPreparation PrepareDestination(
            HistoricalSchoolAffiliationSnapshot pState, int pYear)
        {
            Actor actor = FindActor(pState.ActorId);
            if (!IsUsable(actor) || IsServingOrBound(actor, pState) ||
                pState.LifecycleState == HistoricalSchoolLifecycleState.Dead) return null;
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Voyage) return null;
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.ChoosingDestination)
            {
                HistoricalAffiliationService.RepairEnginePointers(actor);
                if (HistoricalAffiliationService.TryStartChosenTravel(actor))
                    EnsureTravelTask(actor);
                return null;
            }
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Travelling)
            {
                HistoricalAffiliationService.RepairEnginePointers(actor);
                if (!TryStartTimedVoyage(actor, pState, pYear)) EnsureTravelTask(actor);
                return null;
            }
            if (pState.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                pState.LifecycleState != HistoricalSchoolLifecycleState.Resident) return null;
            if (pState.LastTravelYear >= 0 && pYear - pState.LastTravelYear < MinResidenceYears)
                return null;
            HistoricalAffiliationService.RepairEnginePointers(actor);
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Resident)
                actor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                    pColorEffect: false);
            City residence = FindCity(pState.ResidenceCityId) ?? actor.city;
            WorldTile origin = residence?.getTile() ?? actor.current_tile;
            if (origin == null) return null;
            return new DestinationPreparation(actor, pState);
        }

        private static HistoricalSchoolTravelCandidate BuildTravelCandidate(
            TravelCityTarget pCandidate, string pSchool,
            HistoricalSchoolMasterDefinition pMaster, City pResidence, WorldTile pOrigin)
        {
            City city = pCandidate.City;
            CitySchoolSnapshot snapshot = CitySchoolSnapshotService.GetSnapshot(city);
            float underrepresented = 1f - (snapshot?.Share(pSchool) ?? 0f);
            int rivals = snapshot?.Contributors
                .Select(p => p.SchoolId)
                .Where(p => !string.Equals(p, pSchool, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal).Count() ?? 0;
            int population = SafePopulation(city);
            bool atWar = SafeAtWar(city.kingdom);
            bool occupied = ForeignOccupationService.GetResentment(city) > 0f;
            bool disaster = HasActiveDisaster(city, population);
            return new HistoricalSchoolTravelCandidate(city.data.id,
                city.kingdom.id, population, SafeDevelopment(city),
                city.kingdom.capital == city, underrepresented, rivals,
                Math.Min(20, population / 10), ReceptiveRuler(city.kingdom),
                pOpenOffice: HasOpenCentralOffice(city.kingdom),
                pProblemMatch: ProblemMatch(pMaster, city, population, atWar),
                pTransportAvailable: TransportAvailable(pResidence, city),
                pAtWar: atWar, pOccupied: occupied, pDisaster: disaster,
                Toolbox.SquaredDistTile(pOrigin, pCandidate.Target));
        }

        private static bool TryStartTimedVoyage(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState, int pYear)
        {
            if (pActor?.is_inside_boat == true ||
                (pActor?.data != null &&
                 MaritimeTravelStates.ContainsKey(pActor.data.id)))
                return false;
            int waitingYears = pState.TravelWaitStartYear < 0
                ? 0
                : Math.Max(0, pYear - pState.TravelWaitStartYear);
            if (!HistoricalSchoolRules.CanStartTimedVoyage(
                    SchoolLineageService.IsQualifiedTeacher(pActor),
                    pState.TransportFailures, waitingYears,
                    IsServingOrBound(pActor, pState)))
                return false;
            City destination = FindCity(pState.DestinationCityId);
            City residence = FindCity(pState.ResidenceCityId) ?? pActor.city;
            if (!IsLivingCity(destination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(destination) ||
                residence?.getTile() == null) return false;
            int distance = Toolbox.SquaredDistTile(residence.getTile(), destination.getTile());
            int arrival = HistoricalSchoolRules.VoyageArrivalYear(pYear, distance);
            if (!HistoricalAffiliationService.TryBeginVoyage(pActor, pYear, arrival))
                return false;
            CancelMaritimeTravel(pActor.data.id, pActor);
            ReleaseTravelTask(pActor.data.id);
            pActor.cancelAllBeh();
            Building dock = FindDock(residence);
            if (dock != null) pActor.stayInBuilding(dock);
            RefreshVoyageIsolation(pActor);
            return true;
        }

        private static void CompleteDueVoyage(
            HistoricalSchoolAffiliationSnapshot pState, int pYear)
        {
            if (pState?.LifecycleState != HistoricalSchoolLifecycleState.Voyage) return;
            Actor actor = FindActor(pState.ActorId);
            City destination = FindCity(pState.DestinationCityId);
            City previousResidence = FindCity(pState.ResidenceCityId);
            if (!IsUsable(actor)) return;
            RefreshVoyageIsolation(actor);
            if (!IsLivingCity(destination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(destination))
            {
                RestoreCancelledVoyage(actor, pState);
                return;
            }
            if (pState.VoyageArrivalYear < 0 || pYear < pState.VoyageArrivalYear) return;
            Building dock = FindDock(destination);
            string schoolId = SchoolMembershipService.GetSchool(actor.data.id);
            WorldTile tile = dock?.current_tile ?? DestinationTile(destination, actor, schoolId);
            if (tile == null) return;
            actor.exitBuilding();
            actor.is_visible = true;
            actor.spawnOn(tile);
            if (!TryQueueArrival(actor, previousResidence, destination,
                    pState, pYear))
                RestoreCancelledVoyage(actor, pState);
        }

        private static void RefreshVoyageIsolation(Actor pActor)
        {
            if (!IsUsable(pActor)) return;
            pActor.is_visible = false;
            pActor.setNotMoving();
            pActor.addStatusEffect(HistoricalSchoolContent.VoyageStatusId, 120f,
                pColorEffect: false);
        }

        private static void RestoreCancelledVoyage(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState)
        {
            City residence = FindCity(pState.ResidenceCityId);
            WorldTile tile = residence?.getTile() ?? pActor.current_tile;
            pActor.exitBuilding();
            pActor.is_visible = true;
            if (tile != null) pActor.spawnOn(tile);
            HistoricalAffiliationService.CancelTravel(pActor);
            ReleaseTravelTask(pActor.data.id);
            SchoolLineageService.ReleaseItinerant(pActor);
            pActor.finishStatusEffect(HistoricalSchoolContent.VoyageStatusId);
        }

        private static bool TryQueueArrival(Actor pActor,
            City pPreviousResidence, City pDestination,
            HistoricalSchoolAffiliationSnapshot pState, int pYear)
        {
            if (!IsUsable(pActor) || !IsLivingCity(pDestination) ||
                !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(
                    pDestination) ||
                pState == null) return false;
            if (PendingArrivalActorIds.Contains(pActor.data.id)) return true;
            if (!HistoricalAffiliationService.TryPrepareArrival(pActor,
                    pDestination.data.id, pYear,
                    out HistoricalSchoolAffiliationSnapshot previous,
                    out HistoricalSchoolAffiliationSnapshot desired))
                return false;

            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            string actorName = definition?.CanonicalName ??
                               pActor.data.name ?? "";
            string schoolId = SchoolMembershipService.GetSchool(pActor.data.id);
            string operationKey = HistoricalSchoolTravelPersistenceRules.
                OperationKey(pActor.data.id, pDestination.data.id, pYear);
            HistoricalSchoolJourneyArrivalStamp revision =
                HistoricalSchoolJourneyArrivalRevision.Capture(
                    pActor.data.id, pDestination.data.id,
                    pDestinationExisted: true);
            var operation = new JourneyArrivalWriteOperation(operationKey,
                previous, desired,
                pPreviousResidence?.data?.id ?? previous.ResidenceCityId,
                pDestination.data.id,
                pDestination.kingdom?.data?.id ?? -1L,
                schoolId, actorName, pYear,
                actorName + "|" + pDestination.data.name,
                World.world?.getCurWorldTime() ?? 0d, revision);
            if (!HistoricalSchoolWriteBufferService.TryEnqueue(operation))
                return false;
            PendingArrivalActorIds.Add(pActor.data.id);
            return true;
        }

        private static bool ProcessPendingArrivalRetry()
        {
            if (!PendingArrivalRetries.TryGetFirst(out long actorId,
                    out JourneyArrivalWriteOperation operation) ||
                operation == null || !operation.RetryDue(
                    HistoricalSchoolActivityQueue.CurrentFrame))
                return false;
            if (operation.TryRetry())
                PendingArrivalRetries.Remove(actorId);
            else
                operation.ScheduleRetry(operation.ProjectionOnlyRetry);
            return true;
        }

        private static void QueueArrivalRetry(
            JourneyArrivalWriteOperation pOperation, bool pProjectionOnly)
        {
            if (pOperation == null || pOperation.ActorId < 0L) return;
            pOperation.ScheduleRetry(pProjectionOnly);
            if (!PendingArrivalRetries.TryUpsertOwned(pOperation.ActorId,
                    pOperation, PendingArrivalActorIds))
                return;
        }

        private static bool EnsureTravelTask(Actor pActor)
        {
            if (!IsUsable(pActor)) return false;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor.data.id);
            City destination = FindCity(state?.DestinationCityId ?? -1L);
            string schoolId = SchoolMembershipService.GetSchool(pActor.data.id);
            if (state?.LifecycleState != HistoricalSchoolLifecycleState.Travelling ||
                !IsLivingCity(destination) || string.IsNullOrEmpty(schoolId)) return false;
            string activityId = TravelActivityId(pActor.data.id);
            if (HistoricalSchoolTaskLeaseService.TryGet(pActor.data.id,
                    out HistoricalSchoolTaskLease existing))
            {
                bool currentTravel = existing.ActivityId == activityId &&
                                     existing.TaskId == HistoricalSchoolContent.TravelTaskId &&
                                     existing.CityId == destination.data.id;
                if (!currentTravel) return false;
                if (ActiveTravelTargets.TryGetValue(pActor.data.id,
                        out WorldTile existingTarget) &&
                    existingTarget?.zone?.city == destination &&
                    pActor.isTask(HistoricalSchoolContent.TravelTaskId))
                    return true;
                HistoricalSchoolTaskLeaseService.ReleaseExact(
                    pActor.data.id, activityId);
            }
            WorldTile target = DestinationTile(destination, pActor, schoolId);
            if (target == null) return false;
            long frame = HistoricalSchoolActivityQueue.CurrentFrame;
            if (!HistoricalSchoolTaskLeaseService.TrySchedule(
                    pActor,
                    activityId,
                    HistoricalSchoolContent.TravelTaskId,
                    schoolId,
                    destination.data.id,
                    activityId,
                    target,
                    frame,
                    frame + TravelTaskLeaseFrames)) return false;
            ActiveTravelTargets[pActor.data.id] = target;
            return true;
        }

        private static List<City> BuildIndexedCities()
        {
            var cities = new List<City>();
            try
            {
                if (World.world?.cities == null) return cities;
                foreach (City city in World.world.cities)
                    if (IsLivingCity(city)) cities.Add(city);
            }
            catch { }
            var selected = new List<City>(MaxIndexedCities);
            foreach (IGrouping<long, City> group in cities.GroupBy(p => p.kingdom.id)
                         .OrderBy(p => p.Key))
            {
                City representative = group.OrderByDescending(p => p.kingdom.capital == p)
                    .ThenByDescending(SafePopulation)
                    .ThenBy(p => p.data.id)
                    .First();
                selected.Add(representative);
                if (selected.Count >= MaxIndexedCities) return selected;
            }
            foreach (City city in cities.Except(selected)
                         .OrderByDescending(SafePopulation).ThenBy(p => p.data.id))
            {
                selected.Add(city);
                if (selected.Count >= MaxIndexedCities) break;
            }
            return selected;
        }

        private static IReadOnlyList<City> IndexedCities(int pYear)
        {
            if (_indexedCities != null && _indexedCitiesYear == pYear)
                return _indexedCities;
            _indexedCities = BuildIndexedCities();
            _indexedCitiesYear = pYear;
            return _indexedCities;
        }

        private static bool TransportAvailable(City pFrom, City pTo)
        {
            WorldTile from = pFrom?.getTile();
            WorldTile to = pTo?.getTile();
            if (from == null || to == null) return false;
            if (from.isSameIsland(to)) return true;
            return FindDock(pFrom) != null && FindDock(pTo) != null;
        }

        private static Building FindDock(City pCity)
        {
            if (pCity?.buildings == null) return null;
            try
            {
                foreach (Building building in pCity.buildings)
                    if (building?.asset?.docks == true && building.isUsable() &&
                        !building.isAbandoned() && !building.isUnderConstruction()) return building;
            }
            catch { }
            return null;
        }

        private static WorldTile DestinationTile(City pCity, Actor pActor, string pSchoolId)
        {
            if (!IsLivingCity(pCity) || pActor?.data == null ||
                string.IsNullOrEmpty(pSchoolId)) return null;
            return HistoricalSchoolVenueProvider.TryFind(pCity, pActor, pSchoolId,
                HistoricalSchoolVenueKind.TravelArrival, out WorldTile primary, out _,
                out _)
                ? primary
                : null;
        }

        private static bool TryBeginMaritimeTravel(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState, City pDestination,
            WorldTile pTarget)
        {
            if (!IsUsable(pActor) || pState == null ||
                !IsLivingCity(pDestination) || pTarget?.data == null ||
                pActor.current_tile?.data == null)
                return false;

            bool sameIsland;
            try { sameIsland = pActor.current_tile.isSameIsland(pTarget); }
            catch { return false; }

            bool hasOwnedRequest = MaritimeTravelStates.TryGetValue(
                pActor.data.id, out MaritimeTravelState existing);
            if (hasOwnedRequest)
            {
                bool sameJourney = existing.DestinationCityId ==
                                   pDestination.data.id &&
                                   existing.TargetTileId ==
                                   pTarget.data.tile_id;
                if (!sameIsland && sameJourney)
                {
                    City waitingResidence = FindCity(pState.ResidenceCityId);
                    Kingdom waitingKingdom = waitingResidence?.kingdom;
                    if (IsLivingCity(waitingResidence) &&
                        waitingKingdom?.data != null)
                        ArmyRtsTransportProductionService.Request(
                            waitingKingdom, existing.Request);
                    return true;
                }
                CancelMaritimeTravel(pActor.data.id, pActor);
                hasOwnedRequest = false;
            }

            bool travelValid = pState.LifecycleState ==
                               HistoricalSchoolLifecycleState.Travelling;
            if (!HistoricalSchoolMaritimeTravelRules.ShouldRequestTaxi(
                    travelValid, pActor.is_inside_boat, sameIsland,
                    hasOwnedRequest))
                return false;

            City residence = FindCity(pState.ResidenceCityId);
            Kingdom departureKingdom = residence?.kingdom;
            if (!IsLivingCity(residence) || departureKingdom?.data == null)
                return false;

            ReleaseUnownedTaxiRequest(pActor);
            try
            {
                var request = new TaxiRequest(pActor, departureKingdom,
                    pActor.current_tile, pTarget);
                TaxiManager.list.Add(request);
                ArmyRtsTransportProductionService.Request(departureKingdom, request);
                MaritimeTravelStates[pActor.data.id] =
                    new MaritimeTravelState
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
                MaritimeTravelStates.Remove(pActor.data.id);
                return false;
            }
        }

        public static bool TryResumeAfterDisembark(Actor pActor)
        {
            if (pActor?.data == null ||
                !MaritimeTravelStates.TryGetValue(pActor.data.id,
                    out MaritimeTravelState maritime))
                return false;

            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor.data.id);
            City destination = FindCity(maritime.DestinationCityId);
            WorldTile target = FindTile(maritime.TargetTileId);
            bool usable = IsUsable(pActor);
            bool travelling = state?.LifecycleState ==
                              HistoricalSchoolLifecycleState.Travelling;
            bool destinationValid = IsLivingCity(destination) &&
                                    target?.data != null;
            bool reachedDestinationIsland = false;
            try
            {
                reachedDestinationIsland = destinationValid &&
                    pActor.current_tile?.isSameIsland(target) == true;
            }
            catch { }

            if (HistoricalSchoolMaritimeTravelRules.ShouldCancelOwnedTravel(
                    ownedTravel: true, usable, travelling,
                    destinationValid))
            {
                MaritimeTravelStates.Remove(pActor.data.id);
                ReleaseTravelTask(pActor.data.id);
                if (usable && travelling && !destinationValid)
                {
                    HistoricalAffiliationService.CancelTravel(pActor);
                    SchoolLineageService.ReleaseItinerant(pActor);
                }
                return false;
            }

            if (!HistoricalSchoolMaritimeTravelRules.
                    ShouldResumeAfterDisembark(
                        ownedTravel: true, usable, travelling,
                        destinationValid, reachedDestinationIsland))
            {
                MaritimeTravelStates.Remove(pActor.data.id);
                ReleaseTravelTask(pActor.data.id);
                HistoricalAffiliationService.RegisterTransportFailure(pActor,
                    Date.getCurrentYear());
                return false;
            }

            MaritimeTravelStates.Remove(pActor.data.id);
            if (EnsureTravelTask(pActor)) return true;
            ReleaseTravelTask(pActor.data.id);
            HistoricalAffiliationService.RegisterTransportFailure(pActor,
                Date.getCurrentYear());
            return false;
        }

        private static void CancelMaritimeTravel(long pActorId,
            Actor pActor)
        {
            if (!MaritimeTravelStates.TryGetValue(pActorId,
                    out MaritimeTravelState maritime)) return;
            MaritimeTravelStates.Remove(pActorId);
            TaxiRequest request = maritime.Request;
            if (request == null) return;
            try
            {
                Actor actor = pActor ?? maritime.Actor;
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

        private static WorldTile FindTile(int pTileId)
        {
            WorldTile[] tiles = World.world?.tiles_list;
            return tiles != null && pTileId >= 0 && pTileId < tiles.Length
                ? tiles[pTileId]
                : null;
        }

        private static string TravelActivityId(long pActorId)
        {
            return "travel:" + pActorId;
        }

        private static void ReleaseTravelTask(long pActorId)
        {
            ActiveTravelTargets.Remove(pActorId);
            HistoricalSchoolTaskLeaseService.ReleaseExact(
                pActorId, TravelActivityId(pActorId));
        }

        private static void ClearMaritimeTravels()
        {
            var states = new List<MaritimeTravelState>(
                MaritimeTravelStates.Values);
            MaritimeTravelStates.Clear();
            for (int i = 0; i < states.Count; i++)
            {
                TaxiRequest request = states[i]?.Request;
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

        private static int PositiveModulo(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int result = pValue % pCount;
            return result < 0 ? result + pCount : result;
        }

        private static bool IsLivingCity(City pCity)
        {
            return pCity?.data != null && !pCity.isRekt() && pCity.kingdom?.data != null &&
                   !pCity.kingdom.isRekt();
        }

        private static bool IsUsable(Actor pActor)
        {
            return pActor?.data != null && pActor.isAlive() && !pActor.isRekt();
        }

        private static bool IsServingOrBound(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState)
        {
            if (pActor?.data == null || pState == null) return true;
            if (pState.ServiceKingdomId >= 0 ||
                pState.LifecycleState == HistoricalSchoolLifecycleState.Serving) return true;
            pActor.data.get(LineageKeys.COURT_KINGDOM_ID, out long courtKingdomId, -1L);
            if (courtKingdomId >= 0 || pActor.isKing() || pActor.isCityLeader()) return true;
            if (pActor.isWarrior() || pActor.hasArmy() ||
                GeneralService.IsActiveGeneralFast(pActor))
                return true;
            try { return HeirService.IsCurrentHeir(pActor.kingdom, pActor); }
            catch { return false; }
        }

        private static Actor FindActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static City FindCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return pCity?.getPopulationPeople() ?? 0; }
            catch { return 0; }
        }

        private static float SafeDevelopment(City pCity)
        {
            try
            {
                return SafePopulation(pCity) + pCity.countZones() * 8f +
                       (pCity.buildings?.Count ?? 0) * 3f;
            }
            catch { return SafePopulation(pCity); }
        }

        private static bool ReceptiveRuler(Kingdom pKingdom)
        {
            try { return pKingdom?.king?.stats?["diplomacy"] >= 50f; }
            catch { return false; }
        }

        private static bool SafeAtWar(Kingdom pKingdom)
        {
            try { return pKingdom?.hasEnemies() == true; }
            catch { return true; }
        }

        private static bool HasOpenCentralOffice(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            try
            {
                string[] expected =
                    CourtService.CentralOfficeIdsForCurrentProfile(pKingdom);
                if (expected.Length == 0) return false;
                int occupied = CourtService.GetActiveOfficers(pKingdom, 96)
                    .Where(p => p.layer == CourtOfficeLayer.Central)
                    .Select(p => p.office_id)
                    .Distinct(StringComparer.Ordinal).Count();
                return occupied < expected.Length;
            }
            catch { return false; }
        }

        private sealed class DestinationPreparation
        {
            public DestinationPreparation(Actor pActor,
                HistoricalSchoolAffiliationSnapshot pState)
            {
                Actor = pActor;
                State = pState;
            }

            public Actor Actor { get; }
            public HistoricalSchoolAffiliationSnapshot State { get; }
        }

        private sealed class TravelCityTarget
        {
            public TravelCityTarget(City pCity, WorldTile pTarget)
            {
                City = pCity;
                Target = pTarget;
            }

            public City City { get; }
            public WorldTile Target { get; }
        }

        private static bool HasActiveDisaster(City pCity, int pPopulation)
        {
            if (pCity?.status == null) return false;
            int population = Math.Max(1, pPopulation);
            return pCity.status.sick >= Math.Max(5, population / 4) ||
                   pCity.status.hungry >= Math.Max(5, population / 3);
        }

        private static float ProblemMatch(HistoricalSchoolMasterDefinition pMaster, City pCity,
            int pPopulation, bool pAtWar)
        {
            if (pMaster == null || pCity?.status == null) return 0f;
            float match = 0f;
            int population = Math.Max(1, pPopulation);
            bool livelihood = pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Livelihood) ||
                              pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Famine);
            bool medicine = pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Medicine) ||
                            pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Epidemic);
            bool conflict = pMaster.DebateTopics.Contains(HistoricalDebateTopicId.War) ||
                            pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Defense) ||
                            pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Peace) ||
                            pMaster.DebateTopics.Contains(HistoricalDebateTopicId.Diplomacy);
            if (livelihood)
                match = Math.Max(match, Math.Min(1f, pCity.status.hungry / (float)population * 3f));
            if (medicine)
                match = Math.Max(match, Math.Min(1f, pCity.status.sick / (float)population * 4f));
            if (conflict && pAtWar) match = Math.Max(match, 1f);
            return match;
        }

        private static void QueueJourneyHistory(Actor pActor,
            City pDestination, string pActorName)
        {
            if (pActor?.data == null || pDestination?.data == null) return;
            long actorId = pActor.data.id;
            long cityId = pDestination.data.id;
            string cityName = pDestination.data.name ?? "";
            HistoryWriter.DeferredContext personContext =
                HistoryWriter.CaptureDeferredContext(
                    HistoricalAffiliationService.HomeKingdom(pActor));
            HistoryWriter.PersonSnapshot personSnapshot =
                HistoryWriter.CapturePersonSnapshot(pActor);
            HistoryText personContent = HistoryText.Actor(pActor, pActorName) +
                HistoryLocalizationRules.H("aw_hist_school_travelled_to") +
                HistoryText.City(pDestination, pDestination.kingdom);
            HistoryWriter.DeferredContext cityContext =
                HistoryWriter.CaptureDeferredContext(pDestination.kingdom);
            HistoryText cityContent = HistoryText.Actor(pActor, pActorName) +
                HistoryLocalizationRules.H("aw_hist_school_arrived_to_study");

            // 快照(CapturePersonSnapshot / CaptureDeferredContext)上面已经取好,
            // RecordDeferred* 本就表示「快照已备、现在写」。再包一层无 key 的
            // EnqueueOrdered 只是让它挤进不能合并的 <ordered> 队列 —— 和禁卫军
            // 那两处是同一个错误。全项目 8 处 RecordDeferred* 只有这 4 处套了
            // 队列,其余 4 处都是直接调。
            HistoryWriter.RecordDeferredPerson(personContext,
                personSnapshot, actorId, pActorName,
                "school_master_travel", personContent,
                ChronicleCategory.LIFE, HistoryTarget.Actor(actorId));
            HistoryWriter.RecordDeferredCity(cityContext, cityId,
                cityName, "school_master_arrival", cityContent,
                HistoryTarget.From("city", cityId));
        }

        private sealed class JourneyArrivalWriteOperation :
            IHistoricalSchoolWriteOperation,
            IHistoricalSchoolAsyncWriteOperation
        {
            private readonly HistoricalSchoolAffiliationSnapshot _previous;
            private readonly HistoricalSchoolAffiliationSnapshot _desired;
            private readonly long _previousResidenceCityId;
            private readonly long _destinationCityId;
            private readonly long _destinationKingdomId;
            private readonly string _schoolId;
            private readonly string _actorName;
            private readonly int _year;
            private readonly string _payload;
            private readonly double _worldTime;
            private readonly HistoricalSchoolJourneyArrivalStamp _revision;
            private bool _projectionOnlyRetry;
            private int _retryAttempts;
            private long _retryReadyFrame;

            public JourneyArrivalWriteOperation(string pOperationKey,
                HistoricalSchoolAffiliationSnapshot pPrevious,
                HistoricalSchoolAffiliationSnapshot pDesired,
                long pPreviousResidenceCityId, long pDestinationCityId,
                long pDestinationKingdomId, string pSchoolId,
                string pActorName, int pYear, string pPayload,
                double pWorldTime,
                HistoricalSchoolJourneyArrivalStamp pRevision)
            {
                OperationKey = pOperationKey ?? "";
                _previous = pPrevious;
                _desired = pDesired;
                _previousResidenceCityId = pPreviousResidenceCityId;
                _destinationCityId = pDestinationCityId;
                _destinationKingdomId = pDestinationKingdomId;
                _schoolId = pSchoolId ?? "";
                _actorName = pActorName ?? "";
                _year = pYear;
                _payload = pPayload ?? "";
                _worldTime = pWorldTime;
                _revision = pRevision;
            }

            public string OperationKey { get; }
            public long ActorId => _desired?.ActorId ?? -1L;
            public bool ProjectionOnlyRetry => _projectionOnlyRetry;

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                if (!HistoricalSchoolJourneyArrivalRevision.IsCurrent(_revision))
                    return HistoricalSchoolTeachingPersistenceOutcome.
                        CleanFailure;
                Actor actor = FindActor(_desired?.ActorId ?? -1L);
                City destination = FindCity(_destinationCityId);
                if (!IsUsable(actor) || !IsLivingCity(destination) ||
                    !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(
                        destination))
                    return HistoricalSchoolTeachingPersistenceOutcome.
                        CleanFailure;
                HistoricalSchoolTeachingPersistenceOutcome affiliation =
                    HistoricalSchoolStore.
                        SaveAffiliationTransitionInTransaction(pDb,
                            pTransaction, _previous, _desired, _worldTime);
                if (affiliation ==
                        HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                    affiliation ==
                        HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                    return affiliation;
                HistoricalSchoolTeachingPersistenceOutcome journeyEvent =
                    HistoricalSchoolStore.RecordSchoolEventInTransaction(
                        pDb, pTransaction, OperationKey, "journey_arrival",
                        _desired.ActorId, -1L, _schoolId,
                        _destinationCityId, _destinationKingdomId, _year,
                        _payload, 2, _worldTime);
                return HistoricalSchoolTravelPersistenceRules.Combine(
                    affiliation, journeyEvent);
            }

            public IHistoricalSchoolBackgroundWrite DetachBackgroundWrite()
            {
                Actor actor = FindActor(_desired?.ActorId ?? -1L);
                if (!IsUsable(actor)) return null;
                return new JourneyArrivalBackgroundWrite(OperationKey,
                    CopyAffiliation(_previous), CopyAffiliation(_desired),
                    _destinationCityId, _destinationKingdomId, _schoolId,
                    _year, _payload, _worldTime, _revision);
            }

            public void AfterCommit(
                HistoricalSchoolTeachingPersistenceOutcome pOutcome)
            {
                try
                {
                    if (!TryApplyCommittedProjection())
                        QueueArrivalRetry(this, pProjectionOnly: true);
                }
                catch (Exception)
                {
                    QueueArrivalRetry(this, pProjectionOnly: true);
                }
            }

            public void OnCleanFailure()
            {
                QueueArrivalRetry(this, pProjectionOnly: false);
            }

            public bool RetryDue(long pFrame)
            {
                return pFrame >= _retryReadyFrame;
            }

            public void ScheduleRetry(bool pProjectionOnly)
            {
                _projectionOnlyRetry |= pProjectionOnly;
                _retryAttempts = Math.Min(31, _retryAttempts + 1);
                int shift = Math.Min(8, Math.Max(0,
                    _retryAttempts - 1));
                long delay = 1L << shift;
                long frame = HistoricalSchoolActivityQueue.CurrentFrame;
                _retryReadyFrame = frame > long.MaxValue - delay
                    ? long.MaxValue
                    : frame + delay;
            }

            public bool TryRetry()
            {
                if (_projectionOnlyRetry)
                    return TryApplyCommittedProjection();
                Actor actor = FindActor(ActorId);
                HistoricalSchoolAffiliationSnapshot current =
                    HistoricalAffiliationService.Get(ActorId);
                if (!IsUsable(actor) || current == null)
                {
                    PendingArrivalActorIds.Remove(ActorId);
                    return true;
                }
                if (current.LifecycleState !=
                        HistoricalSchoolLifecycleState.Travelling &&
                    current.LifecycleState !=
                        HistoricalSchoolLifecycleState.Voyage)
                {
                    PendingArrivalActorIds.Remove(ActorId);
                    return true;
                }
                City destination = FindCity(current.DestinationCityId);
                if (!IsLivingCity(destination) ||
                    !HistoricalSchoolXiaAccessService.CanReceiveSchoolTravel(
                        destination))
                {
                    RestoreCancelledVoyage(actor, current);
                    PendingArrivalActorIds.Remove(ActorId);
                    return true;
                }
                City previousResidence = FindCity(current.ResidenceCityId);
                PendingArrivalActorIds.Remove(ActorId);
                if (TryQueueArrival(actor, previousResidence, destination,
                        current, _year))
                    return true;
                PendingArrivalActorIds.Add(ActorId);
                return false;
            }

            private bool TryApplyCommittedProjection()
            {
                if (!HistoricalAffiliationService.ApplyPersistedTransition(
                        _previous, _desired))
                {
                    HistoricalAffiliationService.LoadState();
                    if (!HistoricalAffiliationService.ApplyPersistedTransition(
                            _previous, _desired)) return false;
                }
                Actor actor = FindActor(ActorId);
                City previousResidence = FindCity(_previousResidenceCityId);
                City destination = FindCity(_destinationCityId);
                if (!IsUsable(actor) || !IsLivingCity(destination))
                    return false;
                HistoricalSchoolStore.InvalidateTeachingCommit(
                    _destinationCityId);
                ReleaseTravelTask(ActorId);
                SchoolLineageService.ReleaseItinerant(actor);
                actor.finishStatusEffect(
                    HistoricalSchoolContent.VoyageStatusId);
                actor.addStatusEffect(HistoricalSchoolContent.GuestStatusId,
                    120f, pColorEffect: false);
                CitySchoolSnapshotService.MarkDirty(previousResidence);
                CitySchoolSnapshotService.MarkDirty(destination);
                QueueJourneyHistory(actor, destination, _actorName);
                PendingArrivalActorIds.Remove(ActorId);
                return true;
            }

            private static HistoricalSchoolAffiliationSnapshot CopyAffiliation(
                HistoricalSchoolAffiliationSnapshot pSnapshot)
            {
                if (pSnapshot == null) return null;
                return new HistoricalSchoolAffiliationSnapshot(pSnapshot.ActorId,
                    pSnapshot.HomeKingdomId, pSnapshot.HomeKingdomName,
                    pSnapshot.HometownCityId, pSnapshot.ResidenceCityId,
                    pSnapshot.PreviousResidenceCityId,
                    pSnapshot.DestinationCityId, pSnapshot.ServiceKingdomId,
                    pSnapshot.LifecycleState, pSnapshot.ServiceStartYear,
                    pSnapshot.ServiceEndYear, pSnapshot.LastTravelYear,
                    pSnapshot.TravelWaitStartYear, pSnapshot.VoyageStartYear,
                    pSnapshot.VoyageArrivalYear, pSnapshot.TransportFailures);
            }
        }

        private sealed class JourneyArrivalBackgroundWrite :
            IHistoricalSchoolBackgroundWrite
        {
            private readonly string _operationKey;
            private readonly HistoricalSchoolAffiliationSnapshot _previous;
            private readonly HistoricalSchoolAffiliationSnapshot _desired;
            private readonly long _destinationCityId;
            private readonly long _destinationKingdomId;
            private readonly string _schoolId;
            private readonly int _year;
            private readonly string _payload;
            private readonly double _worldTime;
            private readonly HistoricalSchoolJourneyArrivalStamp _revision;

            public JourneyArrivalBackgroundWrite(string pOperationKey,
                HistoricalSchoolAffiliationSnapshot pPrevious,
                HistoricalSchoolAffiliationSnapshot pDesired,
                long pDestinationCityId, long pDestinationKingdomId,
                string pSchoolId, int pYear, string pPayload,
                double pWorldTime,
                HistoricalSchoolJourneyArrivalStamp pRevision)
            {
                _operationKey = pOperationKey ?? "";
                _previous = pPrevious;
                _desired = pDesired;
                _destinationCityId = pDestinationCityId;
                _destinationKingdomId = pDestinationKingdomId;
                _schoolId = pSchoolId ?? "";
                _year = pYear;
                _payload = pPayload ?? "";
                _worldTime = pWorldTime;
                _revision = pRevision;
            }

            public HistoricalSchoolTeachingPersistenceOutcome Execute(
                System.Data.SQLite.SQLiteConnection pDb,
                System.Data.SQLite.SQLiteTransaction pTransaction)
            {
                if (!HistoricalSchoolJourneyArrivalRevision.IsCurrent(_revision))
                    return HistoricalSchoolTeachingPersistenceOutcome.
                        CleanFailure;
                HistoricalSchoolTeachingPersistenceOutcome affiliation =
                    HistoricalSchoolStore.SaveAffiliationTransitionInTransaction(
                        pDb, pTransaction, _previous, _desired, _worldTime);
                if (affiliation ==
                        HistoricalSchoolTeachingPersistenceOutcome.Unknown ||
                    affiliation ==
                        HistoricalSchoolTeachingPersistenceOutcome.CleanFailure)
                    return affiliation;
                HistoricalSchoolTeachingPersistenceOutcome journeyEvent =
                    HistoricalSchoolStore.RecordSchoolEventInTransaction(
                        pDb, pTransaction, _operationKey, "journey_arrival",
                        _desired?.ActorId ?? -1L, -1L, _schoolId,
                        _destinationCityId, _destinationKingdomId, _year,
                        _payload, 2, _worldTime);
                return HistoricalSchoolTravelPersistenceRules.Combine(
                    affiliation, journeyEvent);
            }
        }
    }
}
