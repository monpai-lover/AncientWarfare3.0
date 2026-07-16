using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRetreatService
    {
        private const int CaptainMutationBudget = 1;

        private sealed class RetreatState
        {
            public long TargetCityId = -1L;
        }

        private static readonly Dictionary<long, RetreatState> RetreatStates =
            new Dictionary<long, RetreatState>();

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
                ScheduleArmyRetreat(army,
                    FindRetreatCity(army, pActor.kingdom, sourceCity, targetCity));
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

            ScheduleArmyRetreat(pArmy, retreatCity);
        }

        public static void ClearRuntime()
        {
            RetreatStates.Clear();
        }

        private static void ScheduleArmyRetreat(Army pArmy, City pRetreatCity)
        {
            if (pArmy?.data == null || pRetreatCity?.data == null) return;
            if (!RetreatStates.TryGetValue(pArmy.id, out RetreatState state))
            {
                state = new RetreatState();
                RetreatStates[pArmy.id] = state;
            }
            if (state.TargetCityId != pRetreatCity.id)
                state.TargetCityId = pRetreatCity.id;
            EnqueueRetreatBatch(pArmy.id);
        }

        private static void EnqueueRetreatBatch(long pArmyId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("army_retreat", pArmyId),
                DeferredWorkClass.Runtime,
                () => ProcessRetreatBatch(pArmyId));
        }

        private static void ProcessRetreatBatch(long pArmyId)
        {
            if (!RetreatStates.TryGetValue(pArmyId, out RetreatState state)) return;
            Army army = ResolveArmy(pArmyId);
            City retreatCity = ResolveCity(state.TargetCityId);
            WorldTile tile = SafeCityTile(retreatCity);
            if (army?.data == null || tile == null)
            {
                RetreatStates.Remove(pArmyId);
                return;
            }
            Actor captain = SafeCaptain(army);
            if (captain?.current_tile == null)
            {
                RetreatStates.Remove(pArmyId);
                return;
            }
            try
            {
                if (CaptainMutationBudget > 0 && tile.isSameIsland(captain.current_tile))
                    captain.goTo(tile, pLimitPathfindingRegions: 6);
            }
            catch { }
            RetreatStates.Remove(pArmyId);
        }

        private static City FindRetreatCity(Army pArmy, Kingdom pKingdom, City pSourceCity, City pTargetCity)
        {
            Kingdom kingdom = pKingdom?.data != null ? pKingdom : SafeKingdom(pArmy, pSourceCity);
            City fallback = pSourceCity?.kingdom == kingdom ? pSourceCity : kingdom?.capital;
            if (kingdom?.data == null) return fallback;
            if (IsValidRetreatCity(fallback, kingdom, pTargetCity)) return fallback;
            City anchor = null;
            try { anchor = AWArmyService.FindAnchorCity(pArmy); } catch { }
            if (IsValidRetreatCity(anchor, kingdom, pTargetCity)) return anchor;
            if (IsValidRetreatCity(kingdom.capital, kingdom, pTargetCity)) return kingdom.capital;
            City first = kingdom.cities.Count > 0 ? kingdom.cities[0] : null;
            return IsValidRetreatCity(first, kingdom, pTargetCity) ? first : fallback;
        }

        private static int CountAliveUnits(Army pArmy)
        {
            if (pArmy?.data == null) return 0;
            try { return Math.Max(0, pArmy.countUnits()); }
            catch { return 0; }
        }

        private static bool IsValidRetreatCity(City pCity, Kingdom pKingdom, City pTargetCity)
        {
            return pCity?.data != null && !pCity.isRekt() && pCity != pTargetCity &&
                   pCity.kingdom == pKingdom;
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

        private static Army ResolveArmy(long pId)
        {
            try { return pId >= 0 ? World.world?.armies?.get(pId) : null; }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return pId >= 0 ? World.world?.cities?.get(pId) : null; }
            catch { return null; }
        }
    }
}
