using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public sealed class CityReserveDonorFacts
    {
        public CityReserveDonorFacts(long cityId, bool preferred,
            long distanceSquared)
        {
            CityId = cityId;
            Preferred = preferred;
            DistanceSquared = Math.Max(0L, distanceSquared);
        }

        public long CityId { get; }
        public bool Preferred { get; }
        public long DistanceSquared { get; }
    }

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

        public static IReadOnlyList<long> OrderDonorCityIds(
            IReadOnlyList<CityReserveDonorFacts> donors)
        {
            var ordered = new List<CityReserveDonorFacts>(
                donors?.Count ?? 0);
            if (donors != null)
                for (int i = 0; i < donors.Count; i++)
                    if (donors[i] != null && donors[i].CityId >= 0L)
                        ordered.Add(donors[i]);
            ordered.Sort(CompareDonors);
            var ids = new List<long>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
                ids.Add(ordered[i].CityId);
            return ids;
        }

        public static bool TryTakeNextActorId(SortedSet<long> actorIds,
            out long actorId)
        {
            actorId = -1L;
            if (actorIds == null || actorIds.Count == 0) return false;
            actorId = actorIds.Min;
            actorIds.Remove(actorId);
            return true;
        }

        public static bool CanConsumeFromCity(bool kingdomFrozen,
            bool realmControlled, int population)
        {
            return kingdomFrozen && realmControlled &&
                   WartimeRecruitmentPopulationRules.RecruitmentCapacity(
                       population, 1) > 0;
        }

        public static bool CanConfirmExhausted(bool kingdomFrozen,
            bool allIndexedCitiesChecked, int remainingIndexedActors)
        {
            return kingdomFrozen && allIndexedCitiesChecked &&
                   Math.Max(0, remainingIndexedActors) == 0;
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

        private static int CompareDonors(CityReserveDonorFacts left,
            CityReserveDonorFacts right)
        {
            int preferred = right.Preferred.CompareTo(left.Preferred);
            if (preferred != 0) return preferred;
            int distance = left.DistanceSquared.CompareTo(
                right.DistanceSquared);
            return distance != 0
                ? distance
                : left.CityId.CompareTo(right.CityId);
        }
    }
}
