using System;

namespace AncientWarfare3.core.lineage
{
    internal static class CityArmyReinforcementService
    {
        internal static int ApprovedTarget(Army pArmy, Kingdom pKingdom)
        {
            int living = SafeLiving(pArmy);
            if (pArmy?.data == null || pKingdom?.data == null ||
                AWArmyService.IsSpecialArmy(pArmy)) return living;
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            if (!IsValidAnchor(anchor, pKingdom)) return living;
            if (World.world?.armies == null) return living;

            int population = SafePopulation(anchor);
            int slots = EffectiveWarriorSlots(anchor, pKingdom);
            int capacity = CityArmyReinforcementRules.CityCapacity(population,
                slots);
            if (!ArmyFieldIndexService.TryGetCityArmy(anchor,
                    out Army canonical) || canonical != pArmy)
                return living;
            return Math.Max(living, capacity);
        }

        private static bool IsValidAnchor(City pCity, Kingdom pKingdom)
        {
            return pCity?.data != null && !pCity.isRekt() &&
                   ReferenceEquals(pCity.kingdom, pKingdom);
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int EffectiveWarriorSlots(City pCity,
            Kingdom pKingdom)
        {
            int slots = 0;
            try { slots = pCity?.status?.warrior_slots ?? 0; }
            catch { }
            return Math.Max(0, MandateMilitaryPhaseService.
                EffectiveWarriorSlots(pKingdom, slots));
        }

        private static int SafeLiving(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

    }
}
