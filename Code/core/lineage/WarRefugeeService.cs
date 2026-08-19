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

        internal static void ProcessAuthorityCycle(long pCycleToken,
            bool pPaused)
        {
            if (pPaused || pCycleToken == _lastMonthToken) return;
            _lastMonthToken = pCycleToken;
            int currentMonth = ResolveMonthKey();
            if (currentMonth == _month) return;
            _month = currentMonth;
            if (!CanMutate()) return;
            ProcessPersistedJourneys(currentMonth, JourneyBudgetPerMonth);
            CaptureThreatenedCities(currentMonth, CityBudgetPerMonth);
        }

        internal static void RebuildRuntime()
        {
            Reset();
            try
            {
                LineageArchiveManager archive = LineageArchiveManager.Instance;
                if (archive?.OperatingDB != null)
                    WarRefugeePersistence.EnsureSchema(archive.OperatingDB);
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
                IReadOnlyList<WarRefugeeActiveMember> members =
                    WarRefugeePersistence.LoadActiveMembers(archive.OperatingDB,
                        journeyId, HouseholdLimit);
                for (int i = 0; i < members.Count; i++)
                {
                    Actor candidate = ResolveActor(members[i].ActorId);
                    if (!IsLivingActor(candidate)) continue;
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
                LineageArchiveManager archive = LineageArchiveManager.Instance;
                if (archive?.OperatingDB == null ||
                    !WarRefugeePersistence.TryGetActiveJourneyForActor(
                        archive.OperatingDB, pActor.data.id, out long journeyId) ||
                    !WarRefugeePersistence.TryLoadJourney(archive.OperatingDB,
                        journeyId, out WarRefugeeJourneySnapshot journey) ||
                    journey.DestinationCityId != pCity.id) return;
                journey.State = WarRefugeeJourneyState.Arrived;
                journey.ArrivalYear = ResolveMonthKey();
                WarRefugeePersistence.UpsertJourney(archive.OperatingDB, journey);
                pActor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                    (int)WarRefugeeJourneyState.Arrived);
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
            return journeyId >= 0L;
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
            var cities = new List<City>();
            try { foreach (City value in World.world.cities) cities.Add(value); }
            catch { return; }
            int count = cities.Count;
            if (count <= 0) return;
            int processed = 0;
            while (processed++ < pBudget)
            {
                if (_cityCursor >= count) _cityCursor = 0;
                City city = cities[_cityCursor++];
                if (!IsLivingCity(city) || !HasDirectThreat(city)) continue;
                Kingdom owner = city.kingdom;
                if (!IsLivingKingdom(owner)) continue;
                int population = SafePopulation(city);
                if (population <= 0) continue;
                var eligible = CollectEligibleActors(city, owner);
                if (eligible.Count == 0) continue;
                int hungry = Math.Max(0, city.status?.hungry ?? 0);
                var facts = new WarRefugeeThreatFacts(
                    pNearbyArmy: true,
                    pSiege: city.being_captured_by != null,
                    pCombatOrTransfer: city.being_captured_by != null,
                    pFamine: hungry >= Math.Max(5, population / 3),
                    pActiveWar: HasAnyWar(owner));
                int permille = WarRefugeeRules.DeparturePermille(facts, city.id);
                int cityBudget = Math.Min(8, eligible.Count);
                int quota = WarRefugeeRules.DepartureQuota(population,
                    eligible.Count, Math.Max(1, population / 5), cityBudget,
                    32, permille);
                if (quota <= 0) continue;
                City destination = SelectDestination(city, owner, quota);
                if (!IsLivingCity(destination)) continue;
                CreateJourney(archive.OperatingDB, city, destination, eligible,
                    quota, pMonthKey);
            }
        }

        private static void ProcessPersistedJourneys(int pMonthKey, int pBudget)
        {
            LineageArchiveManager archive = LineageArchiveManager.Instance;
            if (archive?.OperatingDB == null) return;
            IReadOnlyList<WarRefugeeJourneySnapshot> journeys =
                WarRefugeePersistence.LoadActiveJourneys(archive.OperatingDB,
                    pBudget);
            for (int i = 0; i < journeys.Count; i++)
            {
                WarRefugeeJourneySnapshot journey = journeys[i];
                if (journey.State == WarRefugeeJourneyState.Arrived)
                {
                    ProcessAssimilation(archive.OperatingDB, journey,
                        pMonthKey / 12);
                    continue;
                }
                City destination = ResolveCity(journey.DestinationCityId);
                if (!IsLivingCity(destination) || destination.kingdom == null)
                    continue;
                IReadOnlyList<WarRefugeeActiveMember> members =
                    WarRefugeePersistence.LoadActiveMembers(archive.OperatingDB,
                        journey.JourneyId, HouseholdLimit);
                if (members.Count == 0) continue;
                int month = pMonthKey;
                if (journey.ArrivalYear >= 0 && month >= journey.ArrivalYear)
                {
                    SettleJourney(archive.OperatingDB, journey, members,
                        destination, pMonthKey / 12);
                    continue;
                }
                MoveJourneyMembers(archive.OperatingDB, journey, members,
                    destination);
            }
        }

        private static void CreateJourney(System.Data.SQLite.SQLiteConnection pDb,
            City pOrigin, City pDestination, IReadOnlyList<Actor> pCandidates,
            int pQuota, int pMonthKey)
        {
            long id = TableIdAllocator.Next(pDb, "AW_WarRefugeeJourney",
                "JOURNEY_ID");
            if (id < 0L) return;
            int count = Math.Min(Math.Min(HouseholdLimit, pQuota), pCandidates.Count);
            if (count <= 0) return;
            WorldTile originTile = pOrigin.getTile();
            WorldTile destinationTile = pDestination.getTile();
            int distance = Distance(originTile, destinationTile);
            bool crossSea = !SameIsland(originTile, destinationTile);
            bool reachable = false;
            try { reachable = pOrigin.reachableFrom(pDestination); }
            catch { }
            bool abstractJourney = WarRefugeeRules.ShouldUseAbstractJourney(
                crossSea, reachable, 3);
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
            if (!WarRefugeePersistence.UpsertJourney(pDb, journey)) return;
            for (int i = 0; i < count; i++)
            {
                Actor actor = pCandidates[i];
                if (actor?.data == null) continue;
                if (!WarRefugeePersistence.InsertMember(pDb,
                    new WarRefugeeMemberSnapshot
                    {
                        JourneyId = id, ActorId = actor.data.id,
                        IsLeader = i == 0, Active = true,
                        OriginCulture = actor.culture == null
                            ? "" : Convert.ToString(actor.culture.id)
                    })) continue;
                actor.data.set(LineageKeys.WAR_REFUGEE_JOURNEY_ID, id);
                actor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                    (int)WarRefugeeJourneyState.Traveling);
                if (!abstractJourney) MoveActor(actor, destinationTile);
            }
        }

        private static void MoveJourneyMembers(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeActiveMember> pMembers, City pDestination)
        {
            WorldTile tile = pDestination.getTile();
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor actor = ResolveActor(pMembers[i].ActorId);
                if (!IsLivingActor(actor) || actor.city == pDestination) continue;
                MoveActor(actor, tile);
            }
        }

        private static void SettleJourney(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney,
            IReadOnlyList<WarRefugeeActiveMember> pMembers, City pDestination,
            int pYear)
        {
            for (int i = 0; i < pMembers.Count; i++)
            {
                Actor actor = ResolveActor(pMembers[i].ActorId);
                if (!IsLivingActor(actor))
                {
                    WarRefugeePersistence.SetMemberActive(pDb,
                        pJourney.JourneyId, pMembers[i].ActorId, false);
                    continue;
                }
                try
                {
                    actor.cancelAllBeh();
                    actor.joinCity(pDestination);
                }
                catch { continue; }
                actor.data.set(LineageKeys.WAR_REFUGEE_STATE,
                    (int)WarRefugeeJourneyState.Arrived);
                WarRefugeePersistence.InsertOrigin(pDb,
                    new WarRefugeeOriginSnapshot
                    {
                        ActorId = actor.data.id, JourneyId = pJourney.JourneyId,
                        OriginKingdomId = pJourney.OriginKingdomId,
                        OriginCityId = pJourney.OriginCityId,
                        OriginCulture = pMembers[i].OriginCulture,
                        SettledYear = pYear
                    });
            }
            pJourney.State = WarRefugeeJourneyState.Arrived;
            pJourney.ArrivalYear = pJourney.ArrivalYear < 0
                ? pYear * 12 : pJourney.ArrivalYear;
            WarRefugeePersistence.UpsertJourney(pDb, pJourney);
        }

        private static void ProcessAssimilation(System.Data.SQLite.SQLiteConnection pDb,
            WarRefugeeJourneySnapshot pJourney, int pYear)
        {
            Kingdom host = ResolveKingdom(pJourney.DestinationKingdomId);
            City city = ResolveCity(pJourney.DestinationCityId);
            if (!IsLivingKingdom(host) || !IsLivingCity(city) ||
                city.kingdom != host || XiaizationService.IsNativePolicyKingdom(host))
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
                    Math.Max(0, pYear - pJourney.DepartureYear), lastYear,
                    pYear)) continue;
                int chance = WarRefugeeRules.AssimilationPermille(
                    Math.Max(0, pYear - pJourney.DepartureYear),
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

        private static List<Actor> CollectEligibleActors(City pCity,
            Kingdom pOwner)
        {
            var result = new List<Actor>();
            try
            {
                foreach (Actor actor in pCity.units)
                {
                    if (!IsLivingActor(actor) || !IsEligible(actor, pOwner)) continue;
                    result.Add(actor);
                    if (result.Count >= HouseholdLimit) break;
                }
            }
            catch { }
            return result;
        }

        private static bool IsEligible(Actor pActor, Kingdom pOwner)
        {
            if (pActor.kingdom != pOwner || pActor.isKing() || pActor.isCityLeader() ||
                pActor.isWarrior() || GeneralService.IsGeneral(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                CourtService.CaptureRuntimeOfficerProjection(pActor) != null)
                return false;
            pActor.data.get(LineageKeys.WAR_REFUGEE_JOURNEY_ID,
                out long journeyId, -1L);
            return journeyId < 0L;
        }

        private static City SelectDestination(City pOrigin, Kingdom pOwner,
            int pBatchSize)
        {
            City best = null;
            try
            {
                foreach (City candidate in World.world.cities)
                {
                    if (!IsLivingCity(candidate) || candidate == pOrigin ||
                        candidate.kingdom == null) continue;
                    WarRefugeeRelation relation;
                    bool enemy;
                    try { enemy = candidate.kingdom.isEnemy(pOwner); }
                    catch { enemy = true; }
                    if (enemy) relation = WarRefugeeRelation.Enemy;
                    else if (candidate.kingdom == pOwner)
                        relation = WarRefugeeRelation.Domestic;
                    else relation = WarRefugeeRelation.Neutral;
                    var facts = new WarRefugeeDestinationFacts(candidate.id,
                        true, false, false, HasAnyWar(candidate.kingdom),
                        Math.Max(1, SafePopulation(candidate)),
                        Math.Max(1, SafePopulation(candidate)),
                        Math.Max(1, SafePopulation(candidate)), relation,
                        Distance(pOrigin.getTile(), candidate.getTile()));
                    if (!WarRefugeeRules.CanReceive(facts, pBatchSize)) continue;
                    if (best == null || WarRefugeeRules.CompareDestinations(
                        facts, BuildDestinationFacts(best, pOwner, pOrigin)) < 0)
                        best = candidate;
                }
            }
            catch { }
            return best;
        }

        private static WarRefugeeDestinationFacts BuildDestinationFacts(
            City pCity, Kingdom pOwner, City pOrigin)
        {
            bool enemy;
            try { enemy = pCity.kingdom.isEnemy(pOwner); }
            catch { enemy = true; }
            return new WarRefugeeDestinationFacts(pCity.id, true, false,
                false, HasAnyWar(pCity.kingdom), Math.Max(1, SafePopulation(pCity)),
                Math.Max(1, SafePopulation(pCity)), Math.Max(1, SafePopulation(pCity)),
                enemy ? WarRefugeeRelation.Enemy :
                    pCity.kingdom == pOwner ? WarRefugeeRelation.Domestic :
                    WarRefugeeRelation.Neutral,
                Distance(pOrigin.getTile(), pCity.getTile()));
        }

        private static bool HasDirectThreat(City pCity)
        {
            try { return pCity.being_captured_by != null ||
                pCity.target_attack_city != null || pCity.target_attack_zone != null; }
            catch { return false; }
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
