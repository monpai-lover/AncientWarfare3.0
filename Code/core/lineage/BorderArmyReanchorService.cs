using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class BorderArmyReanchorService
    {
        public static void ReanchorExistingArmies(IReadOnlyList<City> pTargets,
            HashSet<long> pAllowedOwnerIds, Func<City, WorldTile> pPatrolSelector = null,
            Action<Actor> pPrepareActor = null)
        {
            if (pTargets == null || pTargets.Count == 0 || pAllowedOwnerIds == null || pAllowedOwnerIds.Count == 0 ||
                World.world?.armies == null) return;
            var armies = new List<Army>();
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive() ||
                    !AWArmyService.IsRoleArmy(army, AWArmyRole.BorderArmy)) continue;
                Kingdom owner = SafeOwner(army);
                if (owner?.data != null && pAllowedOwnerIds.Contains(owner.id)) armies.Add(army);
            }

            var used = new HashSet<long>();
            foreach (City city in pTargets)
            {
                Kingdom owner = city?.kingdom;
                if (city?.data == null || owner?.data == null ||
                    !pAllowedOwnerIds.Contains(owner.id)) continue;
                Army existing = AWArmyService.FindArmy(owner, city, AWArmyRole.BorderArmy);
                if (existing != null)
                {
                    used.Add(existing.id);
                    MoveToPatrol(existing, city, pPatrolSelector, pPrepareActor);
                    continue;
                }
                Army candidate = PickSameOwner(armies, used, owner);
                if (candidate == null) continue;
                AWArmyService.ReanchorArmy(candidate, owner, city, AWArmyRole.BorderArmy,
                    BuildName(owner, city));
                MoveToPatrol(candidate, city, pPatrolSelector, pPrepareActor);
                used.Add(candidate.id);
            }

            foreach (Army army in armies)
            {
                if (army?.data == null || used.Contains(army.id)) continue;
                City anchor = AWArmyService.FindAnchorCity(army);
                bool retained = false;
                foreach (City target in pTargets)
                    if (target == anchor) { retained = true; break; }
                if (!retained) Release(army);
            }
        }

        private static Army PickSameOwner(List<Army> pArmies, HashSet<long> pUsed, Kingdom pOwner)
        {
            foreach (Army army in pArmies)
                if (army?.data != null && !pUsed.Contains(army.id) && SafeOwner(army) == pOwner)
                    return army;
            return null;
        }

        private static void MoveToPatrol(Army pArmy, City pCity,
            Func<City, WorldTile> pPatrolSelector, Action<Actor> pPrepareActor)
        {
            WorldTile patrol = null;
            try { patrol = pPatrolSelector?.Invoke(pCity) ?? pCity?.getTile(); } catch { }
            if (patrol == null) return;
            try
            {
                foreach (Actor actor in pArmy.getUnits())
                {
                    if (actor?.data == null || actor.isRekt()) continue;
                    pPrepareActor?.Invoke(actor);
                    if (actor.current_tile != null && actor.current_tile.isSameIsland(patrol)) actor.goTo(patrol);
                }
            }
            catch { }
        }

        private static void Release(Army pArmy)
        {
            if (pArmy?.data == null) return;
            var units = new List<Actor>();
            try { foreach (Actor unit in pArmy.getUnits()) if (unit?.data != null && !unit.isRekt()) units.Add(unit); }
            catch { }
            foreach (Actor unit in units)
            {
                unit.data.set(LineageKeys.MANDATE_BORDER_GUARD, false);
                try { unit.removeFromArmy(); } catch { unit.setArmy(null); }
            }
            try { pArmy.setCaptain(null); } catch { }
            try { World.world?.armies?.removeObject(pArmy); } catch { }
        }

        private static Kingdom SafeOwner(Army pArmy)
        {
            try { return pArmy?.getKingdom(); } catch { }
            try { return AWArmyService.FindAnchorCity(pArmy)?.kingdom; } catch { return null; }
        }

        private static string BuildName(Kingdom pOwner, City pCity)
        {
            string name = AWArmyRoleRules.DisplayName(AWArmyRole.BorderArmy, pOwner?.name ?? "", 1);
            string cityName = pCity?.data?.name;
            return string.IsNullOrEmpty(cityName) ? name : cityName + " " + name;
        }
    }
}
