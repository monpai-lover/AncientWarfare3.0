using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    /// <summary>
    /// 战争难民。
    ///
    /// 旧实现自建了 journey / member / origin 三张 SQLite 表和一套旅程状态机
    /// (Traveling/Returning/Arrived、抽象到达年、目的地重排重试、领队选举、
    /// 跨海判定、按月推进物理移动),1300 余行,每月光"推进旅程"就要 22~60ms,
    /// 还得靠游标切片摊到多个权威周期才不压帧。
    ///
    /// 但这件事原版本来就会做:joinCity 一次调用即完成国籍(内部转 joinKingdom)、
    /// 家园与人口统计,之后原版 AI 的 getHomeBuilding / BehBuildingTargetHome
    /// 会自己驱使 actor 走回新家。于是这里只保留"谁走、去哪"的判定
    /// (WarRefugeeRules 那套围城/饥荒/战争概率与配额规则原样保留),落户改成
    /// 一次 joinCity,不再有任何需要按月推进的状态。
    ///
    /// 同化也从"按月扫描"改成事件驱动:母国战争结束时判定一次 —— 原籍城市
    /// 还活着则按概率回归(不同化),否则留下并完成文化同化。状态存在
    /// actor.data 上,不再需要表。
    /// </summary>
    internal static class WarRefugeeService
    {
        // 每月最多考察几座受威胁城市。判定本身很便宜,真正贵的是落户,
        // 而落户现在是一次 joinCity。
        private const int CityBudgetPerMonth = 6;
        private const int HouseholdLimit = 8;
        // 原籍尚存时的回归概率(千分比)。停留越久越不想走,按停留年数递减。
        private const int ReturnBasePermille = 700;
        private const int ReturnDecayPermillePerYear = 100;
        private const int ReturnMinimumPermille = 150;

        private static long _lastMonthToken = long.MinValue;
        private static int _month;
        private static int _cityCursor;
        private static readonly HashSet<long> ThreatenedCityIds =
            new HashSet<long>();
        private static readonly Dictionary<long, HashSet<long>>
            DestinationCityIdsByKingdom =
                new Dictionary<long, HashSet<long>>();
        private static readonly Dictionary<long, long> DestinationOwnerByCity =
            new Dictionary<long, long>();
        private static Dictionary<long, int> _monthlyReservations =
            new Dictionary<long, int>();
        private static bool _destinationSnapshotInitialized;

        internal static void ProcessAuthorityCycle(long pCycleToken,
            bool pPaused)
        {
            if (pPaused || pCycleToken == _lastMonthToken) return;
            _lastMonthToken = pCycleToken;
            int currentMonth = ResolveMonthKey();
            if (currentMonth == _month) return;
            _month = currentMonth;
            if (!CanMutate()) return;
            // 每月只做一件事:考察受威胁城市、把该走的人就地落户到目的地。
            // 没有"在途"概念,所以没有需要推进的东西。
            AWAuthorityCycleService.SubStep(
                AWAuthorityCycleService.AuthorityStep.RefugeeMonthlyReservations,
                RefreshMonthlyReservations);
            AWAuthorityCycleService.SubStep(
                AWAuthorityCycleService.AuthorityStep.RefugeeThreatenedCities,
                () => CaptureThreatenedCities(currentMonth,
                    CityBudgetPerMonth));
        }

        internal static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || _destinationSnapshotInitialized) return;
            BuildDestinationSnapshot();
        }

        /// <summary>
        /// 母国战争结束:对该国出身的难民做一次性判定。原籍城市还活着则按概率
        /// 回归原籍(不同化),否则留在避难地并完成同化。这是同化的唯一入口 ——
        /// 不再有按月推进的计时。
        /// </summary>
        internal static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null || !CanMutate()) return;
            try
            {
                ResolveRefugeesForEndedWar(pWar);
            }
            catch { }
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
                BuildDestinationSnapshot();
            }
            catch { }
        }

        internal static void OnActorDying(Actor pActor)
        {
            // 难民身份只是 actor.data 上的几个键,随 actor 一起消失,
            // 没有需要回收的外部记录。
            _ = pActor;
        }

        internal static void OnActorJoinedCity(Actor pActor, City pCity)
        {
            // 落户由本服务主动发起,原版 joinCity 不再需要回调对账。保留空实现
            // 是为了让 Actor.joinCity 的补丁点继续存在(其它系统也挂在上面)。
            _ = pActor;
            _ = pCity;
        }

        internal static void OnActorBorn(Actor pBaby, Actor pParent1,
            Actor pParent2)
        {
            if (pBaby?.data == null || pBaby.city?.culture == null) return;
            try
            {
                if (IsUnresolvedRefugee(pParent1) ||
                    IsUnresolvedRefugee(pParent2))
                    pBaby.setCulture(pBaby.city.culture);
            }
            catch { }
        }

        internal static void Reset()
        {
            _lastMonthToken = long.MinValue;
            _month = 0;
            _cityCursor = 0;
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
            if (World.world?.cities == null || pBudget <= 0) return;
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
                int settled = SettleHousehold(city, destination, eligible,
                    quota, pMonthKey);
                AddMonthlyReservation(destination.id, settled);
                remainingWorldBudget -= Math.Max(0, settled);
            }
        }

        /// <summary>
        /// 直接落户:joinCity 一次搞定国籍、家园与人口统计,之后原版 AI 自己
        /// 驱使 actor 走回新家。旧实现在这里要建行程、选领队、算抵达年、按月
        /// 推进移动 —— 全部不再需要。
        /// </summary>
        private static int SettleHousehold(City pOrigin, City pDestination,
            IReadOnlyList<Actor> pCandidates, int pQuota, int pMonthKey)
        {
            int count = Math.Min(Math.Min(HouseholdLimit, pQuota),
                pCandidates.Count);
            if (count <= 0) return 0;
            long originKingdomId = pOrigin.kingdom?.data?.id ?? -1L;
            int settled = 0;
            for (int i = 0; i < count; i++)
            {
                Actor actor = pCandidates[i];
                if (!IsLivingActor(actor)) continue;
                try
                {
                    // 记下出身,供战争结束时判定是否回归。
                    actor.data.set(LineageKeys.WAR_REFUGEE_ORIGIN_KINGDOM_ID,
                        originKingdomId);
                    actor.data.set(LineageKeys.WAR_REFUGEE_ORIGIN_CITY_ID,
                        pOrigin.id);
                    actor.data.set(LineageKeys.WAR_REFUGEE_DEPARTURE_MONTH,
                        pMonthKey);
                    actor.joinCity(pDestination);
                    settled++;
                }
                catch { }
            }
            return settled;
        }

        private static void ResolveRefugeesForEndedWar(War pWar)
        {
            if (World.world?.units == null) return;
            int currentMonth = ResolveMonthKey();
            var pending = new List<Actor>();
            foreach (Actor actor in World.world.units)
            {
                if (!IsUnresolvedRefugee(actor)) continue;
                actor.data.get(LineageKeys.WAR_REFUGEE_ORIGIN_KINGDOM_ID,
                    out long originKingdomId, -1L);
                if (originKingdomId < 0L) continue;
                Kingdom origin = ResolveKingdom(originKingdomId);
                if (origin == null || !IsWarParticipant(pWar, origin)) continue;
                pending.Add(actor);
            }

            for (int i = 0; i < pending.Count; i++)
                ResolveRefugee(pending[i], currentMonth);
        }

        private static void ResolveRefugee(Actor pActor, int pCurrentMonth)
        {
            try
            {
                pActor.data.get(LineageKeys.WAR_REFUGEE_ORIGIN_CITY_ID,
                    out long originCityId, -1L);
                City originCity = ResolveCity(originCityId);
                bool canReturn = IsLivingCity(originCity) &&
                                 IsLivingKingdom(originCity.kingdom) &&
                                 !ThreatenedCityIds.Contains(originCityId);
                if (canReturn && RollReturn(pActor, pCurrentMonth))
                {
                    // 回原籍:不同化,身份记录一并清掉。
                    pActor.joinCity(originCity);
                    ClearRefugeeKeys(pActor);
                    return;
                }
                // 留下:完成文化同化,身份记录清掉,此后不再参与判定。
                City host = pActor.city;
                if (host?.culture != null) pActor.setCulture(host.culture);
                ClearRefugeeKeys(pActor);
            }
            catch { }
        }

        private static bool RollReturn(Actor pActor, int pCurrentMonth)
        {
            pActor.data.get(LineageKeys.WAR_REFUGEE_DEPARTURE_MONTH,
                out int departureMonth, -1);
            int years = departureMonth < 0
                ? 0
                : Math.Max(0, (pCurrentMonth - departureMonth) / 12);
            int permille = Math.Max(ReturnMinimumPermille,
                ReturnBasePermille - years * ReturnDecayPermillePerYear);
            return WarRefugeeRules.StableChance(pActor.data.id,
                pCurrentMonth, permille);
        }

        private static bool IsUnresolvedRefugee(Actor pActor)
        {
            if (pActor?.data == null || !IsLivingActor(pActor)) return false;
            pActor.data.get(LineageKeys.WAR_REFUGEE_ORIGIN_KINGDOM_ID,
                out long originKingdomId, -1L);
            return originKingdomId >= 0L;
        }

        private static void ClearRefugeeKeys(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.WAR_REFUGEE_ORIGIN_KINGDOM_ID, -1L);
            pActor.data.set(LineageKeys.WAR_REFUGEE_ORIGIN_CITY_ID, -1L);
            pActor.data.set(LineageKeys.WAR_REFUGEE_DEPARTURE_MONTH, -1);
        }

        private static bool IsWarParticipant(War pWar, Kingdom pKingdom)
        {
            try { return pWar.hasKingdom(pKingdom); }
            catch { return false; }
        }

        private static List<Actor> CollectHousehold(City pCity, Kingdom pOwner)
        {
            var result = new List<Actor>();
            try
            {
                Actor seed = null;
                foreach (Actor actor in pCity.units)
                {
                    if (!IsLivingActor(actor) ||
                        !IsEligible(actor, pOwner)) continue;
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
            if (!IsLivingActor(pActor) || pActor.city != pCity ||
                !IsEligible(pActor, pOwner) ||
                !pIds.Add(pActor.data.id)) return;
            if (pResult.Count < HouseholdLimit) pResult.Add(pActor);
        }

        private static bool IsEligible(Actor pActor, Kingdom pOwner)
        {
            long heirId = -1L;
            pOwner?.data?.get(LineageKeys.KINGDOM_HEIR_ID, out heirId, -1L);
            if (pActor.kingdom != pOwner || pActor.isKing() ||
                pActor.isCityLeader() ||
                pActor.data.id == heirId ||
                pActor.isWarrior() || GeneralService.IsGeneral(pActor) ||
                RoyalGuardService.IsRoyalGuard(pActor) ||
                CourtService.CaptureRuntimeOfficerProjection(pActor) != null)
                return false;
            // 已经是待判定难民的人不再重复迁出。
            return !IsUnresolvedRefugee(pActor);
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
                    else if (VassalService.GetDiplomaticSuzerain(
                                 candidate.kingdom) == pOwner ||
                             VassalService.GetDiplomaticSuzerain(pOwner) ==
                                 candidate.kingdom ||
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
                    if (!WarRefugeeRules.CanReceive(facts, pBatchSize))
                        continue;
                    if (!hasBest ||
                        WarRefugeeRules.CompareDestinations(facts,
                            bestFacts) < 0)
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

        private static bool AreAllied(Kingdom pLeft, Kingdom pRight)
        {
            try { return pLeft?.data != null && pRight?.data != null &&
                Alliance.isSame(pLeft.getAlliance(), pRight.getAlliance()); }
            catch { return false; }
        }

        private static bool HasDirectThreat(City pCity)
        {
            return ThreatenedCityIds.Contains(pCity.id);
        }

        private static bool IsCityDangerous(City pCity)
        {
            return ThreatenedCityIds.Contains(pCity.id);
        }

        private static void BuildDestinationSnapshot()
        {
            DestinationCityIdsByKingdom.Clear();
            DestinationOwnerByCity.Clear();
            try
            {
                if (World.world?.cities == null) return;
                foreach (City city in World.world.cities)
                {
                    if (!IsLivingCity(city) || city.kingdom?.data == null)
                        continue;
                    AddDestination(city.id, city.kingdom.id);
                }
                _destinationSnapshotInitialized = true;
            }
            catch { }
        }

        private static void RefreshMonthlyReservations()
        {
            // 落户即时完成,不再有"已预约但未到达"的量,每月清零即可。
            _monthlyReservations = new Dictionary<long, int>();
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
                    out HashSet<long> cities))
            {
                cities = new HashSet<long>();
                DestinationCityIdsByKingdom[pKingdomId] = cities;
            }
            cities.Add(pCityId);
            DestinationOwnerByCity[pCityId] = pKingdomId;
        }

        private static void RemoveDestination(long pCityId, long pKingdomId)
        {
            if (pCityId < 0L) return;
            if (pKingdomId >= 0L)
                RemoveDestinationFromKingdom(pCityId, pKingdomId);
            if (DestinationOwnerByCity.TryGetValue(pCityId,
                    out long indexedKingdomId) &&
                indexedKingdomId != pKingdomId)
                RemoveDestinationFromKingdom(pCityId, indexedKingdomId);
            DestinationOwnerByCity.Remove(pCityId);
        }

        private static void RemoveDestinationFromKingdom(long pCityId,
            long pKingdomId)
        {
            if (!DestinationCityIdsByKingdom.TryGetValue(pKingdomId,
                    out HashSet<long> cities)) return;
            cities.Remove(pCityId);
            if (cities.Count == 0)
                DestinationCityIdsByKingdom.Remove(pKingdomId);
        }

        private static bool HasAnyWar(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null &&
                World.world.wars.hasWars(pKingdom); }
            catch { return false; }
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
            Dictionary<long, int> pReservations)
        {
            try
            {
                int reserved = 0;
                if (pReservations != null && pCity != null)
                    pReservations.TryGetValue(pCity.id, out reserved);
                return WarRefugeeRules.ReadableSpareCapacity(
                    SafeHousing(pCity), SafePopulation(pCity), reserved,
                    pOwnReservation);
            }
            catch { return 0; }
        }

        private static bool IsFamine(City pCity)
        {
            try
            {
                int hungry = Math.Max(0, pCity?.status?.hungry ?? 0);
                int population = SafePopulation(pCity);
                return hungry >= Math.Max(5, population / 3);
            }
            catch { return false; }
        }

        private static int Distance(WorldTile pLeft, WorldTile pRight)
        {
            try
            {
                if (pLeft?.data == null || pRight?.data == null) return int.MaxValue;
                int dx = pLeft.x - pRight.x;
                int dy = pLeft.y - pRight.y;
                return (int)Math.Sqrt(dx * dx + dy * dy);
            }
            catch { return int.MaxValue; }
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return pId < 0L ? null : World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId < 0L ? null : World.world?.cities?.get(pId); }
            catch { return null; }
        }

        private static bool IsLivingActor(Actor pActor)
        {
            try { return pActor?.data != null && !pActor.isRekt() && pActor.isAlive(); }
            catch { return false; }
        }

        private static bool IsLivingCity(City pCity)
        {
            try { return pCity?.data != null && !pCity.isRekt(); }
            catch { return false; }
        }

        private static bool IsLivingKingdom(Kingdom pKingdom)
        {
            try { return pKingdom?.data != null && !pKingdom.isRekt() &&
                pKingdom.isCiv(); }
            catch { return false; }
        }

        private static bool CanMutate()
        {
            return !AW3MultiplayerReplicaScope.IsReplicaSession &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
