using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class RoyalExpansionDecisionService
    {
        private const int MAX_MOVED_SETTLERS = 6;

        public static bool CanExecute(Kingdom pKingdom)
        {
            return TryFindPlan(pKingdom, out _, out _, out _);
        }

        public static bool Execute(Kingdom pKingdom)
        {
            if (!TryFindPlan(pKingdom, out Actor founder, out City sourceCity, out TileZone zone)) return false;

            City newCity = World.world?.cities?.buildNewCity(founder, zone);
            if (newCity?.data == null) return false;

            founder.stopBeingWarrior();
            founder.joinCity(newCity);
            newCity.setLeader(founder, pNew: true);
            MoveSettlers(sourceCity, newCity, founder);

            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.POLICY_COMPLETED,
                HistoryText.Kingdom(pKingdom) + " \u6D3E" + HistoryText.Actor(founder) +
                "\u5F00\u7586\uFF0C\u5EFA\u7ACB" + HistoryText.City(newCity, pKingdom),
                HistoryTarget.City(newCity));
            return true;
        }

        private static bool TryFindPlan(Kingdom pKingdom, out Actor pFounder, out City pSourceCity, out TileZone pZone)
        {
            pFounder = null;
            pSourceCity = null;
            pZone = null;

            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            if (!KingdomPolicyService.IsPolicyEnabledForKingdom(pKingdom)) return false;
            if (!WorldLawLibrary.world_law_kingdom_expansion.isEnabled()) return false;
            if (!pKingdom.hasKing()) return false;
            if (HasActiveWar(pKingdom)) return false;
            if (pKingdom.countCities() >= pKingdom.getMaxCities()) return false;

            pFounder = FindFounder(pKingdom);
            if (pFounder == null) return false;

            World.world.city_zone_helper.city_place_finder.recalc();
            if (!World.world.city_zone_helper.city_place_finder.hasPossibleZones()) return false;

            return TryFindZone(pKingdom, pFounder, out pSourceCity, out pZone);
        }

        private static Actor FindFounder(Kingdom pKingdom)
        {
            Actor king = pKingdom?.king;
            if (king?.data == null) return null;

            Actor best = null;
            double bestTime = double.MaxValue;
            foreach (Actor child in king.getChildren(pOnlyCurrentFamily: false))
            {
                if (!IsFounderCandidate(pKingdom, king, child)) continue;
                if (child.data.created_time >= bestTime) continue;
                best = child;
                bestTime = child.data.created_time;
            }
            return best;
        }

        private static bool IsFounderCandidate(Kingdom pKingdom, Actor pKing, Actor pActor)
        {
            if (pActor?.data == null || pKing?.data == null) return false;
            if (pActor.isRekt() || !pActor.isAlive()) return false;
            if (!pActor.isAdult() || !pActor.isSexMale()) return false;
            if (pActor.kingdom != pKingdom) return false;
            if (pActor.isKing() || pActor.isCityLeader() || pActor.isArmyGroupLeader()) return false;
            if (pActor.hasArmy()) return false;
            if (HeirService.IsCurrentHeir(pKingdom, pActor)) return false;
            if (IsEldestLivingAdultSon(pKing, pActor)) return false;

            if (LineageService.IsXia(pActor) && !LineageService.IsEnfeoffmentCandidate(pActor)) return false;
            return true;
        }

        private static bool IsEldestLivingAdultSon(Actor pKing, Actor pActor)
        {
            Actor eldest = null;
            double earliest = double.MaxValue;
            foreach (Actor child in pKing.getChildren(pOnlyCurrentFamily: false))
            {
                if (child?.data == null || child.isRekt() || !child.isAlive()) continue;
                if (!child.isAdult() || !child.isSexMale()) continue;
                if (child.kingdom != pKing.kingdom) continue;
                if (child.data.created_time >= earliest) continue;
                eldest = child;
                earliest = child.data.created_time;
            }
            return eldest == pActor;
        }

        private static bool TryFindZone(Kingdom pKingdom, Actor pFounder, out City pSourceCity, out TileZone pZone)
        {
            pSourceCity = null;
            pZone = null;

            foreach (City city in pKingdom.getCities())
            {
                if (!CanExpandFrom(city)) continue;
                TileZone zone = FindZoneFromCity(city, pFounder);
                if (zone == null) continue;
                pSourceCity = city;
                pZone = zone;
                return true;
            }

            return false;
        }

        private static bool CanExpandFrom(City pCity)
        {
            return pCity?.data != null &&
                   pCity.isAlive() &&
                   pCity.getTile() != null &&
                   pCity.status.population_adults >= 30 &&
                   !pCity.needSettlers();
        }

        private static TileZone FindZoneFromCity(City pCity, Actor pFounder)
        {
            WorldTile cityTile = pCity?.getTile();
            if (cityTile == null) return null;

            TileZone best = null;
            int bestScore = int.MaxValue;
            foreach (TileZone zone in World.world.city_zone_helper.city_place_finder.zones)
            {
                if (zone?.centerTile == null || zone.hasCity()) continue;
                bool sameIsland = cityTile.isSameIsland(zone.centerTile);
                if (!sameIsland && !pCity.hasTransportBoats()) continue;
                if (!sameIsland && !cityTile.reachableFrom(zone.centerTile)) continue;
                if (!zone.isGoodForNewCity(pFounder)) continue;

                int dist = Toolbox.SquaredDistVec2(zone.centerTile.pos, cityTile.pos);
                int score = sameIsland ? dist : dist + 1000000;
                if (score >= bestScore) continue;
                best = zone;
                bestScore = score;
            }
            return best;
        }

        private static void MoveSettlers(City pSourceCity, City pNewCity, Actor pFounder)
        {
            if (pSourceCity?.data == null || pNewCity?.data == null || pSourceCity == pNewCity) return;

            int moved = 1;
            long heirId = HeirService.PeekRegisteredHeir(pSourceCity.kingdom)?.data?.id ?? -1L;
            foreach (Actor unit in pSourceCity.units.LoopRandom())
            {
                if (moved >= MAX_MOVED_SETTLERS) break;
                if (!CanMoveSettler(unit, pFounder, heirId, pNewCity)) continue;
                unit.stopBeingWarrior();
                unit.joinCity(pNewCity);
                unit.cancelAllBeh();
                moved++;
            }
        }

        private static bool CanMoveSettler(Actor pUnit, Actor pFounder, long pHeirId, City pNewCity)
        {
            if (pUnit?.data == null || pUnit == pFounder) return false;
            if (pUnit.data.id == pHeirId) return false;
            if (pUnit.isRekt() || !pUnit.isAlive() || !pUnit.isAdult()) return false;
            if (pUnit.isKing() || pUnit.isCityLeader() || pUnit.isArmyGroupLeader()) return false;
            if (pUnit.hasArmy()) return false;
            if (pUnit.city == pNewCity) return false;
            return true;
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            try
            {
                if (pKingdom.hasEnemies()) return true;
                foreach (var _ in pKingdom.getWars()) return true;
                return false;
            }
            catch { return false; }
        }
    }
}
