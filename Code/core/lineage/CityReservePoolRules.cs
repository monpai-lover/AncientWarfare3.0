using System;

namespace AncientWarfare3.core.lineage
{
    public static class CityReservePoolRules
    {
        public const int PeaceCityBudget = 1;
        public const int PreparationCityBudget = 4;
        public const int PeaceActorBudget = 8;
        public const int PreparationActorBudget = 32;
        public const int ReserveExhaustionContribution = 20;

        public static int Capacity(int population, int effectiveWarriorSlots)
        {
            return CityArmyReinforcementRules.CityCapacity(population,
                effectiveWarriorSlots);
        }

        public static bool CanEnroll(bool alive, bool adult,
            bool localResident, bool baseEligible, bool frozen,
            int memberCount, int capacity)
        {
            return alive && adult && localResident && baseEligible &&
                   !frozen && Math.Max(0, memberCount) <
                   Math.Max(0, capacity);
        }

        public static int CityBudget(bool preparation)
        {
            return preparation ? PreparationCityBudget : PeaceCityBudget;
        }

        public static int ActorBudget(bool preparation)
        {
            return preparation ? PreparationActorBudget : PeaceActorBudget;
        }

        public static bool CanMaintain(bool frozen, bool worldDayChanged)
        {
            return !frozen && worldDayChanged;
        }

        public static bool ShouldUnfreeze(int activeWarCount)
        {
            return activeWarCount <= 0;
        }

        public static bool ShouldApplyReserveExhaustion(
            bool attackAssignment, int reinforcementShortage,
            bool kingdomFrozen, bool exhaustionConfirmed,
            bool alreadyApplied)
        {
            return attackAssignment && reinforcementShortage > 0 &&
                   kingdomFrozen && exhaustionConfirmed && !alreadyApplied;
        }

        public static int ComposeExhaustion(int baseExhaustion,
            int reserveContribution)
        {
            return Math.Max(0, Math.Min(100,
                Math.Max(0, baseExhaustion) +
                Math.Max(0, reserveContribution)));
        }
    }
}
