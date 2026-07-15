using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRetreatService
    {
        public static bool ShouldStopAttack(Actor pActor)
        {
            if (pActor?.data == null || pActor.isRekt()) return false;
            Army army = pActor.army;
            City sourceCity = pActor.city;
            City targetCity = sourceCity?.target_attack_city ?? sourceCity?.target_attack_zone?.city;
            if (army?.data == null || sourceCity?.data == null || targetCity?.data == null) return false;

            int year = Date.getCurrentYear();
            army.data.get(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR, out int retreatUntil, -1);
            if (ArmyRetreatRules.ShouldSkipAttackWhileRetreating(retreatUntil, year))
            {
                SendArmyToRetreatCity(army, FindRetreatCity(army, pActor.kingdom, sourceCity, targetCity));
                return true;
            }

            string role = AWArmyService.GetRole(army);
            int currentUnits = CountAliveUnits(army);
            long targetId = targetCity.id;
            army.data.get(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID, out long storedTargetId, -1L);
            army.data.get(LineageKeys.AW_ARMY_RETREAT_BASELINE, out int baselineUnits, 0);
            if (ArmyRetreatRules.ShouldResetBaseline(storedTargetId, targetId, baselineUnits, currentUnits))
            {
                baselineUnits = currentUnits;
                army.data.set(LineageKeys.AW_ARMY_RETREAT_BASELINE, baselineUnits);
                army.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID, targetId);
            }

            Actor captain = SafeCaptain(army);
            bool captainAlive = captain?.data != null && !captain.isRekt();
            if (ShouldProtectOccupation(targetCity, pActor.kingdom)) return false;
            bool shouldRetreat = ArmyRetreatRules.ShouldRetreat(
                role,
                baselineUnits,
                currentUnits,
                captainAlive,
                pIsAttacking: true,
                pCooldownActive: false);
            if (!shouldRetreat) return false;

            BeginRetreat(army, pActor.kingdom, sourceCity, targetCity, year);
            return true;
        }

        private static bool ShouldProtectOccupation(City pTargetCity, Kingdom pAttacker)
        {
            if (pTargetCity?.data == null || pAttacker?.data == null) return false;
            try
            {
                bool activeUnits = pTargetCity.isGettingCapturedBy(pAttacker);
                bool noDefenders = !CityOccupationAccelerationService.HasActiveDefenders(pTargetCity);
                bool ownershipChanged = pTargetCity.kingdom == pAttacker ||
                                        !pAttacker.isEnemy(pTargetCity.kingdom);
                CityOccupationAccelerationService.DescribeCaptureFor(
                    pTargetCity, pAttacker, out bool attackerIsDominant, out bool hostileRivalActive);
                return ArmyRetreatRules.ProtectUncontestedOccupation(
                    attackerIsDominant, activeUnits, noDefenders, hostileRivalActive, ownershipChanged);
            }
            catch { return false; }
        }

        private static void BeginRetreat(Army pArmy, Kingdom pKingdom, City pSourceCity, City pTargetCity, int pYear)
        {
            if (pArmy?.data == null) return;
            City retreatCity = FindRetreatCity(pArmy, pKingdom, pSourceCity, pTargetCity);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_UNTIL_YEAR, pYear + ArmyRetreatRules.RetreatCooldownYears);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_CITY_ID, retreatCity?.id ?? -1L);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_BASELINE, 0);
            pArmy.data.set(LineageKeys.AW_ARMY_RETREAT_TARGET_CITY_ID, -1L);

            if (pSourceCity?.data != null && (pSourceCity.target_attack_city == pTargetCity ||
                                              pSourceCity.target_attack_zone?.city == pTargetCity))
            {
                pSourceCity.target_attack_city = null;
                pSourceCity.target_attack_zone = null;
            }

            SendArmyToRetreatCity(pArmy, retreatCity);
        }

        private static void SendArmyToRetreatCity(Army pArmy, City pRetreatCity)
        {
            if (pArmy?.data == null || pRetreatCity?.data == null) return;
            WorldTile tile = SafeCityTile(pRetreatCity);
            if (tile == null) return;

            foreach (Actor unit in SafeUnits(pArmy))
            {
                if (unit?.data == null || unit.isRekt()) continue;
                try
                {
                    if (unit.current_tile != null && !tile.isSameIsland(unit.current_tile)) continue;
                    unit.beh_tile_target = tile;
                    unit.makeWait(0.2f);
                }
                catch { }
            }
        }

        private static City FindRetreatCity(Army pArmy, Kingdom pKingdom, City pSourceCity, City pTargetCity)
        {
            Kingdom kingdom = pKingdom?.data != null ? pKingdom : SafeKingdom(pArmy, pSourceCity);
            City fallback = pSourceCity?.kingdom == kingdom ? pSourceCity : kingdom?.capital;
            if (kingdom?.data == null) return fallback;

            City best = null;
            float bestScore = float.MaxValue;
            Vector2 origin = pTargetCity?.city_center ?? pSourceCity?.city_center ?? Vector2.zero;
            try
            {
                foreach (City city in kingdom.getCities())
                {
                    if (city?.data == null || city.isRekt() || city == pTargetCity) continue;
                    float score = (city.city_center - origin).sqrMagnitude;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    best = city;
                }
            }
            catch { }

            return best ?? fallback;
        }

        private static int CountAliveUnits(Army pArmy)
        {
            int count = 0;
            try
            {
                foreach (Actor unit in pArmy.getUnits())
                    if (unit?.data != null && !unit.isRekt())
                        count++;
                return count;
            }
            catch
            {
                try { return pArmy.countUnits(); }
                catch { return 0; }
            }
        }

        private static Actor SafeCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy.getCaptain();
                return captain?.data != null && !captain.isRekt() ? captain : null;
            }
            catch { return null; }
        }

        private static Kingdom SafeKingdom(Army pArmy, City pSourceCity)
        {
            try
            {
                Kingdom kingdom = pArmy?.getKingdom();
                if (kingdom?.data != null) return kingdom;
            }
            catch { }
            return pSourceCity?.kingdom;
        }

        private static WorldTile SafeCityTile(City pCity)
        {
            try { return pCity.getTile(); }
            catch { return null; }
        }

        private static List<Actor> SafeUnits(Army pArmy)
        {
            try { return new List<Actor>(pArmy.getUnits()); }
            catch { return new List<Actor>(); }
        }
    }
}
