using System;
using System.Collections.Generic;
using AncientWarfare3.utils;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class MandateBorderDefenseService
    {
        private const int YEARLY_GUARD_CAP = 12;
        private const int WAR_GUARD_CAP = 20;
        private const int YEARLY_WALL_CAP = 8;
        private const int WAR_WALL_CAP = 12;
        private const int MAX_BORDER_GUARDS_PER_CITY = 10;

        private sealed class BorderResult
        {
            public int guards;
            public int walls;
            public City main_city;
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            // 年度自动整备已改为天朝决议槽推进，保留空钩子避免旧 patch 调用报错。
        }

        public static bool ExecuteDecision(Kingdom pMandate)
        {
            return ReinforceBorder(pMandate, YEARLY_GUARD_CAP, YEARLY_WALL_CAP, "decision");
        }

        public static void OnMandateWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom mandate = MandateService.GetCurrentMandateKingdom();
            if (mandate?.data == null) return;

            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null) return;
            if (defender != mandate && attacker != mandate) return;

            ReinforceBorder(mandate, WAR_GUARD_CAP, WAR_WALL_CAP, "war");
        }

        private static bool ReinforceBorder(Kingdom pMandate, int pGuardCap, int pWallCap, string pReason)
        {
            if (pMandate?.data == null) return false;
            pGuardCap = LimitedGuardCap(pMandate, pGuardCap);
            List<City> cities = CollectBorderCities(pMandate);
            if (cities.Count == 0) return false;

            var result = new BorderResult();
            foreach (City city in cities)
            {
                if (result.guards < pGuardCap)
                    result.guards += AppointBorderGuards(city, pMandate, pGuardCap - result.guards);
                if (result.walls < pWallCap)
                    result.walls += BuildBorderWalls(city, pMandate, pWallCap - result.walls);
                if (result.main_city == null && (result.guards > 0 || result.walls > 0))
                    result.main_city = city;
                if (result.guards >= pGuardCap && result.walls >= pWallCap) break;
            }

            if (result.guards <= 0 && result.walls <= 0) return false;

            string text = " \u6574\u5907\u8FB9\u9632";
            if (result.guards > 0) text += "\uFF0C\u62BD\u8C03\u8FB9\u519B " + result.guards + " \u540D";
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
                BuildBorderArmyName(owner, pCity), pDetached: true);
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
                if (patrol != null && actor.current_tile != null && actor.current_tile.isSameIsland(patrol))
                {
                    try { actor.goTo(patrol); } catch { }
                }
                changed++;
            }
            return changed;
        }

        private static string BuildBorderArmyName(Kingdom pKingdom, City pCity)
        {
            string name = AWArmyRoleRules.DisplayName(AWArmyRole.BorderArmy, pKingdom?.name ?? "", 1);
            string cityName = pCity?.data?.name;
            return string.IsNullOrEmpty(cityName) ? name : cityName + " " + name;
        }

        private static int BuildBorderWalls(City pCity, Kingdom pMandate, int pCap)
        {
            if (pCity?.data == null || pCap <= 0 || TopTileLibrary.wall_iron == null) return 0;

            var candidates = new List<WorldTile>();
            try
            {
                pCity.recalculateNeighbourZones();
                foreach (TileZone zone in pCity.border_zones)
                {
                    if (zone?.tiles == null) continue;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (!IsWallCandidate(tile, pMandate)) continue;
                        candidates.Add(tile);
                    }
                }
            }
            catch { }

            int built = 0;
            foreach (WorldTile tile in candidates)
            {
                if (built >= pCap) break;
                try
                {
                    tile.setTopTileType(TopTileLibrary.wall_iron);
                    built++;
                }
                catch (Exception e)
                {
                    ModClass.LogWarning("Mandate border wall failed: " + e.Message);
                }
            }
            return built;
        }

        private static bool IsWallCandidate(WorldTile pTile, Kingdom pMandate)
        {
            if (pTile == null || pTile.zone == null || pTile.Type == null) return false;
            if (!pTile.Type.ground || pTile.Type.liquid || pTile.Type.lava || pTile.Type.block) return false;
            if (pTile.Type.wall || pTile.Type.road || pTile.top_type != null) return false;
            if (pTile.hasBuilding()) return false;
            return TouchesOutsideCity(pTile, pMandate);
        }

        private static bool TouchesOutsideCity(WorldTile pTile, Kingdom pMandate)
        {
            try
            {
                foreach (WorldTile n in pTile.neighboursAll)
                {
                    Kingdom kingdom = n?.zone_city?.kingdom;
                    if (kingdom?.data == null || kingdom.isNeutral()) continue;
                    if (kingdom == pMandate || VassalService.GetRootSuzerain(kingdom) == pMandate) continue;
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
                    if (tile != null && tile.Type != null && tile.Type.ground && TouchesOutsideCity(tile, pMandate))
                        return tile;
            }
            catch { }
            return pCity.getTile();
        }

        private static bool CanBeBorderGuard(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pKingdom?.data == null) return false;
            if (pActor.kingdom != pKingdom || pActor.isRekt() || !pActor.isAdult()) return false;
            if (pActor.asset?.is_boat == true) return false;
            if (pActor.isKing() || pActor.isCityLeader()) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (SlaveService.IsSlave(pActor) || SlaveService.IsRetiredSoldier(pActor)) return false;
            if (RoyalGuardService.IsRoyalGuard(pActor) || MandateRebelService.IsRebelLeader(pActor)) return false;
            if (pActor.hasTrait("figure") || pActor.hasTrait("first")) return false;
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
