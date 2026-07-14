using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.court;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.schools
{
    internal static class HistoricalSchoolTravelService
    {
        public const int MaxDestinationCandidates = 16;
        private const int MaxIndexedCities = 64;
        private const int MaxMastersPerQuarter = 24;
        private const int MinResidenceYears = 8;
        private static readonly int[] BucketOffsets = new int[4];

        public static void ClearRuntime()
        {
            Array.Clear(BucketOffsets, 0, BucketOffsets.Length);
        }

        public static void ProcessQuarter(int pQuarterKey)
        {
            int year = Date.getCurrentYear();
            int bucket = ((pQuarterKey % 4) + 4) % 4;
            BucketOffsets[bucket] = HistoricalSchoolTravelQuarterScheduler.Process(
                () => CompleteDueVoyages(year),
                () => HistoricalAffiliationService.ActiveSnapshots(
                    pTravelEligibleOnly: true, pTravelBucket: bucket),
                BucketOffsets[bucket], MaxMastersPerQuarter,
                state => PrepareDestination(state, year),
                () => BuildIndexedCities(),
                (prepared, cities) => TryChooseDestination(prepared.Actor,
                    prepared.State, cities, year));
        }

        public static bool TryPreparePhysicalTravel(Actor pActor, out WorldTile pTarget)
        {
            pTarget = null;
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor) || state == null || IsServingOrBound(pActor, state) ||
                state.LifecycleState != HistoricalSchoolLifecycleState.Travelling) return false;
            City destination = FindCity(state.DestinationCityId);
            if (!IsLivingCity(destination))
            {
                HistoricalAffiliationService.CancelTravel(pActor);
                SchoolLineageService.ReleaseItinerant(pActor);
                return false;
            }
            pTarget = DestinationTile(destination);
            return pTarget != null;
        }

        public static bool TryCompletePhysicalArrival(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (!IsUsable(pActor) || state == null ||
                state.LifecycleState != HistoricalSchoolLifecycleState.Travelling) return false;
            City destination = FindCity(state.DestinationCityId);
            City previousResidence = FindCity(state.ResidenceCityId);
            WorldTile tile = DestinationTile(destination);
            if (tile == null || pActor.current_tile == null ||
                Toolbox.SquaredDistTile(pActor.current_tile, tile) > 4)
            {
                HistoricalAffiliationService.RegisterTransportFailure(pActor,
                    Date.getCurrentYear());
                return false;
            }
            if (!HistoricalAffiliationService.TryArrive(pActor, destination.data.id,
                    Date.getCurrentYear())) return false;
            if (!RecordJourney(pActor, destination, Date.getCurrentYear()))
            {
                if (!HistoricalAffiliationService.RollbackArrival(pActor, state))
                    ModClass.LogWarning("Historical school physical arrival rollback failed");
                else
                    RestorePhysicalArrival(pActor, state);
                return false;
            }
            SchoolLineageService.ReleaseItinerant(pActor);
            pActor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                pColorEffect: false);
            CitySchoolSnapshotService.MarkDirty(previousResidence);
            CitySchoolSnapshotService.MarkDirty(destination);
            return true;
        }

        public static void OnCommittedDeath(Actor pActor)
        {
            if (pActor?.data == null) return;
            try { pActor.finishStatusEffect(HistoricalSchoolContent.VoyageStatusId); }
            catch { }
            SchoolLineageService.ReleaseItinerant(pActor);
        }

        public static void ReportImmediatePathFailure(Actor pActor)
        {
            HistoricalSchoolAffiliationSnapshot state =
                HistoricalAffiliationService.Get(pActor?.data?.id ?? -1L);
            if (state?.LifecycleState == HistoricalSchoolLifecycleState.Travelling)
                HistoricalAffiliationService.RegisterTransportFailure(pActor,
                    Date.getCurrentYear());
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
            var cheapCandidates = new List<TravelCityTarget>(pCities.Count);
            foreach (City city in pCities)
            {
                if (!IsLivingCity(city) || city.data.id == pState.ResidenceCityId) continue;
                WorldTile target = DestinationTile(city);
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
            HistoricalAffiliationService.RepairEnginePointers(actor);
            if (!IsUsable(actor) || IsServingOrBound(actor, pState) ||
                pState.LifecycleState == HistoricalSchoolLifecycleState.Dead) return null;
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Voyage) return null;
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.ChoosingDestination)
            {
                if (HistoricalAffiliationService.TryStartChosenTravel(actor))
                    EnsureTravelTask(actor);
                return null;
            }
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Travelling)
            {
                if (!TryStartTimedVoyage(actor, pState, pYear)) EnsureTravelTask(actor);
                return null;
            }
            if (pState.LifecycleState != HistoricalSchoolLifecycleState.AtHome &&
                pState.LifecycleState != HistoricalSchoolLifecycleState.Resident) return null;
            RestoreScholarJob(actor);
            if (pState.LifecycleState == HistoricalSchoolLifecycleState.Resident)
                actor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                    pColorEffect: false);
            if (pState.LastTravelYear >= 0 && pYear - pState.LastTravelYear < MinResidenceYears)
                return null;
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
            if (!IsLivingCity(destination) || residence?.getTile() == null) return false;
            int distance = Toolbox.SquaredDistTile(residence.getTile(), destination.getTile());
            int arrival = HistoricalSchoolRules.VoyageArrivalYear(pYear, distance);
            if (!HistoricalAffiliationService.TryBeginVoyage(pActor, pYear, arrival))
                return false;
            pActor.cancelAllBeh();
            Building dock = FindDock(residence);
            if (dock != null) pActor.stayInBuilding(dock);
            RefreshVoyageIsolation(pActor);
            return true;
        }

        private static void CompleteDueVoyages(int pYear)
        {
            foreach (HistoricalSchoolAffiliationSnapshot state in
                     HistoricalAffiliationService.ActiveSnapshots(pTravelEligibleOnly: true,
                         pTravelOnly: true))
            {
                if (state.LifecycleState != HistoricalSchoolLifecycleState.Voyage) continue;
                Actor actor = FindActor(state.ActorId);
                City destination = FindCity(state.DestinationCityId);
                City previousResidence = FindCity(state.ResidenceCityId);
                if (!IsUsable(actor)) continue;
                RefreshVoyageIsolation(actor);
                if (!IsLivingCity(destination))
                {
                    RestoreCancelledVoyage(actor, state);
                    continue;
                }
                if (state.VoyageArrivalYear < 0 || pYear < state.VoyageArrivalYear) continue;
                Building dock = FindDock(destination);
                WorldTile tile = dock?.current_tile ?? destination.getTile();
                if (tile == null) continue;
                actor.exitBuilding();
                actor.is_visible = true;
                actor.spawnOn(tile);
                if (!HistoricalAffiliationService.TryArrive(actor, destination.data.id, pYear))
                    continue;
                if (!RecordJourney(actor, destination, pYear))
                {
                    if (!HistoricalAffiliationService.RollbackArrival(actor, state))
                        ModClass.LogWarning("Historical school voyage arrival rollback failed");
                    else
                        RestoreCancelledVoyage(actor, state);
                    continue;
                }
                SchoolLineageService.ReleaseItinerant(actor);
                actor.finishStatusEffect(HistoricalSchoolContent.VoyageStatusId);
                RestoreScholarJob(actor);
                actor.addStatusEffect(HistoricalSchoolContent.GuestStatusId, 120f,
                    pColorEffect: false);
                CitySchoolSnapshotService.MarkDirty(previousResidence);
                CitySchoolSnapshotService.MarkDirty(destination);
            }
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
            SchoolLineageService.ReleaseItinerant(pActor);
            pActor.finishStatusEffect(HistoricalSchoolContent.VoyageStatusId);
            RestoreScholarJob(pActor);
        }

        private static void RestorePhysicalArrival(Actor pActor,
            HistoricalSchoolAffiliationSnapshot pState)
        {
            City residence = FindCity(pState == null ? -1L : pState.ResidenceCityId);
            WorldTile tile = residence?.getTile();
            if (tile != null) pActor.spawnOn(tile);
            EnsureTravelTask(pActor);
        }

        private static void RestoreScholarJob(Actor pActor)
        {
            CitizenJobAsset job =
                AssetManager.citizen_job_library.get(HistoricalSchoolContent.CitizenJobId);
            if (job != null) pActor.setCitizenJob(job);
        }

        private static void EnsureTravelTask(Actor pActor)
        {
            if (!IsUsable(pActor) || pActor.isTask(HistoricalSchoolContent.TravelTaskId)) return;
            pActor.setTask(HistoricalSchoolContent.TravelTaskId, pClean: true,
                pCleanJob: false, pForceAction: true);
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

        private static WorldTile DestinationTile(City pCity)
        {
            return IsLivingCity(pCity) ? pCity.getTile() : null;
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
            if (pActor.isWarrior() || pActor.hasArmy() || GeneralService.IsGeneral(pActor))
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
                string[] expected = CourtTierRules.CentralOfficesForTier(
                    CourtService.ResolveTier(pKingdom));
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

        private static bool RecordJourney(Actor pActor, City pDestination, int pYear)
        {
            HistoricalSchoolMasterDefinition definition =
                HistoricalSchoolDescentService.DefinitionFor(pActor);
            string name = definition?.CanonicalName ?? pActor?.data?.name ?? "";
            if (!HistoricalSchoolStore.RecordSchoolEvent("journey_arrival", pActor.data.id, -1,
                SchoolMembershipService.GetSchool(pActor.data.id), pDestination.data.id,
                pDestination.kingdom?.data?.id ?? -1L, pYear,
                name + "|" + pDestination.data.name, 2,
                World.world?.getCurWorldTime() ?? 0d)) return false;
            try
            {
                HistoryWriter.RecordPerson(pActor.data.id,
                    HistoricalAffiliationService.HomeKingdom(pActor), name,
                    "school_master_travel", name + "游学至" + pDestination.data.name,
                    ChronicleCategory.LIFE);
                HistoryWriter.RecordCity(pDestination, pDestination.kingdom,
                    "school_master_arrival", name + "来此游学");
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Historical school journey history failed: " +
                                    error.Message);
            }
            return true;
        }
    }
}
