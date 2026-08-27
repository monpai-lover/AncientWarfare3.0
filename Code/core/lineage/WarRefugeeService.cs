using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class WarRefugeeService
    {
        private const int CityBudgetPerMonth = 16;
        private const int JourneyBudgetPerMonth = 32;
        private const int HouseholdLimit = 8;
        private static long _lastMonthToken = long.MinValue;
        private static int _month;
        private static int _cityCursor;
        private static long _journeyCursor = -1L;
        private static readonly HashSet<long> ThreatenedCityIds = new HashSet<long>();
        private static readonly Dictionary<long, HashSet<long>> DestinationCityIdsByKingdom =
            new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, long> DestinationOwnerByCity =
            new Dictionary<long, long>();
        private static Dictionary<long, int> _monthlyReservations =
            new Dictionary<long, int>();
        private static bool _destinationSnapshotInitialized;

        private enum WarRefugeeLifecycleResult
        {
            RemainsArrived,
            Transitioned,
            PersistenceFailed
        }

        internal static void ProcessAuthorityCycle(long pCycleToken,
            bool pPaused)
        {
            if (pPaused || pCycleToken == _lastMonthToken) return;
            _lastMonthToken = pCycleToken;
            int currentMonth = ResolveMonthKey();
            if (currentMonth == _month) return;
            _month = currentMonth;
            if (!CanMutate()) return;
            RefreshMonthlyReservations();
            ProcessPersistedJourneys(currentMonth, JourneyBudgetPerMonth);
            CaptureThreatenedCities(currentMonth, CityBudgetPerMonth);
        }

        internal static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || _destinationSnapshotInitialized) return;
            BuildDestinationSnapshot();
        }

        internal static void OnCityOwnerChanged(City pCity,
            Kingdom pOldKingdom, Kingdom pNewKingdom)
        {
            if (!_destinationSnapshotInitialized || pCity?.data == null) return;
            long cityId = pCity.id;
            RemoveDestination(cityId, pOldKingdom?.data?.id ?? -1L);
            long newKingdomId = pNewKingdom?.data?.id ??
                pCity.kingdom?.data?.id ?? -1L;
            if (newKingdomId >= 0L) AddDestination(cityId, newKingdomId);
        }

        internal static void OnCityThreatStateChanged(City pCity, bool pActive)
        {
            long cityId = pCity?.data?.id ?? -1L;
            if (cityId < 0L) return;
            if (pActive) ThreatenedCityIds.Add(cityId);
            else ThreatenedCityIds.Remove(cityId);
        }

        internal static void RebuildRuntime()
        {
            Reset();
            try
            {
                LineageArchiveManager archive = LineageArchiveManager.Instance;
                if (archive?.OperatingDB != null)
                {
                    WarRefugeePersistence.EnsureSchema(archive.OperatingDB);
                    RecoverActiveJourneys(archive.OperatingDB);
                    BuildDestinationSnapshot();
                }
            }
            catch { }
        }

        internal static void OnActorDying(Actor pActor)
        {
            if (pActor?.data == null) return;
            try
            {
                LineageArchiveManager archive = LineageArchiveManager.Instance;
                if (archive?.OperatingDB == null) return;
                if (!WarRefugeePersistence.TryGetActiveJourneyForActor(
                    archive.OperatingDB, pActor.data.id, out long journeyId)) return;
                WarRefugeePersistence.SetMemberActive(archive.OperatingDB,
                    journeyId, pActor.data.id, false);
                if (WarRefugeePersistence.TryLoadJourney(archive.OperatingDB,
                    journeyId, out WarRefugeeJourneySnapshot dyingJourney))
                {
                    dyingJourney.ReservedCapacity =
                        WarRefugeePersistence.CountActiveMembers(
                            archive.OperatingDB, journeyId);
                    WarRefugeePersistence.UpsertJourney(archive.OperatingDB,
                        dyingJourney);
                }
                IReadOnlyList<WarRefugeeActiveMember> members =
                    WarRefugeePersistence.LoadActiveMembers(archive.OperatingDB,
                        journeyId, HouseholdLimit);
                for (int i = 0; i < members.Count; i++)
                {
                    Actor candidate = ResolveActor(members[i].ActorId);
                    if (!IsLivingActor(candidate) || !candidate.isAdult()) continue;
                    WarRefugeePersistence.SetMemberLeader(archive.OperatingDB,
                        journeyId, candidate.data.id);
                    break;
                }
            }
            catch { }
        }

        internal static void OnActorJoinedCity(Actor pActor, City pCity)
        {
            if (pActor?.data == null || pCity?.data == null) return;
            try
            {
                pActor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                    out long journeyId, -1L);
                if (journeyId < 0L) return;
                LineageArchiveManager archive = LineageArchiveManager.Instance;
                if (archive?.OperatingDB == null ||
                    !WarRefugeePersistence.TryGetActiveJourneyForActor(
                        archive.OperatingDB, pActor.data.id,
                        out long activeJourneyId) ||
                    !WarRefugeePersistence.TryLoadJourney(archive.OperatingDB,
                        activeJourneyId,
                        out WarRefugeeJourneySnapshot journey)) return;
                if (journey.DestinationCityId != pCity.id)
                    HandleUnexpectedCityChange(archive.OperatingDB, journey,
                        pActor, pCity);
                // The monthly leader-distance/abstract path owns whole-household arrival.
            }
            catch { }
        }

        internal static void OnActorBorn(Actor pBaby, Actor pParent1,
            Actor pParent2)
        {
            if (pBaby?.data == null || pBaby.city?.culture == null) return;
            try
            {
                bool refugeeParent = HasActiveJourney(pParent1) ||
                                      HasActiveJourney(pParent2);
                if (refugeeParent)
                    pBaby.setCulture(pBaby.city.culture);
            }
            catch { }
        }

        private static bool HasActiveJourney(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                out long journeyId, -1L);
            return journeyId >= 0L && HasActiveJourneyInDatabase(pActor);
        }

        private static bool HasActiveJourneyInDatabase(Actor pActor)
        {
            System.Data.SQLite.SQLiteConnection db =
                LineageArchiveManager.Instance?.OperatingDB;
            if (db == null || pActor?.data == null) return false;
            if (WarRefugeePersistence.TryGetActiveJourneyForActor(db,
                    pActor.data.id, out long activeJourneyId) &&
                WarRefugeePersistence.TryLoadJourney(db, activeJourneyId,
                    out WarRefugeeJourneySnapshot activeJourney))
            {
                try { pActor.data.set(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                    activeJourneyId); }
                catch { }
                try { pActor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                    (int)activeJourney.State); }
                catch { }
                return true;
            }
            WarRefugeeJourneyState staleState = WarRefugeeJourneyState.Cancelled;
            pActor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                out long staleJourneyId, -1L);
            if (staleJourneyId >= 0L && WarRefugeePersistence.TryLoadJourney(db,
                    staleJourneyId, out WarRefugeeJourneySnapshot staleJourney) &&
                (staleJourney.State == WarRefugeeJourneyState.Settled ||
                 staleJourney.State == WarRefugeeJourneyState.Cancelled))
                staleState = staleJourney.State;
            ClearActorJourneyKeys(pActor, staleState);
            return false;
        }

        internal static void ProcessThreatBatch(
            IEnumerable<WarRefugeeThreatService.CityThreatInput> pCities)
        {
            WarRefugeeThreatService.ProcessMonthly(pCities);
        }

        internal static void ProcessJourneyBatch(
            IEnumerable<WarRefugeeJourneyService.JourneyInput> pJourneys)
        {
            WarRefugeeJourneyService.ProcessMonthly(pJourneys);
        }

        internal static void Reset()
        {
            _lastMonthToken = long.MinValue;
            _month = 0;
            _cityCursor = 0;
            _journeyCursor = -1L;
            ThreatenedCityIds.Clear();
            DestinationCityIdsByKingdom.Clear();
            DestinationOwnerByCity.Clear();
            _monthlyReservations = new Dictionary<long, int>();
            _destinationSnapshotInitialized = false;
        }

        private static int ResolveMonthKey()
        {
            try
            {
                int year = Math.Max(0, Date.getCurrentYear());
                int month = Math.Max(1, Math.Min(12, Date.getCurrentMonth()));
                return year * 12 + month;
            }
            catch
            {
                return _month < 1 ? 1 : _month + 1;
            }
        }

        private static void CaptureThreatenedCities(int pMonthKey, int pBudget)
        {
            LineageArchiveManager archive = LineageArchiveManager.Instance;
            if (archive?.OperatingDB == null || World.world?.cities == null ||
                pBudget <= 0) return;
            long[] cityIds;
            try { cityIds = new List<long>(ThreatenedCityIds).ToArray(); }
            catch { return; }
            int count = cityIds.Length;
            if (count <= 0) return;
            int processed = 0;
            int remainingWorldBudget = 32;
            while (processed++ < pBudget && remainingWorldBudget > 0)
            {
                if (_cityCursor >= count) _cityCursor = 0;
                City city = ResolveCity(cityIds[_cityCursor++]);
                if (!IsLivingCity(city) || !HasDirectThreat(city)) continue;
                Kingdom owner = city.kingdom;
                if (!IsLivingKingdom(owner)) continue;
                int population = SafePopulation(city);
                if (population <= 0) continue;
                var eligible = CollectHousehold(city, owner);
                if (eligible.Count == 0) continue;
                int hungry = Math.Max(0, city.status?.hungry ?? 0);
                var facts = new WarRefugeeThreatFacts(
                    pNearbyArmy: true,
                    pSiege: true,
                    pCombatOrTransfer: true,
                    pFamine: hungry >= Math.Max(5, population / 3),
                    pActiveWar: HasAnyWar(owner));
                int permille = WarRefugeeRules.DeparturePermille(facts, city.id);
                int cityBudget = Math.Min(8, eligible.Count);
                int quota = WarRefugeeRules.DepartureQuota(population,
                    eligible.Count, Math.Max(1, population / 5), cityBudget,
                    remainingWorldBudget, permille);
                if (quota <= 0) continue;
                City destination = SelectDestination(city, owner, quota);
                if (!IsLivingCity(destination)) continue;
                int created = CreateJourney(archive.OperatingDB, city,
                    destination, eligible, quota, pMonthKey);
                AddMonthlyReservation(destination.id, created);
                remainingWorldBudget -= Math.Max(0, created);
            }
        }

        private static void ProcessPersistedJourneys(int pMonthKey, int pBudget)
        {
            LineageArchiveManager archive = LineageArchiveManager.Instance;
            if (archive?.OperatingDB == null) return;
            IReadOnlyList<WarRefugeeJourneySnapshot> journeys =
                WarRefugeePersistence.LoadActiveJourneys(archive.OperatingDB,
                    pBudget, _journeyCursor);
            if (journeys.Count == 0 && _journeyCursor >= 0L)
                journeys = WarRefugeePersistence.LoadActiveJourneys(
                    archive.OperatingDB, pBudget, -1L);
            for (int i = 0; i < journeys.Count; i++)
            {
                WarRefugeeJourneySnapshot journey = journeys[i];
                _journeyCursor = journey.JourneyId;
                if (journey.NextRetryMonth >= 0)
                {
                    if (pMonthKey < journey.NextRetryMonth) continue;
                    journey.NextRetryMonth = -1;
                    if (!TryRerankDestination(archive.OperatingDB, journey))
                        DeferDestinationRetry(archive.OperatingDB, journey,
                            pMonthKey);
                    continue;
                }
                if (journey.State == WarRefugeeJourneyState.Arrived)
                {
                    WarRefugeeLifecycleResult result = ProcessArrivedLifecycle(
                        archive.OperatingDB, journey, pMonthKey);
                    if (result != WarRefugeeLifecycleResult.RemainsArrived)
                        continue;
                    ProcessAssimilation(archive.OperatingDB, journey,
                        pMonthKey / 12);
                    continue;
                }
                City destination = ResolveCity(journey.DestinationCityId);
                if (!IsLivingCity(destination) || destination.kingdom == null ||
                    !CanReceiveDestination(destination, journey))
                {
                    if (!TryRerankDestination(archive.OperatingDB, journey))
                        DeferDestinationRetry(archive.OperatingDB, journey,
                            pMonthKey);
                    continue;
                }
                IReadOnlyList<WarRefugeeActiveMember> members =
                    WarRefugeePersistence.LoadActiveMembers(archive.OperatingDB,
                        journey.JourneyId, HouseholdLimit);
                if (members.Count == 0) continue;
                if (journey.ArrivalYear >= 0 && pMonthKey >= journey.ArrivalYear)
                {
                    SettleJourney(archive.OperatingDB, journey, members,
                        destination, pMonthKey);
                    continue;
                }
                ProcessPhysicalJourney(archive.OperatingDB, journey, members,
                    destination, pMonthKey);
            }
        }

        private static int CreateJourney(System.Data.SQLite.SQLiteConnection pDb,
            City pOrigin, City pDestination, IReadOnlyList<Actor> pCandidates,
            int pQuota, int pMonthKey)
        {
            long id = TableIdAllocator.Next(pDb, "AW_WarRefugeeJourney",
                "JOURNEY_ID");
            if (id < 0L) return 0;
            int count = Math.Min(Math.Min(HouseholdLimit, pQuota), pCandidates.Count);
            if (count <= 0) return 0;
            WorldTile originTile = pOrigin.getTile();
            WorldTile destinationTile = pDestination.getTile();
            int distance = Distance(originTile, destinationTile);
            bool crossSea = !SameIsland(originTile, destinationTile);
            bool reachable = false;
            try { reachable = pOrigin.reachableFrom(pDestination); }
            catch { }
            int leaderIndex = -1;
            for (int i = 0; i < count; i++)
                if (IsLivingActor(pCandidates[i]) && pCandidates[i].isAdult())
                { leaderIndex = i; break; }
            bool abstractJourney = WarRefugeeRules.ShouldUseAbstractJourney(
                crossSea, reachable, 0) ||
                WarRefugeeRules.ShouldUseAbstractWithoutAdultLeader(count,
                    leaderIndex < 0 ? 0 : 1);
            var journey = new WarRefugeeJourneySnapshot
            {
                JourneyId = id,
                OriginKingdomId = pOrigin.kingdom.id,
                OriginCityId = pOrigin.id,
                DestinationKingdomId = pDestination.kingdom.id,
                DestinationCityId = pDestination.id,
                State = WarRefugeeJourneyState.Traveling,
                DepartureYear = pMonthKey / 12,
                ArrivalYear = abstractJourney
                    ? WarRefugeeRules.AbstractArrivalMonth(pMonthKey, distance)
                    : -1,
                ReservedCapacity = count,
                SafeMonths = 0,
                LastAssimilationYear = -1
            };
            var memberRows = new List<WarRefugeeMemberSnapshot>(count);
            for (int i = 0; i < count; i++)
            {
                Actor actor = pCandidates[i];
                if (actor?.data == null) return 0;
                memberRows.Add(new WarRefugeeMemberSnapshot
                {
                    JourneyId = id, ActorId = actor.data.id,
                    IsLeader = i == leaderIndex, Active = true,
                    OriginCulture = actor.culture == null
                        ? "" : Convert.ToString(actor.culture.id)
                });
            }
            if (!WarRefugeePersistence.TryCreateHouseholdAtomic(pDb, journey,
                memberRows)) return 0;
            for (int i = 0; i < count; i++)
            {
                Actor actor = pCandidates[i];
                actor.data.set(LineageKeys.WAR_REFUGEE_JOURNEY_ID, id);
                actor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                    (int)WarRefugeeJourneyState.Traveling);
            }
            if (!abstractJourney && leaderIndex >= 0)
                MoveActor(pCandidates[leaderIndex], destinationTile);
            return count;
        }

        private static void ProcessPhysicalJourney(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeActiveMember> pMembers, City pDestination,
            int pMonthKey)
        {
            Actor leader = null;
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor actor = ResolveActor(pMembers[i].ActorId);
                if (pMembers[i].IsLeader && IsLivingActor(actor) &&
                    actor.isAdult()) { leader = actor; break; }
            }
            if (leader == null)
            {
                for (int i = 0; i < pMembers.Count; i++)
                {
                    Actor actor = ResolveActor(pMembers[i].ActorId);
                    if (!IsLivingActor(actor) || !actor.isAdult()) continue;
                    if (WarRefugeePersistence.SetMemberLeader(pDb,
                        pJourney.JourneyId, actor.data.id)) leader = actor;
                    break;
                }
            }
            if (leader == null)
            {
                if (WarRefugeeRules.ShouldUseAbstractWithoutAdultLeader(
                    pMembers.Count, 0))
                {
                    pJourney.ArrivalYear = WarRefugeeRules.AbstractArrivalMonth(
                        pMonthKey, Distance(ResolveCity(pJourney.OriginCityId)?.getTile(),
                            pDestination.getTile()));
                    WarRefugeePersistence.UpsertJourney(pDb, pJourney);
                }
                else CancelJourney(pDb, pJourney);
                return;
            }
            int distance = Distance(leader.current_tile, pDestination.getTile());
            if (WarRefugeeRules.HasLeaderReachedDestination(distance, 16))
            {
                SettleJourney(pDb, pJourney, pMembers, pDestination,
                    pMonthKey);
                return;
            }
            if (distance < pJourney.LastDistance)
                pJourney.RouteRetries = 0;
            else
                pJourney.RouteRetries++;
            pJourney.LastDistance = distance;
            if (WarRefugeeRules.ShouldAdvanceAfterNoProgress(pJourney.RouteRetries))
                pJourney.ArrivalYear = WarRefugeeRules.AbstractArrivalMonth(
                    pMonthKey, distance);
            else
                MoveActor(leader, pDestination.getTile());
            AggregateFollowers(leader, pMembers);
            WarRefugeePersistence.UpsertJourney(pDb, pJourney);
        }

        private static void AggregateFollowers(Actor pLeader,
            IReadOnlyList<WarRefugeeActiveMember> pMembers)
        {
            if (!IsLivingActor(pLeader) || pLeader.current_tile == null) return;
            for (int i = 0; i < pMembers.Count; i++)
            {
                if (pMembers[i].IsLeader) continue;
                Actor follower = ResolveActor(pMembers[i].ActorId);
                if (!IsLivingActor(follower) ||
                    Distance(follower.current_tile, pLeader.current_tile) <= 16)
                    continue;
                try { AWArmyMarchService.TryStepFollowerDirect(follower,
                    pLeader.current_tile); }
                catch { }
            }
        }

        private static void SettleJourney(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeActiveMember> pMembers, City pDestination,
            int pMonthKey)
        {
            var actors = new List<Actor>(pMembers.Count);
            var previousCities = new List<City>(pMembers.Count);
            var origins = new List<WarRefugeeOriginSnapshot>(pMembers.Count);
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor actor = ResolveActor(pMembers[i].ActorId);
                if (!IsLivingActor(actor)) return;
                actors.Add(actor);
                previousCities.Add(actor.city);
                origins.Add(new WarRefugeeOriginSnapshot
                {
                    ActorId = actor.data.id, JourneyId = pJourney.JourneyId,
                    OriginKingdomId = pJourney.OriginKingdomId,
                    OriginCityId = pJourney.OriginCityId,
                    OriginCulture = pMembers[i].OriginCulture,
                    SettledYear = pMonthKey / 12
                });
            }
            int joined = 0;
            for (int i = 0; i < actors.Count; i++)
            {
                int attempted = joined;
                attempted++;
                joined = attempted;
                try
                {
                    actors[i].cancelAllBeh();
                    actors[i].joinCity(pDestination);
                    if (actors[i].city != pDestination) throw new InvalidOperationException();
                }
                catch { RollbackJoinedActors(actors, previousCities, attempted); return; }
            }
            pJourney.State = pJourney.State == WarRefugeeJourneyState.Returning
                ? WarRefugeeJourneyState.Settled : WarRefugeeJourneyState.Arrived;
            pJourney.ArrivalYear = pMonthKey;
            pJourney.NextRetryMonth = -1;
            pJourney.ReservedCapacity = 0;
            bool release = pJourney.State == WarRefugeeJourneyState.Settled;
            if (!WarRefugeePersistence.TryCommitHouseholdArrival(pDb, pJourney,
                origins, release))
            {
                RollbackJoinedActors(actors, previousCities, actors.Count);
                return;
            }
            for (int i = 0; i < actors.Count; i++)
            {
                if (release)
                    ClearActorJourneyKeys(actors[i], pJourney.State);
                else
                    try { actors[i].data.set(LineageKeys.WAR_REFUGEE_STATE,
                        (int)pJourney.State); }
                    catch { }
            }
        }

        private static void RollbackJoinedActors(IReadOnlyList<Actor> pActors,
            IReadOnlyList<City> pPreviousCities, int pJoined)
        {
            for (int i = Math.Min(pJoined, pActors.Count) - 1; i >= 0; i--)
                try { if (IsLivingCity(pPreviousCities[i]))
                    pActors[i].joinCity(pPreviousCities[i]); }
                catch { }
        }

        private static WarRefugeeLifecycleResult ProcessArrivedLifecycle(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, int pMonthKey)
        {
            WarRefugeeJourneySnapshot transition = CopyJourney(pJourney);
            City origin = ResolveCity(transition.OriginCityId);
            bool originExists = IsLivingCity(origin);
            bool originSafe = originExists && !HasDirectThreat(origin);
            transition.ConsecutiveDangerMonths = originSafe ? 0 :
                transition.ConsecutiveDangerMonths + 1;
            transition.SafeMonths = originSafe ? transition.SafeMonths + 1 : 0;
            int monthsSinceArrival = Math.Max(0,
                pMonthKey - transition.ArrivalYear);
            if (WarRefugeeRules.ShouldSettleAfterOriginLoss(originExists,
                    monthsSinceArrival) ||
                WarRefugeeRules.ShouldSettleAfterProlongedDanger(originSafe,
                    transition.ConsecutiveDangerMonths))
            {
                return TransitionTerminal(pDb, pJourney,
                        WarRefugeeJourneyState.Settled)
                    ? WarRefugeeLifecycleResult.Transitioned
                    : WarRefugeeLifecycleResult.PersistenceFailed;
            }
            bool preferReturn = BuildReturnPreference(pDb, pJourney, origin,
                pMonthKey);
            WarRefugeeJourneyState decision = WarRefugeeRules.PostArrivalDecision(
                originSafe, transition.SafeMonths,
                preferReturn,
                true);
            if (decision == WarRefugeeJourneyState.Returning && originSafe)
            {
                int batchSize = WarRefugeePersistence.CountActiveMembers(pDb,
                    transition.JourneyId);
                WarRefugeeDestinationFacts returnFacts = BuildDestinationFacts(
                    origin, ResolveKingdom(transition.DestinationKingdomId),
                    ResolveCity(transition.DestinationCityId), 0);
                if (!WarRefugeeRules.CanReceive(returnFacts, batchSize))
                {
                    if (!WarRefugeePersistence.UpsertJourney(pDb, transition))
                        return WarRefugeeLifecycleResult.PersistenceFailed;
                    ApplyJourneySnapshot(pJourney, transition);
                    return WarRefugeeLifecycleResult.RemainsArrived;
                }
                long hostKingdom = transition.DestinationKingdomId;
                long hostCity = transition.DestinationCityId;
                transition.DestinationKingdomId = transition.OriginKingdomId;
                transition.DestinationCityId = transition.OriginCityId;
                transition.OriginKingdomId = hostKingdom;
                transition.OriginCityId = hostCity;
                transition.State = WarRefugeeJourneyState.Returning;
                transition.ReservedCapacity = batchSize;
                transition.RouteRetries = 0;
                transition.LastDistance = int.MaxValue;
                transition.NextRetryMonth = -1;
                bool crossSea = !SameIsland(origin.getTile(),
                    ResolveCity(hostCity)?.getTile());
                bool reachable = false;
                try { reachable = origin.reachableFrom(ResolveCity(hostCity)); }
                catch { }
                transition.ArrivalYear = WarRefugeeRules.ShouldUseAbstractJourney(
                    crossSea, reachable, 0)
                    ? WarRefugeeRules.AbstractArrivalMonth(pMonthKey,
                        Distance(origin.getTile(), ResolveCity(hostCity)?.getTile()))
                    : -1;
            }
            else if (decision == WarRefugeeJourneyState.Settled)
            {
                return TransitionTerminal(pDb, pJourney,
                        WarRefugeeJourneyState.Settled)
                    ? WarRefugeeLifecycleResult.Transitioned
                    : WarRefugeeLifecycleResult.PersistenceFailed;
            }
            if (!WarRefugeePersistence.UpsertJourney(pDb, transition))
                return WarRefugeeLifecycleResult.PersistenceFailed;
            ApplyJourneySnapshot(pJourney, transition);
            return decision == WarRefugeeJourneyState.Returning
                ? WarRefugeeLifecycleResult.Transitioned
                : WarRefugeeLifecycleResult.RemainsArrived;
        }

        private static WarRefugeeJourneySnapshot CopyJourney(
            WarRefugeeJourneySnapshot pSource)
        {
            return new WarRefugeeJourneySnapshot
            {
                JourneyId = pSource.JourneyId,
                OriginKingdomId = pSource.OriginKingdomId,
                OriginCityId = pSource.OriginCityId,
                DestinationKingdomId = pSource.DestinationKingdomId,
                DestinationCityId = pSource.DestinationCityId,
                State = pSource.State,
                DepartureYear = pSource.DepartureYear,
                ArrivalYear = pSource.ArrivalYear,
                ReservedCapacity = pSource.ReservedCapacity,
                SafeMonths = pSource.SafeMonths,
                LastAssimilationYear = pSource.LastAssimilationYear,
                RouteRetries = pSource.RouteRetries,
                LastDistance = pSource.LastDistance,
                ConsecutiveDangerMonths = pSource.ConsecutiveDangerMonths,
                NextRetryMonth = pSource.NextRetryMonth
            };
        }

        private static void ApplyJourneySnapshot(
            WarRefugeeJourneySnapshot pTarget,
            WarRefugeeJourneySnapshot pSource)
        {
            pTarget.JourneyId = pSource.JourneyId;
            pTarget.OriginKingdomId = pSource.OriginKingdomId;
            pTarget.OriginCityId = pSource.OriginCityId;
            pTarget.DestinationKingdomId = pSource.DestinationKingdomId;
            pTarget.DestinationCityId = pSource.DestinationCityId;
            pTarget.State = pSource.State;
            pTarget.DepartureYear = pSource.DepartureYear;
            pTarget.ArrivalYear = pSource.ArrivalYear;
            pTarget.ReservedCapacity = pSource.ReservedCapacity;
            pTarget.SafeMonths = pSource.SafeMonths;
            pTarget.LastAssimilationYear = pSource.LastAssimilationYear;
            pTarget.RouteRetries = pSource.RouteRetries;
            pTarget.LastDistance = pSource.LastDistance;
            pTarget.ConsecutiveDangerMonths = pSource.ConsecutiveDangerMonths;
            pTarget.NextRetryMonth = pSource.NextRetryMonth;
        }

        private static bool BuildReturnPreference(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, City pOrigin, int pMonthKey)
        {
            City host = ResolveCity(pJourney.DestinationCityId);
            IReadOnlyList<WarRefugeeActiveMember> members =
                WarRefugeePersistence.LoadActiveMembers(pDb, pJourney.JourneyId,
                    HouseholdLimit);
            int relatives = 0;
            bool localMarriage = false;
            bool hostBornChildren = false;
            bool established = false;
            for (int i = 0; i < members.Count; i++)
            {
                Actor actor = ResolveActor(members[i].ActorId);
                if (!IsLivingActor(actor)) continue;
                if (actor.lover?.city == pOrigin) relatives++;
                if (actor.lover?.city == host) localMarriage = true;
                if (HasLocalChild(actor, host)) hostBornChildren = true;
                if (actor.isCityLeader() || GeneralService.IsGeneral(actor) ||
                    CourtService.CaptureRuntimeOfficerProjection(actor) != null)
                    established = true;
                foreach (Actor child in actor.getChildren(false))
                    if (child?.city == pOrigin) relatives++;
            }
            var facts = new WarRefugeeReturnFacts(
                originSafe: IsLivingCity(pOrigin) && !HasDirectThreat(pOrigin),
                originProsperity: SafeFood(pOrigin) + SafeCapacity(pOrigin, 0),
                hostSafe: IsLivingCity(host) && !HasDirectThreat(host),
                hostProsperity: SafeFood(host) + SafeCapacity(host, 0),
                relativesAtOrigin: relatives,
                residenceYears: Math.Max(0,
                    (pMonthKey - pJourney.ArrivalYear) / 12),
                localMarriage: localMarriage,
                hostBornChildren: hostBornChildren,
                establishedLivelihood: established);
            return WarRefugeeRules.PreferReturn(facts, pJourney.JourneyId,
                pMonthKey);
        }

        private static bool TransitionTerminal(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, WarRefugeeJourneyState pState)
        {
            IReadOnlyList<WarRefugeeActiveMember> pMembers =
                WarRefugeePersistence.LoadActiveMembers(pDb,
                    pJourney.JourneyId, HouseholdLimit);
            if (!WarRefugeePersistence.TryTransitionHouseholdTerminalAtomic(
                pDb, pJourney.JourneyId, pState)) return false;
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor actor = ResolveActor(pMembers[i].ActorId);
                if (actor?.data == null) continue;
                ClearActorJourneyKeys(actor, pState);
            }
            pJourney.State = pState;
            pJourney.ReservedCapacity = 0;
            return true;
        }

        private static void ClearActorJourneyKeys(Actor pActor,
            WarRefugeeJourneyState pState)
        {
            if (pActor?.data == null) return;
            try { pActor.data.set(LineageKeys.WAR_REFUGEE_JOURNEY_ID, -1L); }
            catch { }
            try { pActor.data.set(LineageKeys.WAR_REFUGEE_STATE, (int)pState); }
            catch { }
        }

        private static void CancelJourney(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney)
        {
            TransitionTerminal(pDb, pJourney,
                WarRefugeeJourneyState.Cancelled);
        }

        private static void DeferDestinationRetry(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, int pMonthKey)
        {
            pJourney.NextRetryMonth = pMonthKey + 1;
            pJourney.ArrivalYear = -1;
            pJourney.RouteRetries = Math.Min(3, pJourney.RouteRetries + 1);
            WarRefugeePersistence.UpsertJourney(pDb, pJourney);
        }

        private static void HandleUnexpectedCityChange(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, Actor pActor, City pCity)
        {
            if (pJourney == null || pActor?.data == null) return;
            City destination = ResolveCity(pJourney.DestinationCityId);
            if (pCity == destination) return;
            if (!CanReceiveDestination(destination, pJourney) &&
                !TryRerankDestination(pDb, pJourney))
                DeferDestinationRetry(pDb, pJourney, ResolveMonthKey());
            pActor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                (int)pJourney.State);
        }

        private static bool TryRerankDestination(
            System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney)
        {
            City origin = ResolveCity(pJourney.OriginCityId);
            Kingdom owner = ResolveKingdom(pJourney.OriginKingdomId);
            int batchSize = WarRefugeePersistence.CountActiveMembers(pDb,
                pJourney.JourneyId);
            if (!IsLivingCity(origin) || !IsLivingKingdom(owner) ||
                batchSize <= 0) return false;
            City replacement = SelectDestination(origin, owner, batchSize);
            if (!IsLivingCity(replacement)) return false;
            pJourney.DestinationCityId = replacement.id;
            pJourney.DestinationKingdomId = replacement.kingdom.id;
            pJourney.ReservedCapacity = batchSize;
            pJourney.RouteRetries = 0;
            pJourney.LastDistance = int.MaxValue;
            pJourney.NextRetryMonth = -1;
            bool crossSea = !SameIsland(origin.getTile(), replacement.getTile());
            bool reachable = false;
            try { reachable = origin.reachableFrom(replacement); }
            catch { }
            pJourney.ArrivalYear = WarRefugeeRules.ShouldUseAbstractJourney(
                crossSea, reachable, 0)
                ? WarRefugeeRules.AbstractArrivalMonth(_month,
                    Distance(origin.getTile(), replacement.getTile()))
                : -1;
            return WarRefugeePersistence.UpsertJourney(pDb, pJourney);
        }

        private static void RecoverActiveJourneys(
            System.Data.SQLite.SQLiteConnection pDb)
        {
            var activeActorIds = new HashSet<long>();
            long cursor = -1L;
            while (true)
            {
                IReadOnlyList<WarRefugeeJourneySnapshot> journeys =
                    WarRefugeePersistence.LoadActiveJourneys(pDb, 128, cursor);
                if (journeys.Count == 0) break;
                for (int i = 0; i < journeys.Count; i++)
                {
                    WarRefugeeJourneySnapshot journey = journeys[i];
                    cursor = journey.JourneyId;
                    IReadOnlyList<WarRefugeeActiveMember> members =
                        WarRefugeePersistence.LoadActiveMembers(pDb,
                            journey.JourneyId, HouseholdLimit);
                    Actor leader = null;
                    int living = 0;
                    for (int j = 0; j < members.Count; j++)
                    {
                        Actor actor = ResolveActor(members[j].ActorId);
                        if (!IsLivingActor(actor))
                        {
                            WarRefugeePersistence.SetMemberActive(pDb,
                                journey.JourneyId, members[j].ActorId, false);
                            continue;
                        }
                        activeActorIds.Add(members[j].ActorId);
                        living++;
                        actor.data.set(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                            journey.JourneyId);
                        actor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                            (int)journey.State);
                        if (leader == null && actor.isAdult()) leader = actor;
                    }
                    if (living == 0) { CancelJourney(pDb, journey); continue; }
                    journey.ReservedCapacity = living;
                    if (leader != null)
                        WarRefugeePersistence.SetMemberLeader(pDb,
                            journey.JourneyId, leader.data.id);
                    else if (journey.State == WarRefugeeJourneyState.Traveling ||
                             journey.State == WarRefugeeJourneyState.Returning)
                    {
                        journey.ArrivalYear = WarRefugeeRules.AbstractArrivalMonth(
                            Math.Max(1, ResolveMonthKey()), 1);
                    }
                    WarRefugeePersistence.UpsertJourney(pDb, journey);
                }
                if (journeys.Count < 128) break;
            }
            ReconcileStaleActorJourneyKeys(activeActorIds);
        }

        private static void ReconcileStaleActorJourneyKeys(
            ISet<long> pActiveActorIds)
        {
            if (World.world?.units == null) return;
            try
            {
                foreach (Actor actor in World.world.units)
                {
                    if (actor?.data == null) continue;
                    actor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                        out long journeyId, -1L);
                    if (journeyId < 0L || pActiveActorIds.Contains(actor.data.id))
                        continue;
                    ClearActorJourneyKeys(actor,
                        WarRefugeeJourneyState.Cancelled);
                }
            }
            catch { }
        }

        private static void ProcessAssimilation(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, int pYear)
        {
            Kingdom host = ResolveKingdom(pJourney.DestinationKingdomId);
            City city = ResolveCity(pJourney.DestinationCityId);
            if (!IsLivingKingdom(host) || !IsLivingCity(city) ||
                city.kingdom != host || !WarRefugeeRules.IsHostNonXia(
                    XiaizationService.IsNativePolicyKingdom(host),
                    XiaizationService.GetLevel(host) >=
                        XiaizationService.LevelXiaizedDynasty))
                return;
            IReadOnlyList<WarRefugeeActiveMember> members =
                WarRefugeePersistence.LoadActiveMembers(pDb,
                    pJourney.JourneyId, HouseholdLimit);
            for (int i = 0; i < members.Count; i++)
            {
                Actor actor = ResolveActor(members[i].ActorId);
                if (!IsLivingActor(actor)) continue;
                actor.data.get(LineageKeys.WAR_REFUGEE_LAST_ASSIMILATION_YEAR,
                    out int lastYear, -1);
                if (!WarRefugeeRules.CanEvaluateAssimilation(true,
                    Math.Max(0, pYear - pJourney.ArrivalYear / 12), lastYear,
                    pYear)) continue;
                int chance = WarRefugeeRules.AssimilationPermille(
                    Math.Max(0, pYear - pJourney.ArrivalYear / 12),
                    actor.lover?.culture == city.culture,
                    HasLocalChild(actor, city), false);
                actor.data.set(LineageKeys.WAR_REFUGEE_LAST_ASSIMILATION_YEAR,
                    pYear);
                if (WarRefugeeRules.StableChance(actor.data.id, pYear, chance))
                {
                    try { actor.tryToConvertToCulture(city.culture); }
                    catch { }
                }
            }
        }

        private static List<Actor> CollectHousehold(City pCity,
            Kingdom pOwner)
        {
            var result = new List<Actor>();
            try
            {
                Actor seed = null;
                foreach (Actor actor in pCity.units)
                {
                    if (!IsLivingActor(actor) || !IsEligible(actor, pOwner)) continue;
                    seed = actor;
                    break;
                }
                if (seed == null) return result;
                var ids = new HashSet<long>();
                AddHouseholdMember(result, ids, seed, pCity, pOwner);
                Actor spouse = seed.lover;
                if (spouse?.lover == seed)
                    AddHouseholdMember(result, ids, spouse, pCity, pOwner);
                foreach (Actor child in seed.getChildren(false))
                {
                    if (child?.isAdult() == true) continue;
                    AddHouseholdMember(result, ids, child, pCity, pOwner);
                    if (result.Count >= HouseholdLimit) break;
                }
            }
            catch { }
            return result;
        }

        private static void AddHouseholdMember(List<Actor> pResult,
            HashSet<long> pIds, Actor pActor, City pCity, Kingdom pOwner)
        {
            if (pResult.Count >= HouseholdLimit || pActor?.data == null ||
                pActor.city != pCity || !IsEligible(pActor, pOwner) ||
                !pIds.Add(pActor.data.id)) return;
            pResult.Add(pActor);
        }

        private static bool IsEligible(Actor pActor, Kingdom pOwner)
        {
            long heirId = -1L;
            pOwner?.data?.get(LineageKeys.KINGDOM_HEIR_ID, out heirId, -1L);
            if (pActor.kingdom != pOwner || pActor.isKing() || pActor.isCityLeader() ||
                pActor.data.id == heirId ||
                pActor.isWarrior() || GeneralService.IsGeneral(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                CourtService.CaptureRuntimeOfficerProjection(pActor) != null)
                return false;
            return !HasActiveJourney(pActor);
        }

        private static City SelectDestination(City pOrigin, Kingdom pOwner,
            int pBatchSize)
        {
            City best = null;
            WarRefugeeDestinationFacts bestFacts = default;
            bool hasBest = false;
            try
            {
                if (!DestinationCityIdsByKingdom.TryGetValue(pOwner.id,
                        out HashSet<long> indexedCities)) return null;
                foreach (long candidateId in indexedCities)
                {
                    City candidate = ResolveCity(candidateId);
                    if (!IsLivingCity(candidate) || candidate == pOrigin ||
                        candidate.kingdom == null ||
                        !DestinationOwnerByCity.TryGetValue(candidateId,
                            out long indexedOwnerId) ||
                        indexedOwnerId != pOwner.id ||
                        candidate.kingdom.id != pOwner.id) continue;
                    WarRefugeeRelation relation;
                    bool enemy;
                    try { enemy = candidate.kingdom.isEnemy(pOwner); }
                    catch { enemy = true; }
                    if (enemy) relation = WarRefugeeRelation.Enemy;
                    else if (candidate.kingdom == pOwner)
                        relation = WarRefugeeRelation.Domestic;
                    else if (VassalService.GetDiplomaticSuzerain(candidate.kingdom) == pOwner ||
                             VassalService.GetDiplomaticSuzerain(pOwner) == candidate.kingdom ||
                             TributaryProtectionService.IsProtectedPair(pOwner,
                                 candidate.kingdom) || AreAllied(pOwner,
                                     candidate.kingdom))
                        relation = WarRefugeeRelation.ProtectedPartner;
                    else relation = WarRefugeeRelation.Neutral;
                    var facts = new WarRefugeeDestinationFacts(candidate.id,
                        true, ThreatenedCityIds.Contains(candidate.id),
                        false, IsCityDangerous(candidate), SafeFood(candidate),
                        SafeHousing(candidate), SafeCapacity(candidate, 0,
                            _monthlyReservations), relation,
                        Distance(pOrigin.getTile(), candidate.getTile()),
                        IsFamine(candidate));
                    if (!WarRefugeeRules.CanReceive(facts, pBatchSize) ||
                        !WarRefugeeRules.AcceptForeignHost(relation, facts.Food,
                            facts.Capacity, pBatchSize, pOrigin.id, candidate.id))
                        continue;
                    if (!hasBest || WarRefugeeRules.CompareDestinations(
                            facts, bestFacts) < 0)
                    {
                        best = candidate;
                        bestFacts = facts;
                        hasBest = true;
                    }
                }
            }
            catch { }
            return best;
        }

        private static bool CanReceiveDestination(City pDestination,
            WarRefugeeJourneySnapshot pJourney)
        {
            if (!IsLivingCity(pDestination) || pDestination.kingdom == null)
                return false;
            return WarRefugeeRules.CanReceive(BuildDestinationFacts(pDestination,
                ResolveKingdom(pJourney.OriginKingdomId),
                ResolveCity(pJourney.OriginCityId), pJourney.ReservedCapacity),
                Math.Max(1, pJourney.ReservedCapacity));
        }

        private static WarRefugeeDestinationFacts BuildDestinationFacts(
            City pCity, Kingdom pOwner, City pOrigin, int pOwnReservation = 0)
        {
            bool enemy;
            try { enemy = pCity.kingdom.isEnemy(pOwner); }
            catch { enemy = true; }
            WarRefugeeRelation relation = enemy ? WarRefugeeRelation.Enemy :
                pCity.kingdom == pOwner ? WarRefugeeRelation.Domestic :
                VassalService.GetDiplomaticSuzerain(pCity.kingdom) == pOwner ||
                VassalService.GetDiplomaticSuzerain(pOwner) == pCity.kingdom ||
                TributaryProtectionService.IsProtectedPair(pOwner, pCity.kingdom) ||
                AreAllied(pOwner, pCity.kingdom)
                    ? WarRefugeeRelation.ProtectedPartner : WarRefugeeRelation.Neutral;
            return new WarRefugeeDestinationFacts(pCity.id, true,
                ThreatenedCityIds.Contains(pCity.id), false,
                IsCityDangerous(pCity),
                SafeFood(pCity), SafeHousing(pCity),
                SafeCapacity(pCity, pOwnReservation), relation,
                Distance(pOrigin?.getTile(), pCity.getTile()), IsFamine(pCity));
        }

        private static bool AreAllied(Kingdom pLeft, Kingdom pRight)
        {
            try { return pLeft?.data != null && pRight?.data != null &&
                Alliance.isSame(pLeft.getAlliance(), pRight.getAlliance()); }
            catch { return false; }
        }

        private static bool HasDirectThreat(City pCity)
        {
            return pCity?.data != null && ThreatenedCityIds.Contains(pCity.id);
        }

        private static bool IsCityDangerous(City pCity)
        {
            return pCity?.data != null && ThreatenedCityIds.Contains(pCity.id);
        }

        private static void BuildDestinationSnapshot()
        {
            DestinationCityIdsByKingdom.Clear();
            DestinationOwnerByCity.Clear();
            try
            {
                foreach (City city in World.world.cities)
                {
                    long cityId = city?.data?.id ?? -1L;
                    long kingdomId = city?.kingdom?.data?.id ?? -1L;
                    if (cityId >= 0L && kingdomId >= 0L)
                        AddDestination(cityId, kingdomId);
                }
                _destinationSnapshotInitialized = true;
            }
            catch { }
        }

        private static void RefreshMonthlyReservations()
        {
            try
            {
                var loaded = WarRefugeePersistence.LoadActiveReservations(
                    LineageArchiveManager.Instance?.OperatingDB);
                var snapshot = new Dictionary<long, int>();
                if (loaded != null)
                    foreach (KeyValuePair<long, int> pair in loaded)
                        snapshot[pair.Key] = pair.Value;
                _monthlyReservations = snapshot;
            }
            catch
            {
                _monthlyReservations = new Dictionary<long, int>();
            }
        }

        private static void AddMonthlyReservation(long pCityId, int pCount)
        {
            if (pCityId < 0L || pCount <= 0) return;
            _monthlyReservations.TryGetValue(pCityId, out int current);
            _monthlyReservations[pCityId] = current + pCount;
        }

        private static void AddDestination(long pCityId, long pKingdomId)
        {
            if (pCityId < 0L || pKingdomId < 0L) return;
            if (!DestinationCityIdsByKingdom.TryGetValue(pKingdomId,
                    out HashSet<long> ids))
            {
                ids = new HashSet<long>();
                DestinationCityIdsByKingdom[pKingdomId] = ids;
            }
            ids.Add(pCityId);
            DestinationOwnerByCity[pCityId] = pKingdomId;
        }

        private static void RemoveDestination(long pCityId, long pKingdomId)
        {
            if (pCityId < 0L) return;
            long indexedKingdomId = -1L;
            DestinationOwnerByCity.TryGetValue(pCityId,
                out indexedKingdomId);
            RemoveDestinationFromKingdom(pCityId, indexedKingdomId);
            if (pKingdomId != indexedKingdomId)
                RemoveDestinationFromKingdom(pCityId, pKingdomId);
            DestinationOwnerByCity.Remove(pCityId);
        }

        private static void RemoveDestinationFromKingdom(long pCityId,
            long pKingdomId)
        {
            if (pKingdomId >= 0L && DestinationCityIdsByKingdom.TryGetValue(
                    pKingdomId, out HashSet<long> ids))
            {
                ids.Remove(pCityId);
                if (ids.Count == 0) DestinationCityIdsByKingdom.Remove(pKingdomId);
            }
        }

        private static bool HasAnyWar(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && World.world.wars.hasWars(pKingdom); }
            catch { return false; }
        }

        private static bool HasLocalChild(Actor pActor, City pCity)
        {
            try
            {
                foreach (Actor child in pActor.getChildren(false))
                    if (child?.city == pCity) return true;
            }
            catch { }
            return false;
        }

        private static void MoveActor(Actor pActor, WorldTile pTile)
        {
            if (!IsLivingActor(pActor) || pTile == null) return;
            try { pActor.goTo(pTile, pLimitPathfindingRegions: 6); }
            catch { }
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int SafeFood(City pCity)
        {
            try { return Math.Max(0, pCity?.countFoodTotal() ?? 0); }
            catch { return 0; }
        }

        private static int SafeHousing(City pCity)
        {
            try { return Math.Max(0, (pCity?.buildings?.Count ?? 0) * 8); }
            catch { return 0; }
        }

        private static int SafeCapacity(City pCity, int pOwnReservation,
            IReadOnlyDictionary<long, int> pReservations = null)
        {
            try
            {
                int reserved;
                if (pReservations != null)
                    pReservations.TryGetValue(pCity.id, out reserved);
                else
                    reserved = WarRefugeePersistence.CountActiveReservations(
                        LineageArchiveManager.Instance?.OperatingDB, pCity.id);
                return WarRefugeeRules.ReadableSpareCapacity(
                    SafeHousing(pCity), SafePopulation(pCity), reserved,
                    pOwnReservation);
            }
            catch { return 0; }
        }

        private static bool IsFamine(City pCity)
        {
            int population = SafePopulation(pCity);
            try { return population > 0 &&
                Math.Max(0, pCity.status?.hungry ?? 0) >=
                    Math.Max(5, population / 3); }
            catch { return true; }
        }

        private static int Distance(WorldTile pLeft, WorldTile pRight)
        {
            if (pLeft == null || pRight == null) return 0;
            long dx = pLeft.x - pRight.x;
            long dy = pLeft.y - pRight.y;
            long value = dx * dx + dy * dy;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static bool SameIsland(WorldTile pLeft, WorldTile pRight)
        {
            try { return pLeft?.region?.island != null &&
                pRight?.region?.island != null &&
                ReferenceEquals(pLeft.region.island, pRight.region.island); }
            catch { return false; }
        }

        private static Actor ResolveActor(long pId)
        {
            try { return pId >= 0 ? World.world?.units?.get(pId) : null; }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId >= 0 ? World.world?.kingdoms?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }

        private static bool IsLivingActor(Actor pActor)
        {
            try { return pActor?.data != null && pActor.isAlive() && !pActor.isRekt(); }
            catch { return false; }
        }

        private static bool IsLivingCity(City pCity)
        {
            try { return pCity?.data != null && !pCity.isRekt(); }
            catch { return false; }
        }

        private static bool IsLivingKingdom(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && !pKingdom.isRekt() && pKingdom.isCiv(); }
            catch { return false; }
        }

        private static bool CanMutate()
        {
            return !AW3MultiplayerReplicaScope.IsReplicaSession &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
