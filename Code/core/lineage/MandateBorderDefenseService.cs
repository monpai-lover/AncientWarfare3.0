using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.policy;
using AncientWarfare3.core.schools;
using AncientWarfare3.utils;
using ai.behaviours;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderDefenseService
    {
        private const int YEARLY_GUARD_CAP = 12;
        private const int WAR_GUARD_CAP = 20;
        private const int YEARLY_TOWER_CAP = 1;
        private const int WAR_TOWER_CAP = 2;
        private const int MAX_BORDER_ARMIES = 3;
        private const int MAX_BORDER_GUARDS_PER_CITY = 10;
        private const int BORDER_THREAT_RADIUS = 55;
        private const float BORDER_NO_THREAT_WAIT_MIN = 4f;
        private const float BORDER_NO_THREAT_WAIT_MAX = 8f;
        private const float BORDER_PATROL_WAIT_MIN = 5f;
        private const float BORDER_PATROL_WAIT_MAX = 10f;
        private static readonly System.Random Rng = new System.Random();

        private sealed class BorderResult
        {
            public int guards;
            public int walls;
            public int towers;
            public City main_city;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            // 年度自动整备已改为天朝决议槽推进，保留空钩子避免旧 patch 调用报错。
        }

        public static bool ExecuteDecision(Kingdom pMandate)
        {
            if (ReinforceBorder(pMandate, YEARLY_GUARD_CAP,
                    true, YEARLY_TOWER_CAP, "decision"))
                return true;
            return CityEconomyService.TryGetLatestCachedForeignLandBorder(
                       pMandate, out bool hasBorder) && hasBorder;
        }

        public static void OnMandateWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;
            MandateReport report = MandateService.ReadReport();
            if (!MandateWarDefenseRules.ShouldActivate(
                    report.active, report.kingdom_id, attacker.id, defender.id)) return;
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null) return;

            ReinforceBorder(mandate, WAR_GUARD_CAP, true,
                WAR_TOWER_CAP, "war");
        }

        private static bool ReinforceBorder(Kingdom pMandate,
            int pGuardCap, bool pBuildWalls, int pTowerCap, string pReason)
        {
            if (pMandate?.data == null) return false;
            pGuardCap = LimitedGuardCap(pMandate, pGuardCap);
            List<City> cities = CollectBorderCities(pMandate);
            if (cities.Count == 0) return false;
            cities = SelectBorderArmyCities(cities);
            ReanchorBorderArmies(pMandate, cities);

            var result = new BorderResult();
            foreach (City city in cities)
            {
                if (result.guards < pGuardCap)
                    result.guards += AppointBorderGuards(city, pMandate, pGuardCap - result.guards);
                if (result.towers < pTowerCap)
                    result.towers += BuildBorderTowers(city, pMandate, pTowerCap - result.towers);
                if (pBuildWalls)
                    result.walls += BuildBorderWalls(city);
                if (result.main_city == null && (result.guards > 0 || result.walls > 0 || result.towers > 0))
                    result.main_city = city;
            }

            if (result.guards <= 0 && result.walls <= 0 && result.towers <= 0) return false;

            string text = " \u6574\u5907\u8FB9\u9632";
            if (result.guards > 0) text += "\uFF0C\u62BD\u8C03\u8FB9\u519B " + result.guards + " \u540D";
            if (result.towers > 0) text += "\uFF0C\u4FEE\u7B51\u7BAD\u5854 " + result.towers + " \u5EA7";
            if (result.walls > 0) text += "\uFF0C\u4FEE\u7B51\u8FB9\u5899 " + result.walls + " \u6BB5";
            if (pReason == "war") text += "\uFF0C\u6218\u65F6\u52A8\u5458";

            HistoryWriter.RecordKingdom(pMandate, KingdomEvent.MANDATE_BORDER_DEFENSE,
                HistoryText.Kingdom(pMandate) + HistoryText.PlainText(text),
                result.main_city == null ? HistoryTarget.Kingdom(pMandate) : HistoryTarget.City(result.main_city));
            if (result.main_city?.data != null)
                HistoryWriter.RecordCity(result.main_city, pMandate, CityEvent.MANDATE_BORDER_DEFENSE,
                    HistoryText.City(result.main_city, pMandate) + HistoryText.PlainText(text),
                    HistoryTarget.Kingdom(pMandate));

            MandateService.RecordMandateEvent("mandate_border_defense", pMandate, pMandate.king, result.main_city,
                1, MandateService.ReadReport().mandate_value, (pMandate.name ?? "") + text);
            return true;
        }

        private static int LimitedGuardCap(Kingdom pMandate, int pRequestedCap)
        {
            int eligible = CountEligibleGuards(pMandate);
            if (eligible < 30) return 0;
            int percentCap = Mathf.Max(2, Mathf.FloorToInt(eligible * 0.15f));
            if (eligible < 60) percentCap = Mathf.Min(percentCap, 5);
            return Mathf.Clamp(percentCap, 0, pRequestedCap);
        }

        private static int CountEligibleGuards(Kingdom pMandate)
        {
            if (pMandate?.data == null) return 0;
            int count = 0;
            count += CountEligibleGuardsInKingdom(pMandate);
            foreach (Kingdom vassal in VassalService.GetVassals(pMandate, true))
                count += CountEligibleGuardsInKingdom(vassal);
            return count;
        }

        private static int CountEligibleGuardsInKingdom(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int count = 0;
            foreach (Actor unit in pKingdom.getUnits())
                if (CanBeBorderGuard(unit, pKingdom)) count++;
            return count;
        }

        private static List<City> CollectBorderCities(Kingdom pMandate)
        {
            var result = new List<City>();
            foreach (long id in MandateService.GetCurrentCoreCityIds())
            {
                City city = FindCity(id);
                if (city?.data == null || city.isRekt()) continue;
                Kingdom owner = city.kingdom;
                if (owner?.data == null) continue;
                if (owner != pMandate && VassalService.GetRootSuzerain(owner) != pMandate) continue;
                if (!HasOutsideNeighbour(city, pMandate)) continue;
                result.Add(city);
            }

            result.Sort((a, b) => BorderScore(b, pMandate).CompareTo(BorderScore(a, pMandate)));
            return result;
        }

        private static List<City> SelectBorderArmyCities(List<City> pCities)
        {
            if (pCities.Count <= MAX_BORDER_ARMIES) return pCities;
            return pCities.GetRange(0, MAX_BORDER_ARMIES);
        }

        private static void ReanchorBorderArmies(Kingdom pMandate, List<City> pBorderCities)
        {
            if (pMandate?.data == null || pBorderCities == null) return;
            var allowedOwners = new HashSet<long> { pMandate.id };
            foreach (Kingdom vassal in VassalService.GetVassals(pMandate, true))
                if (vassal?.data != null) allowedOwners.Add(vassal.id);
            BorderArmyReanchorService.ReanchorExistingArmies(pBorderCities, allowedOwners,
                city => PickBorderTile(city, pMandate), PrepareExistingBorderActor);
        }

        private static void PrepareExistingBorderActor(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.MANDATE_BORDER_GUARD, true);
            AssignBorderGuardJob(pActor);
        }

        private static List<Army> GetMandateBorderArmies(Kingdom pMandate)
        {
            var result = new List<Army>();
            if (World.world?.armies == null || pMandate?.data == null) return result;

            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive()) continue;
                if (!AWArmyService.IsRoleArmy(army, AWArmyRole.BorderArmy)) continue;
                Kingdom owner = SafeArmyKingdom(army);
                if (owner?.data == null) continue;
                if (owner == pMandate || VassalService.GetRootSuzerain(owner) == pMandate)
                    result.Add(army);
            }
            return result;
        }

        private static Army PickReusableBorderArmy(List<Army> pArmies, HashSet<long> pUsed, Kingdom pOwner)
        {
            foreach (Army army in pArmies)
            {
                if (army?.data == null || pUsed.Contains(army.id)) continue;
                if (SafeArmyKingdom(army) != pOwner) continue;
                return army;
            }
            return null;
        }

        private static void ReleaseBorderArmy(Army pArmy)
        {
            if (pArmy?.data == null) return;
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                var units = new List<Actor>();
                try
                {
                    foreach (Actor unit in pArmy.getUnits())
                        if (unit?.data != null && !unit.isRekt())
                            units.Add(unit);
                }
                catch { }

                foreach (Actor unit in units)
                {
                    unit.data.set(LineageKeys.MANDATE_BORDER_GUARD, false);
                    try { unit.removeFromArmy(); }
                    catch { unit.setArmy(null); }
                }

                try { pArmy.setCaptain(null); } catch { }
                try { World.world?.armies?.removeObject(pArmy); } catch { }
            }
        }

        private static bool HasOutsideNeighbour(City pCity, Kingdom pMandate)
        {
            try
            {
                pCity.recalculateNeighbourZones();
                pCity.recalculateNeighbourCities();
                foreach (City other in pCity.neighbours_cities)
                {
                    Kingdom kingdom = other?.kingdom;
                    if (kingdom?.data == null || kingdom.isNeutral()) continue;
                    if (kingdom == pMandate || VassalService.GetRootSuzerain(kingdom) == pMandate) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static float BorderScore(City pCity, Kingdom pMandate)
        {
            float score = 0f;
            try { score += pCity.getPopulationPeople() * 0.1f + pCity.countZones() * 0.5f; } catch { }
            try
            {
                foreach (City other in pCity.neighbours_cities)
                {
                    Kingdom kingdom = other?.kingdom;
                    if (kingdom?.data == null) continue;
                    if (kingdom == pMandate || VassalService.GetRootSuzerain(kingdom) == pMandate) continue;
                    score += pMandate.isEnemy(kingdom) ? 20f : 8f;
                    if (!LineageService.IsXiaKingdom(kingdom)) score += 8f;
                }
            }
            catch { }
            return score;
        }

        private static int AppointBorderGuards(City pCity, Kingdom pMandate, int pCap)
        {
            if (pCity?.data == null || pCap <= 0) return 0;
            Kingdom owner = pCity.kingdom;
            if (owner?.data == null) return 0;

            int existing = CountBorderGuards(pCity);
            int need = Mathf.Clamp(MAX_BORDER_GUARDS_PER_CITY - existing, 0, pCap);
            if (need <= 0) return 0;

            var candidates = new List<Actor>();
            foreach (Actor unit in pCity.getUnits())
                if (CanBeBorderGuard(unit, owner)) candidates.Add(unit);
            if (candidates.Count < need)
            {
                foreach (Actor unit in owner.getUnits())
                {
                    if (candidates.Contains(unit)) continue;
                    if (CanBeBorderGuard(unit, owner)) candidates.Add(unit);
                }
            }

            candidates.Sort((a, b) => CombatScore(b).CompareTo(CombatScore(a)));
            if (candidates.Count == 0) return 0;

            Actor captain = candidates[0];
            Army borderArmy = AWArmyService.EnsureArmy(owner, pCity, captain, AWArmyRole.BorderArmy,
                BuildBorderArmyName(owner, pCity), pDetached: false);
            WorldTile patrol = PickBorderTile(pCity, pMandate);
            int changed = 0;
            foreach (Actor actor in candidates)
            {
                if (changed >= need) break;
                actor.data.set(LineageKeys.MANDATE_BORDER_GUARD, true);
                if (!actor.isWarrior())
                {
                    try { actor.setProfession(UnitProfession.Warrior); } catch { }
                }
                if (borderArmy != null)
                    AWArmyService.AddToArmy(actor, borderArmy);
                AssignBorderGuardJob(actor);
                if (ArmyRtsRuntimeModeRules.
                        ShouldUseLegacyArmyFollowerOrders(
                            ArmyRtsRuntimeMode.Current) &&
                    patrol != null && actor.current_tile != null &&
                    actor.current_tile.isSameIsland(patrol))
                {
                    try { actor.goTo(patrol); } catch { }
                }
                changed++;
            }
            if (borderArmy?.data != null && patrol != null)
                ArmyRtsControllerService.TrySetFormationAnchor(borderArmy,
                    patrol);
            if (changed > 0 && borderArmy?.data != null)
                WarNoticeService.OnArmyChanged(owner, borderArmy);
            return changed;
        }

        private static void MoveBorderArmyToPatrol(Army pArmy, City pCity, Kingdom pMandate)
        {
            if (pArmy?.data == null || pCity?.data == null) return;
            WorldTile patrol = PickBorderTile(pCity, pMandate);
            if (patrol == null) return;
            bool useLegacyFollowerOrders = ArmyRtsRuntimeModeRules.
                ShouldUseLegacyArmyFollowerOrders(
                    ArmyRtsRuntimeMode.Current);
            if (!useLegacyFollowerOrders)
                ArmyRtsControllerService.TrySetFormationAnchor(pArmy,
                    patrol);
            try
            {
                foreach (Actor actor in pArmy.getUnits())
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    actor.data.set(LineageKeys.MANDATE_BORDER_GUARD, true);
                    AssignBorderGuardJob(actor);
                    if (useLegacyFollowerOrders &&
                        actor.current_tile != null &&
                        actor.current_tile.isSameIsland(patrol))
                        actor.goTo(patrol);
                }
            }
            catch { }
        }

        private static void AssignBorderGuardJob(Actor pActor)
        {
            if (!ArmyRtsRuntimeModeRules.ShouldUseLegacyArmyFollowerOrders(
                    ArmyRtsRuntimeMode.Current) || pActor?.data == null ||
                BorderGuardContent.ACTOR_JOB_BORDER_GUARD == null) return;
            try { pActor.ai.setJob(BorderGuardContent.ACTOR_JOB_BORDER_GUARD); }
            catch { }
        }

        private static string BuildBorderArmyName(Kingdom pKingdom, City pCity)
        {
            string name = AWArmyRoleRules.DisplayName(AWArmyRole.BorderArmy, pKingdom?.name ?? "", 1);
            string cityName = pCity?.data?.name;
            return string.IsNullOrEmpty(cityName) ? name : cityName + " " + name;
        }

        private static int BuildBorderWalls(City pCity)
        {
            TopTileType wall = ResolveBorderWallType();
            if (pCity?.data == null || wall == null) return 0;
            return CultiwayStyleCityWallService.Build(
                pCity, wall, 2, true).Changed;
        }

        private static TopTileType ResolveBorderWallType()
        {
            TopTileType preferred = TopTileLibrary.wall_order;
            if (preferred != null && preferred.id == MandateBorderWallRules.PreferredWallTopTileId)
                return preferred;
            return preferred ?? TopTileLibrary.wall_ancient ?? TopTileLibrary.wall_iron;
        }

        private static int BuildBorderTowers(City pCity, Kingdom pMandate, int pCap)
        {
            if (pCity?.data == null || pCap <= 0) return 0;
            if (pCity.countBuildingsType("type_watch_tower", pCountOnlyFinished: false) >= 3) return 0;

            BuildingAsset tower = GetWatchTowerAsset(pCity);
            if (tower == null) return 0;

            int built = 0;
            try
            {
                Building building = CityBehBuild.tryToBuild(pCity, tower);
                if (building != null) built++;
            }
            catch { }
            if (built >= pCap) return built;

            foreach (WorldTile tile in GetBorderBuildCandidates(pCity, pMandate))
            {
                if (built >= pCap) break;
                try
                {
                    if (!World.world.buildings.canBuildFrom(tile, tower, pCity)) continue;
                    Building building = World.world.buildings.addBuilding(tower, tile, pCheckForBuild: false);
                    if (building == null) continue;
                    building.setKingdom(pCity.kingdom);
                    built++;
                }
                catch (Exception e)
                {
                    ModClass.LogWarning("Mandate border tower failed: " + e.Message);
                }
            }
            return built;
        }

        private static BuildingAsset GetWatchTowerAsset(City pCity)
        {
            try
            {
                BuildingAsset tower = pCity.getActorAsset()?.architecture_asset?.getBuilding("order_watch_tower");
                if (tower != null) return tower;
            }
            catch { }

            return AssetManager.buildings.get("watch_tower_Xia") ??
                   AssetManager.buildings.get("watch_tower_human");
        }

        private static List<WorldTile> GetBorderBuildCandidates(City pCity, Kingdom pMandate)
        {
            var candidates = new List<WorldTile>();
            try
            {
                pCity.recalculateNeighbourZones();
                foreach (TileZone zone in pCity.border_zones)
                {
                    if (zone?.tiles == null) continue;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (tile == null || !IsInsideCity(tile, pCity)) continue;
                        if (!TouchesExternalLandBorder(tile, pMandate)) continue;
                        candidates.Add(tile);
                    }
                }
            }
            catch { }
            return candidates;
        }

        private static bool IsInsideCity(WorldTile pTile, City pCity)
        {
            try
            {
                if (pTile?.zone_city == pCity) return true;
                return pTile?.zone?.city == pCity;
            }
            catch { return false; }
        }

        private static bool TouchesExternalLandBorder(WorldTile pTile, Kingdom pMandate)
        {
            try
            {
                foreach (WorldTile n in pTile.neighboursAll)
                {
                    if (n == null || n.Type == null) continue;
                    City city = null;
                    try { city = n.zone_city; } catch { city = null; }
                    Kingdom kingdom = city?.kingdom;
                    bool sameMandateSystem = kingdom != null &&
                        (kingdom == pMandate || VassalService.GetRootSuzerain(kingdom) == pMandate);
                    if (MandateBorderWallRules.IsExternalLandBorderNeighbor(
                            pNeighborHasCity: city?.data != null && kingdom?.data != null,
                            pNeighborGround: n.Type.ground,
                            pNeighborLiquid: n.Type.liquid,
                            pNeighborLava: n.Type.lava,
                            pNeighborBlock: n.Type.block,
                            pNeighborNeutral: kingdom == null || kingdom.isNeutral(),
                            pSameMandateSystem: sameMandateSystem))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static WorldTile PickBorderTile(City pCity, Kingdom pMandate)
        {
            try
            {
                pCity.recalculateNeighbourZones();
                foreach (TileZone zone in pCity.border_zones)
                foreach (WorldTile tile in zone.tiles)
                    if (tile != null && tile.Type != null && tile.Type.ground &&
                        TouchesExternalLandBorder(tile, pMandate))
                        return tile;
            }
            catch { }
            return pCity.getTile();
        }

        public static bool IsBorderGuard(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            pActor.data.get(LineageKeys.MANDATE_BORDER_GUARD, out bool flag, false);
            if (flag) return true;
            return AWArmyService.IsRoleArmy(pActor.army, AWArmyRole.BorderArmy);
        }

        public static void ReleaseBorderGuard(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.MANDATE_BORDER_GUARD, false);
        }

        public static Actor FindThreatNearBorderGuard(Actor pActor)
        {
            if (!IsBorderGuard(pActor)) return null;
            Kingdom kingdom = pActor.kingdom;
            if (kingdom?.data == null) return null;
            try
            {
                if (!kingdom.hasEnemies()) return null;
            }
            catch { return null; }

            WorldTile origin = pActor.current_tile ?? GetBorderGuardPatrolTile(pActor);
            if (origin == null) return null;

            Actor best = null;
            int bestDist = int.MaxValue;
            int maxDist = BORDER_THREAT_RADIUS * BORDER_THREAT_RADIUS;
            using ListPool<Kingdom> enemies = kingdom.getEnemiesKingdoms();
            foreach (Kingdom enemy in enemies)
            {
                if (enemy?.data == null) continue;
                foreach (Actor target in enemy.getUnits())
                {
                    if (!IsValidThreatForBorderGuard(pActor, target)) continue;
                    int dist = Toolbox.SquaredDistTile(origin, target.current_tile);
                    if (dist > maxDist || dist >= bestDist) continue;
                    bestDist = dist;
                    best = target;
                }
            }
            return best;
        }

        public static bool IsValidThreatForBorderGuard(Actor pGuard, Actor pTarget)
        {
            if (!IsBorderGuard(pGuard)) return false;
            if (pTarget?.data == null || pTarget.isRekt() || !pTarget.isAlive()) return false;
            if (pGuard.kingdom?.data == null || pTarget.kingdom?.data == null) return false;
            if (pGuard.kingdom == pTarget.kingdom || !pGuard.kingdom.isEnemy(pTarget.kingdom)) return false;
            if (pGuard.current_tile == null || pTarget.current_tile == null) return false;
            return pGuard.current_tile.isSameIsland(pTarget.current_tile);
        }

        public static WorldTile GetBorderGuardPatrolTile(Actor pActor)
        {
            if (!IsBorderGuard(pActor)) return null;
            City city = AWArmyService.FindAnchorCity(pActor.army) ?? pActor.city;
            if (city?.data == null) return pActor.city?.getTile();
            Kingdom mandate = VassalService.GetRootSuzerain(city.kingdom) ?? city.kingdom;
            WorldTile tile = PickBorderTile(city, mandate);
            if (tile == null) return city.getTile();
            if (pActor.current_tile != null && !pActor.current_tile.isSameIsland(tile))
                return city.getTile();
            return tile;
        }

        public static void WaitAfterBorderGuardNoThreat(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(BORDER_NO_THREAT_WAIT_MIN, BORDER_NO_THREAT_WAIT_MAX));
        }

        public static void WaitAfterBorderGuardPatrol(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.makeWait(RandomWait(BORDER_PATROL_WAIT_MIN, BORDER_PATROL_WAIT_MAX));
        }

        private static bool CanBeBorderGuard(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (!HistoricalMasterVocationService.CanEnter(pActor,
                    HistoricalMasterMilitaryContext.BorderArmy)) return false;
            bool canonicalMaster = HistoricalSchoolDescentService.IsCanonicalMaster(pActor);
            if (pActor.kingdom != pKingdom || pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.asset?.is_boat == true) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor) || MandateRebelService.IsRebelLeader(pActor)) return false;
            if (!canonicalMaster && (pActor.hasTrait("figure") || pActor.hasTrait("first")))
                return false;
            return pActor.isWarrior() || pActor.isUnitFitToRule();
        }

        private static int CountBorderGuards(City pCity)
        {
            var seen = new HashSet<long>();
            int count = 0;
            Army borderArmy = AWArmyService.FindArmy(pCity?.kingdom, pCity, AWArmyRole.BorderArmy);
            if (borderArmy != null)
            {
                foreach (Actor unit in borderArmy.getUnits())
                {
                    if (unit?.data == null || unit.isRekt()) continue;
                    unit.data.get(LineageKeys.MANDATE_BORDER_GUARD, out bool flag, false);
                    if (!flag) continue;
                    seen.Add(unit.data.id);
                    count++;
                }
            }

            foreach (Actor unit in pCity.getUnits())
            {
                if (unit?.data == null || unit.isRekt()) continue;
                if (seen.Contains(unit.data.id)) continue;
                unit.data.get(LineageKeys.MANDATE_BORDER_GUARD, out bool flag, false);
                if (flag) count++;
            }
            return count;
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage") + SafeStat(pActor, "warfare") * 2f +
                   SafeStat(pActor, "health") * 0.08f + SafeStat(pActor, "armor") * 1.5f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static Kingdom SafeArmyKingdom(Army pArmy)
        {
            try
            {
                Kingdom kingdom = pArmy?.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            return anchor?.kingdom;
        }

        private static float RandomWait(float pMin, float pMax)
        {
            if (pMax < pMin) return pMax;
            return (float)(pMin + Rng.NextDouble() * (pMax - pMin));
        }

        private static City FindCity(long pId)
        {
            if (pId < 0 || World.world?.cities == null) return null;
            try
            {
                City city = World.world.cities.get(pId);
                if (city?.data != null) return city;
            }
            catch { }
            foreach (City city in World.world.cities)
                if (city?.data != null && city.id == pId) return city;
            return null;
        }
    }
}
