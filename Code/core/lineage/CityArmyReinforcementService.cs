using System;
using System.Collections.Generic;

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
            var requests = new List<CityArmyReinforcementRequest>();
            foreach (Army candidate in World.world.armies)
            {
                if (!IsAnchoredOrdinaryArmy(candidate, pKingdom, anchor))
                    continue;
                int candidateLiving = SafeLiving(candidate);
                requests.Add(new CityArmyReinforcementRequest(candidate.id,
                    candidateLiving, slots, Priority(candidate)));
            }

            CityArmyReinforcementAllocation[] allocations =
                CityArmyReinforcementRules.Allocate(capacity, requests);
            for (int i = 0; i < allocations.Length; i++)
                if (allocations[i].ArmyId == pArmy.id)
                    return allocations[i].ApprovedTarget;
            return living;
        }

        private static bool IsAnchoredOrdinaryArmy(Army pArmy,
            Kingdom pKingdom, City pAnchor)
        {
            if (pArmy?.data == null || !pArmy.isAlive() ||
                AWArmyService.IsSpecialArmy(pArmy)) return false;
            City anchor = AWArmyService.FindAnchorCity(pArmy);
            if (anchor?.data == null || anchor.id != pAnchor.id) return false;
            return ReferenceEquals(AWArmyService.GetIntendedKingdom(pArmy,
                anchor), pKingdom);
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

        private static CityArmyPriority Priority(Army pArmy)
        {
            if (!ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission))
                return CityArmyPriority.Reserve;
            switch (mission.ProposalKind)
            {
                case ArmyRtsProposalKind.Defend:
                case ArmyRtsProposalKind.FrontHold:
                    return CityArmyPriority.Frontline;
                case ArmyRtsProposalKind.Attack:
                    return CityArmyPriority.War;
                default:
                    return mission.WarId >= 0L
                        ? CityArmyPriority.War
                        : CityArmyPriority.Reserve;
            }
        }
    }
}
